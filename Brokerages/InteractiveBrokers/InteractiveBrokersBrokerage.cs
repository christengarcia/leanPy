/*
 * QUANTCONNECT.COM - Democratizing Finance, Empowering Individuals.
 * Lean Algorithmic Trading Engine v2.0. Copyright 2014 QuantConnect Corporation.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
*/

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using QuantConnect.Configuration;
using QuantConnect.Data;
using QuantConnect.Data.Market;
using QuantConnect.Interfaces;
using QuantConnect.Logging;
using QuantConnect.Orders;
using QuantConnect.Packets;
using QuantConnect.Securities;
using QuantConnect.Util;
using Order = QuantConnect.Orders.Order;
using IB = QuantConnect.Brokerages.InteractiveBrokers.Client;
using IBApi;
using NodaTime;
using Bar = QuantConnect.Data.Market.Bar;
using HistoryRequest = QuantConnect.Data.HistoryRequest;

namespace QuantConnect.Brokerages.InteractiveBrokers
{
    /// <summary>
    /// The Interactive Brokers brokerage
    /// </summary>
    public sealed class InteractiveBrokersBrokerage : Brokerage, IDataQueueHandler, IDataQueueUniverseProvider
    {
        // next valid order id for this client
        private int _nextValidId;
        // next valid client id for the gateway/tws
        private static int _nextClientId;
        // next valid request id for queries
        private int _nextRequestId;
        private int _nextTickerId;
        private volatile bool _disconnected1100Fired;

        private readonly int _port;
        private readonly string _account;
        private readonly string _host;
        private readonly int _clientId;
        private readonly IAlgorithm _algorithm;
        private readonly IOrderProvider _orderProvider;
        private readonly ISecurityProvider _securityProvider;
        private readonly IB.InteractiveBrokersClient _client;
        private readonly string _agentDescription;

        private Thread _messageProcessingThread;
        private readonly AutoResetEvent _resetEventRestartGateway = new AutoResetEvent(false);
        private readonly CancellationTokenSource _ctsRestartGateway = new CancellationTokenSource();

        // Notifies the thread reading information from Gateway/TWS whenever there are messages ready to be consumed
        private readonly EReaderSignal _signal = new EReaderMonitorSignal();

        private readonly ManualResetEvent _waitForNextValidId = new ManualResetEvent(false);
        private readonly ManualResetEvent _accountHoldingsResetEvent = new ManualResetEvent(false);

        // tracks executions before commission reports, map: execId -> execution
        private readonly ConcurrentDictionary<string, Execution> _orderExecutions = new ConcurrentDictionary<string, Execution>();
        // tracks commission reports before executions, map: execId -> commission report
        private readonly ConcurrentDictionary<string, CommissionReport> _commissionReports = new ConcurrentDictionary<string, CommissionReport>();

        // holds account properties, cash balances and holdings for the account
        private readonly InteractiveBrokersAccountData _accountData = new InteractiveBrokersAccountData();

        private readonly object _sync = new object();

        private readonly ConcurrentDictionary<string, ContractDetails> _contractDetails = new ConcurrentDictionary<string, ContractDetails>();

        private readonly InteractiveBrokersSymbolMapper _symbolMapper = new InteractiveBrokersSymbolMapper();

        // Prioritized list of exchanges used to find right futures contract
        private readonly Dictionary<string, string> _futuresExchanges = new Dictionary< string, string>
        {
            { Market.Globex, "GLOBEX" },
            { Market.NYMEX, "NYMEX" },
            { Market.CBOT, "ECBOT" },
            { Market.ICE, "NYBOT" },
            { Market.CBOE, "CFE" }
        };

        // exchange time zones by symbol
        private readonly Dictionary<Symbol, DateTimeZone> _symbolExchangeTimeZones = new Dictionary<Symbol, DateTimeZone>();

        // IB requests made through the IB-API must be limited to a maximum of 50 messages/second
        private readonly RateGate _messagingRateLimiter = new RateGate(50, TimeSpan.FromSeconds(1));

        private bool _previouslyInResetTime;

        // additional IB request information, will be matched with errors in the handler, for better error reporting
        private readonly ConcurrentDictionary<int, string> _requestInformation = new ConcurrentDictionary<int, string>();

        // when unsubscribing symbols immediately after subscribing IB returns an error (Can't find EId with tickerId:nnn),
        // so we track subscription times to ensure symbols are not unsubscribed before a minimum time span has elapsed
        private readonly Dictionary<int, DateTime> _subscriptionTimes = new Dictionary<int, DateTime>();
        private readonly TimeSpan _minimumTimespanBeforeUnsubscribe = TimeSpan.FromMilliseconds(500);

        /// <summary>
        /// Returns true if we're currently connected to the broker
        /// </summary>
        public override bool IsConnected
        {
            get
            {
                return _client != null && _client.Connected && !_disconnected1100Fired;
            }
        }

        /// <summary>
        /// Returns true if the connected user is a financial advisor
        /// </summary>
        public bool IsFinancialAdvisor => _account.Contains("F");

        /// <summary>
        /// Creates a new InteractiveBrokersBrokerage using values from configuration:
        ///     ib-account (required)
        ///     ib-host (optional, defaults to LOCALHOST)
        ///     ib-port (optional, defaults to 4001)
        ///     ib-agent-description (optional, defaults to Individual)
        /// </summary>
        /// <param name="algorithm">The algorithm instance</param>
        /// <param name="orderProvider">An instance of IOrderProvider used to fetch Order objects by brokerage ID</param>
        /// <param name="securityProvider">The security provider used to give access to algorithm securities</param>
        public InteractiveBrokersBrokerage(IAlgorithm algorithm, IOrderProvider orderProvider, ISecurityProvider securityProvider)
            : this(
                algorithm,
                orderProvider,
                securityProvider,
                Config.Get("ib-account"),
                Config.Get("ib-host", "LOCALHOST"),
                Config.GetInt("ib-port", 4001),
                Config.GetValue("ib-agent-description", IB.AgentDescription.Individual)
                )
        {
        }

        /// <summary>
        /// Creates a new InteractiveBrokersBrokerage for the specified account
        /// </summary>
        /// <param name="algorithm">The algorithm instance</param>
        /// <param name="orderProvider">An instance of IOrderProvider used to fetch Order objects by brokerage ID</param>
        /// <param name="securityProvider">The security provider used to give access to algorithm securities</param>
        /// <param name="account">The account used to connect to IB</param>
        public InteractiveBrokersBrokerage(IAlgorithm algorithm, IOrderProvider orderProvider, ISecurityProvider securityProvider, string account)
            : this(
                algorithm,
                orderProvider,
                securityProvider,
                account,
                Config.Get("ib-host", "LOCALHOST"),
                Config.GetInt("ib-port", 4001),
                Config.GetValue("ib-agent-description", IB.AgentDescription.Individual)
                )
        {
        }

        /// <summary>
        /// Creates a new InteractiveBrokersBrokerage from the specified values
        /// </summary>
        /// <param name="algorithm">The algorithm instance</param>
        /// <param name="orderProvider">An instance of IOrderProvider used to fetch Order objects by brokerage ID</param>
        /// <param name="securityProvider">The security provider used to give access to algorithm securities</param>
        /// <param name="account">The Interactive Brokers account name</param>
        /// <param name="host">host name or IP address of the machine where TWS is running. Leave blank to connect to the local host.</param>
        /// <param name="port">must match the port specified in TWS on the Configure&gt;API&gt;Socket Port field.</param>
        /// <param name="agentDescription">Used for Rule 80A describes the type of trader.</param>
        public InteractiveBrokersBrokerage(IAlgorithm algorithm, IOrderProvider orderProvider, ISecurityProvider securityProvider, string account, string host, int port, string agentDescription = IB.AgentDescription.Individual)
            : base("Interactive Brokers Brokerage")
        {
            _algorithm = algorithm;
            _orderProvider = orderProvider;
            _securityProvider = securityProvider;
            _account = account;
            _host = host;
            _port = port;
            _clientId = IncrementClientId();
            _agentDescription = agentDescription;

            Log.Trace($"InteractiveBrokersBrokerage.InteractiveBrokersBrokerage(): Host: {host}, Port: {port}, Account: {account}, AgentDescription: {agentDescription}");

            _client = new IB.InteractiveBrokersClient(_signal);

            // set up event handlers
            _client.UpdatePortfolio += HandlePortfolioUpdates;
            _client.OrderStatus += HandleOrderStatusUpdates;
            _client.OpenOrder += HandleOpenOrder;
            _client.OpenOrderEnd += HandleOpenOrderEnd;
            _client.UpdateAccountValue += HandleUpdateAccountValue;
            _client.ExecutionDetails += HandleExecutionDetails;
            _client.CommissionReport += HandleCommissionReport;
            _client.Error += HandleError;
            _client.TickPrice += HandleTickPrice;
            _client.TickSize += HandleTickSize;
            _client.CurrentTimeUtc += HandleBrokerTime;

            // we need to wait until we receive the next valid id from the server
            _client.NextValidId += (sender, e) =>
            {
                // only grab this id when we initialize, and we'll manually increment it here to avoid threading issues
                if (_nextValidId == 0)
                {
                    _nextValidId = e.OrderId;
                    _waitForNextValidId.Set();
                }
                Log.Trace("InteractiveBrokersBrokerage.HandleNextValidID(): " + e.OrderId);
            };

            // handle requests to restart the IB gateway
            new Thread(() =>
            {
                try
                {
                    Log.Trace("InteractiveBrokersBrokerage.ResetHandler(): thread started.");

                    while (!_ctsRestartGateway.IsCancellationRequested)
                    {
                        if (_resetEventRestartGateway.WaitOne(1000, _ctsRestartGateway.Token))
                        {
                            Log.Trace("InteractiveBrokersBrokerage.ResetHandler(): Reset sequence start.");

                            try
                            {
                                ResetGatewayConnection();
                            }
                            catch (Exception exception)
                            {
                                Log.Error("InteractiveBrokersBrokerage.ResetHandler(): Error in ResetGatewayConnection: " + exception);
                            }

                            Log.Trace("InteractiveBrokersBrokerage.ResetHandler(): Reset sequence end.");
                        }
                    }

                    Log.Trace("InteractiveBrokersBrokerage.ResetHandler(): thread ended.");
                }
                catch (Exception exception)
                {
                    Log.Error("InteractiveBrokersBrokerage.ResetHandler(): Error in reset handler thread: " + exception);
                }
            }) { IsBackground = true }.Start();
        }

        /// <summary>
        /// Provides public access to the underlying IBClient instance
        /// </summary>
        public IB.InteractiveBrokersClient Client
        {
            get { return _client; }
        }

        /// <summary>
        /// Places a new order and assigns a new broker ID to the order
        /// </summary>
        /// <param name="order">The order to be placed</param>
        /// <returns>True if the request for a new order has been placed, false otherwise</returns>
        public override bool PlaceOrder(Order order)
        {
            try
            {
                Log.Trace("InteractiveBrokersBrokerage.PlaceOrder(): Symbol: " + order.Symbol.Value + " Quantity: " + order.Quantity);

                IBPlaceOrder(order, true);
                return true;
            }
            catch (Exception err)
            {
                Log.Error("InteractiveBrokersBrokerage.PlaceOrder(): " + err);
                return false;
            }
        }

        /// <summary>
        /// Updates the order with the same id
        /// </summary>
        /// <param name="order">The new order information</param>
        /// <returns>True if the request was made for the order to be updated, false otherwise</returns>
        public override bool UpdateOrder(Order order)
        {
            try
            {
                Log.Trace("InteractiveBrokersBrokerage.UpdateOrder(): Symbol: " + order.Symbol.Value + " Quantity: " + order.Quantity + " Status: " + order.Status);

                IBPlaceOrder(order, false);
            }
            catch (Exception err)
            {
                Log.Error("InteractiveBrokersBrokerage.UpdateOrder(): " + err);
                return false;
            }
            return true;
        }

        /// <summary>
        /// Cancels the order with the specified ID
        /// </summary>
        /// <param name="order">The order to cancel</param>
        /// <returns>True if the request was made for the order to be canceled, false otherwise</returns>
        public override bool CancelOrder(Order order)
        {
            try
            {
                Log.Trace("InteractiveBrokersBrokerage.CancelOrder(): Symbol: " + order.Symbol.Value + " Quantity: " + order.Quantity);

                // this could be better
                foreach (var id in order.BrokerId)
                {
                    var orderId = int.Parse(id);

                    _requestInformation[orderId] = "CancelOrder: " + order;

                    _messagingRateLimiter.WaitToProceed();

                    _client.ClientSocket.cancelOrder(orderId);
                }

                // canceled order events fired upon confirmation, see HandleError
            }
            catch (Exception err)
            {
                Log.Error("InteractiveBrokersBrokerage.CancelOrder(): OrderID: " + order.Id + " - " + err);
                return false;
            }
            return true;
        }

        /// <summary>
        /// Gets all open orders on the account
        /// </summary>
        /// <returns>The open orders returned from IB</returns>
        public override List<Order> GetOpenOrders()
        {
            var orders = new List<Order>();

            var manualResetEvent = new ManualResetEvent(false);

            // define our handlers
            EventHandler<IB.OpenOrderEventArgs> clientOnOpenOrder = (sender, args) =>
            {
                // convert IB order objects returned from RequestOpenOrders
                orders.Add(ConvertOrder(args.Order, args.Contract));
            };
            EventHandler clientOnOpenOrderEnd = (sender, args) =>
            {
                // this signals the end of our RequestOpenOrders call
                manualResetEvent.Set();
            };

            _client.OpenOrder += clientOnOpenOrder;
            _client.OpenOrderEnd += clientOnOpenOrderEnd;

            _messagingRateLimiter.WaitToProceed();

            _client.ClientSocket.reqAllOpenOrders();

            // wait for our end signal
            if (!manualResetEvent.WaitOne(15000))
            {
                throw new TimeoutException("InteractiveBrokersBrokerage.GetOpenOrders(): Operation took longer than 15 seconds.");
            }

            // remove our handlers
            _client.OpenOrder -= clientOnOpenOrder;
            _client.OpenOrderEnd -= clientOnOpenOrderEnd;

            return orders;
        }

        /// <summary>
        /// Gets all holdings for the account
        /// </summary>
        /// <returns>The current holdings from the account</returns>
        public override List<Holding> GetAccountHoldings()
        {
            CheckIbGateway();

            if (!IsConnected)
            {
                Log.Trace("InteractiveBrokersBrokerage.GetAccountHoldings(): not connected, connecting now");
                Connect();
            }

            var holdings = _accountData.AccountHoldings.Select(x => ObjectActivator.Clone(x.Value)).Where(x => x.Quantity != 0).ToList();

            // fire up tasks to resolve the conversion rates so we can do them in parallel
            var tasks = holdings.Select(local =>
            {
                // we need to resolve the conversion rate for non-USD currencies
                if (local.Type != SecurityType.Forex)
                {
                    // this assumes all non-forex are us denominated, we should add the currency to 'holding'
                    local.ConversionRate = 1m;
                    return null;
                }
                // if quote currency is in USD don't bother making the request
                var currency = local.Symbol.Value.Substring(3);
                if (currency == "USD")
                {
                    local.ConversionRate = 1m;
                    return null;
                }

                // this will allow us to do this in parallel
                return Task.Factory.StartNew(() => local.ConversionRate = GetUsdConversion(currency));
            }).Where(x => x != null).ToArray();

            Task.WaitAll(tasks, 5000);

            return holdings;
        }

        /// <summary>
        /// Gets the current cash balance for each currency held in the brokerage account
        /// </summary>
        /// <returns>The current cash balance for each currency available for trading</returns>
        public override List<Cash> GetCashBalance()
        {
            CheckIbGateway();

            if (!IsConnected)
            {
                Log.Trace("InteractiveBrokersBrokerage.GetCashBalance(): not connected, connecting now");
                Connect();
            }

            return _accountData.CashBalances.Select(x => new Cash(x.Key, x.Value, GetUsdConversion(x.Key))).ToList();
        }

        /// <summary>
        /// Gets the execution details matching the filter
        /// </summary>
        /// <returns>A list of executions matching the filter</returns>
        public List<IB.ExecutionDetailsEventArgs> GetExecutions(string symbol, string type, string exchange, DateTime? timeSince, string side)
        {
            var filter = new ExecutionFilter
            {
                AcctCode = _account,
                ClientId = _clientId,
                Exchange = exchange,
                SecType = type ?? IB.SecurityType.Undefined,
                Symbol = symbol,
                Time = (timeSince ?? DateTime.MinValue).ToString("yyyyMMdd HH:mm:ss"),
                Side = side ?? IB.ActionSide.Undefined
            };

            var details = new List<IB.ExecutionDetailsEventArgs>();

            var manualResetEvent = new ManualResetEvent(false);

            var requestId = GetNextRequestId();

            _requestInformation[requestId] = "GetExecutions: " + symbol;

            // define our event handlers
            EventHandler<IB.RequestEndEventArgs> clientOnExecutionDataEnd = (sender, args) =>
            {
                if (args.RequestId == requestId) manualResetEvent.Set();
            };
            EventHandler<IB.ExecutionDetailsEventArgs> clientOnExecDetails = (sender, args) =>
            {
                if (args.RequestId == requestId) details.Add(args);
            };

            _client.ExecutionDetails += clientOnExecDetails;
            _client.ExecutionDetailsEnd += clientOnExecutionDataEnd;

            _messagingRateLimiter.WaitToProceed();

            // no need to be fancy with request id since that's all this client does is 1 request
            _client.ClientSocket.reqExecutions(requestId, filter);

            if (!manualResetEvent.WaitOne(5000))
            {
                throw new TimeoutException("InteractiveBrokersBrokerage.GetExecutions(): Operation took longer than 5 seconds.");
            }

            // remove our event handlers
            _client.ExecutionDetails -= clientOnExecDetails;
            _client.ExecutionDetailsEnd -= clientOnExecutionDataEnd;

            return details;
        }

        /// <summary>
        /// Connects the client to the IB gateway
        /// </summary>
        public override void Connect()
        {
            if (IsConnected) return;

            // we're going to receive fresh values for all account data, so we clear all
            _accountData.Clear();

            var attempt = 1;
            const int maxAttempts = 5;
            var existingSessionDetected = false;
            var securityDialogDetected = false;
            while (true)
            {
                try
                {
                    Log.Trace("InteractiveBrokersBrokerage.Connect(): Attempting to connect ({0}/{1}) ...", attempt, maxAttempts);

                    // if message processing thread is still running, wait until it terminates
                    Disconnect();

                    // we're going to try and connect several times, if successful break
                    _client.ClientSocket.eConnect(_host, _port, _clientId);

                    // create the message processing thread
                    var reader = new EReader(_client.ClientSocket, _signal);
                    reader.Start();

                    _messageProcessingThread = new Thread(() =>
                    {
                        Log.Trace("IB message processing thread started: #" + Thread.CurrentThread.ManagedThreadId);

                        while (_client.ClientSocket.IsConnected())
                        {
                            try
                            {
                                _signal.waitForSignal();
                                reader.processMsgs();
                            }
                            catch (Exception error)
                            {
                                // error in message processing thread, log error and disconnect
                                Log.Error("Error in message processing thread #" + Thread.CurrentThread.ManagedThreadId + ": " + error);
                            }
                        }

                        Log.Trace("IB message processing thread ended: #" + Thread.CurrentThread.ManagedThreadId);
                    }) { IsBackground = true };

                    _messageProcessingThread.Start();

                    // pause for a moment to receive next valid ID message from gateway
                    if (!_waitForNextValidId.WaitOne(15000))
                    {
                        Log.Trace("InteractiveBrokersBrokerage.Connect(): Operation took longer than 15 seconds.");

                        // no response, disconnect and retry
                        Disconnect();

                        var ibcLogContent = LoadCurrentIbControllerLogFile();

                        // if existing session detected from IBController log file, log error and throw exception
                        if (ExistingSessionDetected(ibcLogContent))
                        {
                            existingSessionDetected = true;
                            throw new Exception("InteractiveBrokersBrokerage.Connect(): An existing session was detected and will not be automatically disconnected. Please close the existing session manually.");
                        }

                        // if security dialog detected from IBController log file, log error and throw exception
                        if (SecurityDialogDetected(ibcLogContent))
                        {
                            securityDialogDetected = true;
                            throw new Exception("InteractiveBrokersBrokerage.Connect(): A security dialog was detected for Second Factor/Code Card Authentication. Please opt out of the Secure Login System: Manage Account > Security > Secure Login System > SLS Opt Out");
                        }

                        // max out at 5 attempts to connect ~1 minute
                        if (attempt++ < maxAttempts)
                        {
                            Thread.Sleep(1000);
                            continue;
                        }

                        throw new TimeoutException("InteractiveBrokersBrokerage.Connect(): Operation took longer than 15 seconds.");
                    }

                    Log.Trace("IB next valid id received.");

                    if (!_client.Connected) throw new Exception("InteractiveBrokersBrokerage.Connect(): Connection returned but was not in connected state.");

                    if (IsFinancialAdvisor)
                    {
                        if (!DownloadFinancialAdvisorAccount(_account))
                        {
                            Log.Trace("InteractiveBrokersBrokerage.Connect(): DownloadFinancialAdvisorAccount failed.");

                            Disconnect();

                            if (attempt++ < maxAttempts)
                            {
                                Thread.Sleep(1000);
                                continue;
                            }

                            throw new TimeoutException("InteractiveBrokersBrokerage.Connect(): DownloadFinancialAdvisorAccount failed.");
                        }
                    }
                    else
                    {
                        if (!DownloadAccount(_account))
                        {
                            Log.Trace("InteractiveBrokersBrokerage.Connect(): DownloadAccount failed. Operation took longer than 15 seconds.");

                            Disconnect();

                            if (attempt++ < maxAttempts)
                            {
                                Thread.Sleep(1000);
                                continue;
                            }

                            throw new TimeoutException("InteractiveBrokersBrokerage.Connect(): DownloadAccount failed.");
                        }
                    }

                    // enable detailed logging
                    _client.ClientSocket.setServerLogLevel(5);

                    break;
                }
                catch (Exception err)
                {
                    // if existing session or security dialog detected from IBController log file, log error and throw exception
                    if (existingSessionDetected || securityDialogDetected)
                    {
                        Log.Error(err);
                        throw;
                    }

                    // max out at 5 attempts to connect ~1 minute
                    if (attempt++ < maxAttempts)
                    {
                        Thread.Sleep(15000);
                        continue;
                    }

                    // we couldn't connect after several attempts, log the error and throw an exception
                    Log.Error(err);

                    throw;
                }
            }
        }

        /// <summary>
        /// Downloads the financial advisor configuration information.
        /// This method is called upon successful connection.
        /// </summary>
        private bool DownloadFinancialAdvisorAccount(string account)
        {
            if (!_accountData.FinancialAdvisorConfiguration.Load(_client))
                return false;

            // Only one account can be subscribed at a time.
            // With Financial Advisory (FA) account structures there is an alternative way of
            // specifying the account code such that information is returned for 'All' sub accounts.
            // This is done by appending the letter 'A' to the end of the account number
            // https://interactivebrokers.github.io/tws-api/account_updates.html#gsc.tab=0

            // subscribe to the FA account
            DownloadAccount(account + "A");

            return true;
        }

        /// <summary>
        /// Downloads the account information and subscribes to account updates.
        /// This method is called upon successful connection.
        /// </summary>
        private bool DownloadAccount(string account)
        {
            // define our event handler, this acts as stop to make sure when we leave Connect we have downloaded the full account
            EventHandler<IB.AccountDownloadEndEventArgs> clientOnAccountDownloadEnd = (sender, args) =>
            {
                Log.Trace("InteractiveBrokersBrokerage.DownloadAccount(): Finished account download for " + args.Account);
                _accountHoldingsResetEvent.Set();
            };
            _client.AccountDownloadEnd += clientOnAccountDownloadEnd;

            // we'll wait to get our first account update, we need to be absolutely sure we
            // have downloaded the entire account before leaving this function
            var firstAccountUpdateReceived = new ManualResetEvent(false);
            EventHandler<IB.UpdateAccountValueEventArgs> clientOnUpdateAccountValue = (sender, args) =>
            {
                firstAccountUpdateReceived.Set();
            };

            _client.UpdateAccountValue += clientOnUpdateAccountValue;

            // first we won't subscribe, wait for this to finish, below we'll subscribe for continuous updates
            _client.ClientSocket.reqAccountUpdates(true, account);

            // wait to see the first account value update
            firstAccountUpdateReceived.WaitOne(2500);

            // take pause to ensure the account is downloaded before continuing, this was added because running in
            // linux there appears to be different behavior where the account download end fires immediately.
            Thread.Sleep(2500);

            if (!_accountHoldingsResetEvent.WaitOne(15000))
            {
                // remove our event handlers
                _client.AccountDownloadEnd -= clientOnAccountDownloadEnd;
                _client.UpdateAccountValue -= clientOnUpdateAccountValue;

                Log.Trace("InteractiveBrokersBrokerage.DownloadAccount(): Operation took longer than 15 seconds.");

                return false;
            }

            // remove our event handlers
            _client.AccountDownloadEnd -= clientOnAccountDownloadEnd;
            _client.UpdateAccountValue -= clientOnUpdateAccountValue;

            return true;
        }

        /// <summary>
        /// Disconnects the client from the IB gateway
        /// </summary>
        public override void Disconnect()
        {
            _client.ClientSocket.eDisconnect();

            if (_messageProcessingThread != null)
            {
                _signal.issueSignal();
                _messageProcessingThread.Join();
                _messageProcessingThread = null;
            }
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public override void Dispose()
        {
            Log.Trace("InteractiveBrokersBrokerage.Dispose(): Disposing of IB resources.");

            if (_client != null)
            {
                Disconnect();
                _client.Dispose();
            }

            _messagingRateLimiter.Dispose();

            _ctsRestartGateway.Cancel(false);
        }

        /// <summary>
        /// Places the order with InteractiveBrokers
        /// </summary>
        /// <param name="order">The order to be placed</param>
        /// <param name="needsNewId">Set to true to generate a new order ID, false to leave it alone</param>
        /// <param name="exchange">The exchange to send the order to, defaults to "Smart" to use IB's smart routing</param>
        private void IBPlaceOrder(Order order, bool needsNewId, string exchange = null)
        {
            // connect will throw if it fails
            Connect();

            if (!IsConnected)
            {
                throw new InvalidOperationException("InteractiveBrokersBrokerage.IBPlaceOrder(): Unable to place order while not connected.");
            }

            // MOO/MOC require directed option orders
            if (exchange == null &&
                order.Symbol.SecurityType == SecurityType.Option &&
                (order.Type == OrderType.MarketOnOpen || order.Type == OrderType.MarketOnClose))
            {
                exchange = Market.CBOE.ToUpper();
            }

            var contract = CreateContract(order.Symbol, exchange);

            int ibOrderId;
            if (needsNewId)
            {
                // the order ids are generated for us by the SecurityTransactionManaer
                var id = GetNextBrokerageOrderId();
                order.BrokerId.Add(id.ToString());
                ibOrderId = id;
            }
            else if (order.BrokerId.Any())
            {
                // this is *not* perfect code
                ibOrderId = int.Parse(order.BrokerId[0]);
            }
            else
            {
                throw new ArgumentException("Expected order with populated BrokerId for updating orders.");
            }

            _requestInformation[ibOrderId] = "IBPlaceOrder: " + contract;

            _messagingRateLimiter.WaitToProceed();

            if (order.Type == OrderType.OptionExercise)
            {
                _client.ClientSocket.exerciseOptions(ibOrderId, contract, 1, decimal.ToInt32(order.Quantity), _account, 0);
            }
            else
            {
                var ibOrder = ConvertOrder(order, contract, ibOrderId);
                _client.ClientSocket.placeOrder(ibOrder.OrderId, contract, ibOrder);
            }
        }

        private static string GetUniqueKey(Contract contract)
        {
            return string.Format("{0} {1} {2} {3}", contract, contract.LastTradeDateOrContractMonth, contract.Strike, contract.Right);
        }

        private string GetPrimaryExchange(Contract contract)
        {
            ContractDetails details;
            if (_contractDetails.TryGetValue(GetUniqueKey(contract), out details))
            {
                return details.Summary.PrimaryExch;
            }

            details = GetContractDetails(contract);
            if (details == null)
            {
                // we were unable to find the contract details
                return null;
            }

            return details.Summary.PrimaryExch;
        }

        private string GetTradingClass(Contract contract)
        {
            ContractDetails details;
            if (_contractDetails.TryGetValue(GetUniqueKey(contract), out details))
            {
                return details.Summary.TradingClass;
            }

            details = GetContractDetails(contract);
            if (details == null)
            {
                // we were unable to find the contract details
                return null;
            }

            return details.Summary.TradingClass;
        }

        private decimal GetMinTick(Contract contract)
        {
            ContractDetails details;
            if (_contractDetails.TryGetValue(GetUniqueKey(contract), out details))
            {
                return (decimal) details.MinTick;
            }

            details = GetContractDetails(contract);
            if (details == null)
            {
                // we were unable to find the contract details
                return 0;
            }

            return (decimal) details.MinTick;
        }

        private ContractDetails GetContractDetails(Contract contract)
        {
            const int timeout = 60; // sec

            ContractDetails details = null;
            var requestId = GetNextRequestId();

            _requestInformation[requestId] = "GetContractDetails: " + contract;

            var manualResetEvent = new ManualResetEvent(false);

            // define our event handlers
            EventHandler<IB.ContractDetailsEventArgs> clientOnContractDetails = (sender, args) =>
            {
                // ignore other requests
                if (args.RequestId != requestId) return;
                details = args.ContractDetails;
                var uniqueKey = GetUniqueKey(contract);
                _contractDetails.TryAdd(uniqueKey, details);
                manualResetEvent.Set();
                Log.Trace("InteractiveBrokersBrokerage.GetContractDetails(): clientOnContractDetails event: " + uniqueKey);
            };

            _client.ContractDetails += clientOnContractDetails;

            _messagingRateLimiter.WaitToProceed();

            // make the request for data
            _client.ClientSocket.reqContractDetails(requestId, contract);

            if (!manualResetEvent.WaitOne(timeout * 1000))
            {
                Log.Error("InteractiveBrokersBrokerage.GetContractDetails(): failed to receive response from IB within {0} seconds", timeout);
            }

            // be sure to remove our event handlers
            _client.ContractDetails -= clientOnContractDetails;

            return details;
        }

        private string GetFuturesContractExchange(Contract contract)
        {
            // searching for available contracts on different exchanges
            var contractDetails = FindContracts(contract);

            var exchanges = _futuresExchanges.Values.Reverse().ToArray();

            // sorting list of available contracts by exchange priority, taking the top 1
            return contractDetails
                    .Select(details => details.Summary.Exchange)
                    .OrderByDescending(e => Array.IndexOf(exchanges, e))
                    .FirstOrDefault();
        }

        private IEnumerable<ContractDetails> FindContracts(Contract contract)
        {
            const int timeout = 60; // sec

            var requestId = GetNextRequestId();

            _requestInformation[requestId] = "FindContracts: " + contract;

            var manualResetEvent = new ManualResetEvent(false);
            var contractDetails = new List<ContractDetails>();

            // define our event handlers
            EventHandler<IB.ContractDetailsEventArgs> clientOnContractDetails =
                (sender, args) => contractDetails.Add(args.ContractDetails);

            EventHandler<IB.RequestEndEventArgs> clientOnContractDetailsEnd =
                (sender, args) => manualResetEvent.Set();

            _client.ContractDetails += clientOnContractDetails;
            _client.ContractDetailsEnd += clientOnContractDetailsEnd;

            _messagingRateLimiter.WaitToProceed();

            // make the request for data
            _client.ClientSocket.reqContractDetails(requestId, contract);

            if (!manualResetEvent.WaitOne(timeout * 1000))
            {
                Log.Error("InteractiveBrokersBrokerage.FindContracts(): failed to receive response from IB within {0} seconds", timeout);
            }

            // be sure to remove our event handlers
            _client.ContractDetailsEnd -= clientOnContractDetailsEnd;
            _client.ContractDetails -= clientOnContractDetails;

            return contractDetails;
        }

        /// <summary>
        /// Gets the current conversion rate into USD
        /// </summary>
        /// <remarks>Synchronous, blocking</remarks>
        private decimal GetUsdConversion(string currency)
        {
            if (currency == "USD")
            {
                return 1m;
            }

            Log.Trace("InteractiveBrokersBrokerage.GetUsdConversion(): Getting USD conversion for " + currency);

            // determine the correct symbol to choose
            var invertedSymbol = "USD" + currency;
            var normalSymbol = currency + "USD";
            var currencyPair = Currencies.CurrencyPairs.FirstOrDefault(x => x == invertedSymbol || x == normalSymbol);
            if (currencyPair == null)
            {
                throw new Exception("Unable to resolve currency conversion pair for currency: " + currency);
            }

            // is it XXXUSD or USDXXX
            bool inverted = invertedSymbol == currencyPair;
            var symbol = Symbol.Create(currencyPair, SecurityType.Forex, Market.FXCM);
            var contract = CreateContract(symbol);

            ContractDetails details;
            if (!_contractDetails.TryGetValue(GetUniqueKey(contract), out details))
            {
                details = GetContractDetails(contract);
            }

            if (details == null)
            {
                throw new Exception("Unable to resolve conversion for currency: " + currency);
            }

            // if this stays zero then we haven't received the conversion rate
            var rate = 0m;
            var manualResetEvent = new ManualResetEvent(false);

            // we're going to request ticks first and if not present,
            // we'll make a history request and use the latest value returned.

            const int requestTimeout = 60;

            // define and add our tick handler for the ticks
            var marketDataTicker = GetNextTickerId();

            _requestInformation[marketDataTicker] = "GetUsdConversion.MarketData: " + contract;

            EventHandler<IB.TickPriceEventArgs> clientOnTickPrice = (sender, args) =>
            {
                if (args.TickerId == marketDataTicker && args.Field == IBApi.TickType.ASK)
                {
                    rate = Convert.ToDecimal(args.Price);
                    Log.Trace("InteractiveBrokersBrokerage.GetUsdConversion(): Market price rate is " + args.Price + " for currency " + currency);
                    manualResetEvent.Set();
                }
            };

            Log.Trace("InteractiveBrokersBrokerage.GetUsdConversion(): Requesting market data for " + currencyPair);
            _client.TickPrice += clientOnTickPrice;

            _messagingRateLimiter.WaitToProceed();

            _client.ClientSocket.reqMktData(marketDataTicker, contract, string.Empty, true, false, new List<TagValue>());

            if (!manualResetEvent.WaitOne(requestTimeout * 1000))
            {
                Log.Error("InteractiveBrokersBrokerage.GetUsdConversion(): failed to receive response from IB within {0} seconds", requestTimeout);
            }

            _client.TickPrice -= clientOnTickPrice;

            // check to see if ticks returned something
            // we also need to check for negative values, IB returns -1 on Saturday
            if (rate <= 0)
            {
                string errorMessage;
                bool pacingViolation;
                const int pacingDelaySeconds = 60;

                do
                {
                    errorMessage = string.Empty;
                    pacingViolation = false;
                    manualResetEvent.Reset();

                    var data = new List<IB.HistoricalDataEventArgs>();
                    var historicalTicker = GetNextTickerId();

                    _requestInformation[historicalTicker] = "GetUsdConversion.Historical: " + contract;

                    EventHandler<IB.HistoricalDataEventArgs> clientOnHistoricalData = (sender, args) =>
                    {
                        if (args.RequestId == historicalTicker)
                        {
                            data.Add(args);
                        }
                    };

                    EventHandler<IB.HistoricalDataEndEventArgs> clientOnHistoricalDataEnd = (sender, args) =>
                    {
                        if (args.RequestId == historicalTicker)
                        {
                            manualResetEvent.Set();
                        }
                    };

                    EventHandler<IB.ErrorEventArgs> clientOnError = (sender, args) =>
                    {
                        if (args.Code == 162 && args.Message.Contains("pacing violation"))
                        {
                            // pacing violation happened
                            pacingViolation = true;
                        }
                        else
                        {
                            errorMessage = $"Code: {args.Code} - ErrorMessage: {args.Message}";
                        }
                    };

                    Log.Trace("InteractiveBrokersBrokerage.GetUsdConversion(): Requesting historical data for " + currencyPair);
                    _client.HistoricalData += clientOnHistoricalData;
                    _client.HistoricalDataEnd += clientOnHistoricalDataEnd;
                    _client.Error += clientOnError;

                    _messagingRateLimiter.WaitToProceed();

                    // request some historical data, IB's api takes into account weekends/market opening hours
                    const string requestSpan = "100 S";
                    _client.ClientSocket.reqHistoricalData(historicalTicker, contract, DateTime.UtcNow.ToString("yyyyMMdd HH:mm:ss UTC"),
                        requestSpan, IB.BarSize.OneSecond, HistoricalDataType.Ask, 0, 2, false, new List<TagValue>());

                    if (!manualResetEvent.WaitOne(requestTimeout * 1000))
                    {
                        Log.Error("InteractiveBrokersBrokerage.GetUsdConversion(): failed to receive response from IB within {0} seconds", requestTimeout);
                    }

                    if (pacingViolation)
                    {
                        // we received 'pacing violation' error from IB, so we have to wait
                        Log.Trace("InteractiveBrokersBrokerage.GetUsdConversion() Pacing violation, pausing for {0} secs.", pacingDelaySeconds);
                        Thread.Sleep(pacingDelaySeconds * 1000);
                    }
                    else
                    {
                        // check for history
                        var ordered = data.OrderByDescending(x => x.Bar.Time);
                        var mostRecentQuote = ordered.FirstOrDefault();
                        if (mostRecentQuote == null)
                        {
                            throw new Exception("Unable to get recent quote for " + currencyPair + " - " + errorMessage);
                        }

                        rate = Convert.ToDecimal(mostRecentQuote.Bar.Close);
                        Log.Trace("InteractiveBrokersBrokerage.GetUsdConversion(): Last historical price rate is " + rate + " for currency " + currency);
                    }

                    // be sure to unwire our history handler as well
                    _client.HistoricalData -= clientOnHistoricalData;
                    _client.HistoricalDataEnd -= clientOnHistoricalDataEnd;
                    _client.Error -= clientOnError;

                } while (pacingViolation);
            }

            return inverted ? 1 / rate : rate;
        }

        /// <summary>
        /// Handles error messages from IB
        /// </summary>
        private void HandleError(object sender, IB.ErrorEventArgs e)
        {
            // https://www.interactivebrokers.com/en/software/api/apiguide/tables/api_message_codes.htm

            var requestId = e.Id;
            var errorCode = e.Code;
            var errorMsg = e.Message;

            // rewrite these messages to be single lined
            errorMsg = errorMsg.Replace("\r\n", ". ").Replace("\r", ". ").Replace("\n", ". ");

            // if there is additional information for the originating request, append it to the error message
            string requestMessage;
            if (_requestInformation.TryGetValue(requestId, out requestMessage))
            {
                errorMsg += ". Origin: " + requestMessage;
            }

            Log.Trace($"InteractiveBrokersBrokerage.HandleError(): RequestId: {requestId} ErrorCode: {errorCode} - {errorMsg}");

            // figure out the message type based on our code collections below
            var brokerageMessageType = BrokerageMessageType.Information;
            if (ErrorCodes.Contains(errorCode))
            {
                brokerageMessageType = BrokerageMessageType.Error;
            }
            else if (WarningCodes.Contains(errorCode))
            {
                brokerageMessageType = BrokerageMessageType.Warning;
            }

            // code 1100 is a connection failure, we'll wait a minute before exploding gracefully
            if (errorCode == 1100)
            {
                if (!_disconnected1100Fired)
                {
                    _disconnected1100Fired = true;

                    // begin the try wait logic
                    TryWaitForReconnect();
                }
                else
                {
                    // The IB API sends many consecutive disconnect messages (1100) during nightly reset periods and weekends,
                    // so we send the message event only when we transition from connected to disconnected state,
                    // to avoid flooding the logs with the same message.
                    return;
                }
            }
            else if (errorCode == 1102 || errorCode == 1101)
            {
                // we've reconnected
                OnMessage(new BrokerageMessageEvent(brokerageMessageType, errorCode, errorMsg));

                // With IB Gateway v960.2a in the cloud, we are not receiving order fill events after the nightly reset,
                // so we execute the following sequence:
                // disconnect, kill IB Gateway, restart IB Gateway, reconnect, restore data subscriptions
                Log.Trace("InteractiveBrokersBrokerage.HandleError(): Reconnect message received. Restarting...");

                _resetEventRestartGateway.Set();

                return;
            }
            else if (errorCode == 506)
            {
                Log.Trace("InteractiveBrokersBrokerage.HandleError(): Server Version: " + _client.ClientSocket.ServerVersion);
            }

            if (InvalidatingCodes.Contains(errorCode))
            {
                var message = $"{errorCode} - {errorMsg}";
                Log.Trace($"InteractiveBrokersBrokerage.HandleError.InvalidateOrder(): IBOrderId: {requestId} ErrorCode: {message}");

                // invalidate the order
                var order = _orderProvider.GetOrderByBrokerageId(requestId);
                if (order != null)
                {
                    const int orderFee = 0;
                    var orderEvent = new OrderEvent(order, DateTime.UtcNow, orderFee)
                    {
                        Status = OrderStatus.Invalid,
                        Message = message
                    };
                    OnOrderEvent(orderEvent);
                }
                else
                {
                    Log.Error($"InteractiveBrokersBrokerage.HandleError.InvalidateOrder(): Unable to locate order with BrokerageID {requestId}");
                }
            }

            OnMessage(new BrokerageMessageEvent(brokerageMessageType, errorCode, errorMsg));
        }

        /// <summary>
        /// Restarts the IB Gateway and restores the connection
        /// </summary>
        public void ResetGatewayConnection()
        {
            _disconnected1100Fired = false;

            // notify the BrokerageMessageHandler before the restart, so it can stop polling
            OnMessage(BrokerageMessageEvent.Reconnected(string.Empty));

            Log.Trace("InteractiveBrokersBrokerage.ResetGatewayConnection(): Disconnecting...");
            Disconnect();

            Log.Trace("InteractiveBrokersBrokerage.ResetGatewayConnection(): Stopping IB Gateway...");
            InteractiveBrokersGatewayRunner.Stop();

            Log.Trace("InteractiveBrokersBrokerage.ResetGatewayConnection(): Restarting IB Gateway...");
            InteractiveBrokersGatewayRunner.Restart();

            Log.Trace("InteractiveBrokersBrokerage.ResetGatewayConnection(): Reconnecting...");
            Connect();

            Log.Trace("InteractiveBrokersBrokerage.ResetGatewayConnection(): Restoring data subscriptions...");
            RestoreDataSubscriptions();

            // notify the BrokerageMessageHandler after the restart, because
            // it could have received a disconnect event during the steps above
            OnMessage(BrokerageMessageEvent.Reconnected(string.Empty));
        }

        /// <summary>
        /// Restores data subscriptions existing before the IB Gateway restart
        /// </summary>
        private void RestoreDataSubscriptions()
        {
            List<Symbol> subscribedSymbols;
            lock (_sync)
            {
                subscribedSymbols = _subscribedSymbols.Keys.ToList();

                _subscribedSymbols.Clear();
                _subscribedTickets.Clear();
                _underlyings.Clear();
            }

            Subscribe(null, subscribedSymbols);
        }

        /// <summary>
        /// If we lose connection to TWS/IB servers we don't want to send the Error event if it is within
        /// the scheduled server reset times
        /// </summary>
        private void TryWaitForReconnect()
        {
            // IB has server reset schedule: https://www.interactivebrokers.com/en/?f=%2Fen%2Fsoftware%2FsystemStatus.php%3Fib_entity%3Dllc

            if (!_disconnected1100Fired)
            {
                return;
            }

            var isResetTime = IsWithinScheduledServerResetTimes();

            if (!isResetTime)
            {
                if (_previouslyInResetTime)
                {
                    // reset time finished and we're still disconnected, restart IB client
                    Log.Trace("InteractiveBrokersBrokerage.TryWaitForReconnect(): Reset time finished and still disconnected. Restarting...");

                    _resetEventRestartGateway.Set();
                }
                else
                {
                    // if we were disconnected and we're not within the reset times, send the error event
                    OnMessage(BrokerageMessageEvent.Disconnected("Connection with Interactive Brokers lost. " +
                                                                 "This could be because of internet connectivity issues or a log in from another location."
                        ));
                }
            }
            else
            {
                Log.Trace("InteractiveBrokersBrokerage.TryWaitForReconnect(): Within server reset times, trying to wait for reconnect...");

                // we're still not connected but we're also within the schedule reset time, so just keep polling
                Task.Delay(TimeSpan.FromMinutes(1)).ContinueWith(_ => TryWaitForReconnect());
            }

            _previouslyInResetTime = isResetTime;
        }

        /// <summary>
        /// Stores all the account values
        /// </summary>
        private void HandleUpdateAccountValue(object sender, IB.UpdateAccountValueEventArgs e)
        {
            try
            {
                _accountData.AccountProperties[e.Currency + ":" + e.Key] = e.Value;

                // we want to capture if the user's cash changes so we can reflect it in the algorithm
                if (e.Key == AccountValueKeys.CashBalance && e.Currency != "BASE")
                {
                    var cashBalance = decimal.Parse(e.Value, CultureInfo.InvariantCulture);
                    _accountData.CashBalances.AddOrUpdate(e.Currency, cashBalance);

                    OnAccountChanged(new AccountEvent(e.Currency, cashBalance));
                }
            }
            catch (Exception err)
            {
                Log.Error("InteractiveBrokersBrokerage.HandleUpdateAccountValue(): " + err);
            }
        }

        /// <summary>
        /// Handle order events from IB
        /// </summary>
        private void HandleOrderStatusUpdates(object sender, IB.OrderStatusEventArgs update)
        {
            try
            {
                Log.Trace("InteractiveBrokersBrokerage.HandleOrderStatusUpdates(): " + update);

                if (!IsConnected)
                {
                    if (_client != null)
                    {
                        Log.Error("InteractiveBrokersBrokerage.HandleOrderStatusUpdates(): Not connected; update dropped, _client.Connected: {0}, _disconnected1100Fired: {1}", _client.Connected, _disconnected1100Fired);
                    }
                    else
                    {
                        Log.Error("InteractiveBrokersBrokerage.HandleOrderStatusUpdates(): Not connected; _client is null");
                    }
                    return;
                }

                var order = _orderProvider.GetOrderByBrokerageId(update.OrderId);
                if (order == null)
                {
                    Log.Error("InteractiveBrokersBrokerage.HandleOrderStatusUpdates(): Unable to locate order with BrokerageID " + update.OrderId);
                    return;
                }

                var status = ConvertOrderStatus(update.Status);

                if (status == OrderStatus.Filled || status == OrderStatus.PartiallyFilled)
                {
                    // fill events will be only processed in HandleExecutionDetails and HandleCommissionReports
                    return;
                }

                // IB likes to duplicate/triplicate some events, so we fire non-fill events only if status changed
                if (status != order.Status)
                {
                    if (order.Status.IsClosed())
                    {
                        // if the order is already in a closed state, we ignore the event
                        Log.Trace("InteractiveBrokersBrokerage.HandleOrderStatusUpdates(): ignoring update in closed state - order.Status: " + order.Status + ", status: " + status);
                    }
                    else if (order.Status == OrderStatus.PartiallyFilled && (status == OrderStatus.New || status == OrderStatus.Submitted))
                    {
                        // if we receive a New or Submitted event when already partially filled, we ignore it
                        Log.Trace("InteractiveBrokersBrokerage.HandleOrderStatusUpdates(): ignoring status " + status + " after partial fills");
                    }
                    else
                    {
                        // fire the event
                        OnOrderEvent(new OrderEvent(order, DateTime.UtcNow, 0, "Interactive Brokers Order Event")
                        {
                            Status = status
                        });
                    }
                }
            }
            catch (Exception err)
            {
                Log.Error("InteractiveBrokersBrokerage.HandleOrderStatusUpdates(): " + err);
            }
        }

        /// <summary>
        /// Handle OpenOrder event from IB
        /// </summary>
        private static void HandleOpenOrder(object sender, IB.OpenOrderEventArgs e)
        {
            Log.Trace($"InteractiveBrokersBrokerage.HandleOpenOrder(): {e}");
        }

        /// <summary>
        /// Handle OpenOrderEnd event from IB
        /// </summary>
        private static void HandleOpenOrderEnd(object sender, EventArgs e)
        {
            Log.Trace("InteractiveBrokersBrokerage.HandleOpenOrderEnd()");
        }

        /// <summary>
        /// Handle execution events from IB
        /// </summary>
        /// <remarks>
        /// This needs to be handled because if a market order is executed immediately, there will be no OrderStatus event
        /// https://interactivebrokers.github.io/tws-api/order_submission.html#order_status
        /// </remarks>
        private void HandleExecutionDetails(object sender, IB.ExecutionDetailsEventArgs executionDetails)
        {
            try
            {
                Log.Trace("InteractiveBrokersBrokerage.HandleExecutionDetails(): " + executionDetails);

                if (!IsConnected)
                {
                    if (_client != null)
                    {
                        Log.Error("InteractiveBrokersBrokerage.HandleExecutionDetails(): Not connected; update dropped, _client.Connected: {0}, _disconnected1100Fired: {1}", _client.Connected, _disconnected1100Fired);
                    }
                    else
                    {
                        Log.Error("InteractiveBrokersBrokerage.HandleExecutionDetails(): Not connected; _client is null");
                    }
                    return;
                }

                var order = _orderProvider.GetOrderByBrokerageId(executionDetails.Execution.OrderId);
                if (order == null)
                {
                    Log.Error("InteractiveBrokersBrokerage.HandleExecutionDetails(): Unable to locate order with BrokerageID " + executionDetails.Execution.OrderId);
                    return;
                }

                // For financial advisor orders, we first receive executions and commission reports for the master order,
                // followed by executions and commission reports for all allocations.
                // We don't want to emit fills for these allocation events,
                // so we ignore events received after the order is completely filled or
                // executions for allocations which are already included in the master execution.

                CommissionReport commissionReport;
                if (_commissionReports.TryGetValue(executionDetails.Execution.ExecId, out commissionReport))
                {
                    if (CanEmitFill(order, executionDetails.Execution))
                    {
                        // we have both execution and commission report, emit the fill
                        EmitOrderFill(order, executionDetails.Execution, commissionReport);
                    }

                    _commissionReports.TryRemove(commissionReport.ExecId, out commissionReport);
                }
                else
                {
                    // save execution in dictionary and wait for commission report
                    _orderExecutions[executionDetails.Execution.ExecId] = executionDetails.Execution;
                }
            }
            catch (Exception err)
            {
                Log.Error("InteractiveBrokersBrokerage.HandleExecutionDetails(): " + err);
            }
        }

        /// <summary>
        /// Handle commission report events from IB
        /// </summary>
        /// <remarks>
        /// This method matches commission reports with previously saved executions and fires the OrderEvents.
        /// </remarks>
        private void HandleCommissionReport(object sender, IB.CommissionReportEventArgs e)
        {
            try
            {
                Log.Trace("InteractiveBrokersBrokerage.HandleCommissionReport(): " + e);

                Execution execution;
                if (!_orderExecutions.TryGetValue(e.CommissionReport.ExecId, out execution))
                {
                    // save commission in dictionary and wait for execution event
                    _commissionReports[e.CommissionReport.ExecId] = e.CommissionReport;
                    return;
                }

                var order = _orderProvider.GetOrderByBrokerageId(execution.OrderId);
                if (order == null)
                {
                    Log.Error("InteractiveBrokersBrokerage.HandleExecutionDetails(): Unable to locate order with BrokerageID " + execution.OrderId);
                    return;
                }

                if (CanEmitFill(order, execution))
                {
                    // we have both execution and commission report, emit the fill
                    EmitOrderFill(order, execution, e.CommissionReport);
                }

                // always remove previous execution
                _orderExecutions.TryRemove(e.CommissionReport.ExecId, out execution);
            }
            catch (Exception err)
            {
                Log.Error("InteractiveBrokersBrokerage.HandleCommissionReport(): " + err);
            }
        }

        /// <summary>
        /// Decide which fills should be emitted, accounting for different types of Financial Advisor orders
        /// </summary>
        private bool CanEmitFill(Order order, Execution execution)
        {
            if (order.Status == OrderStatus.Filled)
                return false;

            // non-FA orders
            if (!IsFinancialAdvisor)
                return true;

            var orderProperties = order.Properties as InteractiveBrokersOrderProperties;
            if (orderProperties == null)
                return true;

            return
                // FA master orders for groups/profiles
                string.IsNullOrWhiteSpace(orderProperties.Account) && execution.AcctNumber == _account ||

                // FA orders for single managed accounts
                !string.IsNullOrWhiteSpace(orderProperties.Account) && execution.AcctNumber == orderProperties.Account;
        }

        /// <summary>
        /// Emits an order fill (or partial fill) including the actual IB commission paid
        /// </summary>
        private void EmitOrderFill(Order order, Execution execution, CommissionReport commissionReport)
        {
            var currentQuantityFilled = Convert.ToInt32(execution.Shares);
            var totalQuantityFilled = Convert.ToInt32(execution.CumQty);
            var remainingQuantity = Convert.ToInt32(order.AbsoluteQuantity - totalQuantityFilled);
            var price = Convert.ToDecimal(execution.Price);
            var orderFee = Convert.ToDecimal(commissionReport.Commission);

            // set order status based on remaining quantity
            var status = remainingQuantity > 0 ? OrderStatus.PartiallyFilled : OrderStatus.Filled;

            // mark sells as negative quantities
            var fillQuantity = order.Direction == OrderDirection.Buy ? currentQuantityFilled : -currentQuantityFilled;
            order.PriceCurrency = _securityProvider.GetSecurity(order.Symbol).SymbolProperties.QuoteCurrency;
            var orderEvent = new OrderEvent(order, DateTime.UtcNow, orderFee, "Interactive Brokers Order Fill Event")
            {
                Status = status,
                FillPrice = price,
                FillQuantity = fillQuantity
            };
            if (remainingQuantity != 0)
            {
                orderEvent.Message += " - " + remainingQuantity + " remaining";
            }

            // fire the order fill event
            OnOrderEvent(orderEvent);
        }

        /// <summary>
        /// Handle portfolio changed events from IB
        /// </summary>
        private void HandlePortfolioUpdates(object sender, IB.UpdatePortfolioEventArgs e)
        {
            _accountHoldingsResetEvent.Reset();
            var holding = CreateHolding(e);
            _accountData.AccountHoldings[holding.Symbol.Value] = holding;
        }

        /// <summary>
        /// Converts a QC order to an IB order
        /// </summary>
        private IBApi.Order ConvertOrder(Order order, Contract contract, int ibOrderId)
        {
            var ibOrder = new IBApi.Order
            {
                ClientId = _clientId,
                OrderId = ibOrderId,
                Account = _account,
                Action = ConvertOrderDirection(order.Direction),
                TotalQuantity = (int)Math.Abs(order.Quantity),
                OrderType = ConvertOrderType(order.Type),
                AllOrNone = false,
                Tif = IB.TimeInForce.GoodTillCancel,
                Transmit = true,
                Rule80A = _agentDescription
            };

            if (order.Type == OrderType.MarketOnOpen)
            {
                ibOrder.Tif = IB.TimeInForce.MarketOnOpen;
            }
            else if (order.Type == OrderType.MarketOnClose)
            {
                ibOrder.Tif = IB.TimeInForce.Day;
            }

            var limitOrder = order as LimitOrder;
            var stopMarketOrder = order as StopMarketOrder;
            var stopLimitOrder = order as StopLimitOrder;
            if (limitOrder != null)
            {
                ibOrder.LmtPrice = Convert.ToDouble(RoundPrice(limitOrder.LimitPrice, GetMinTick(contract)));
            }
            else if (stopMarketOrder != null)
            {
                ibOrder.AuxPrice = Convert.ToDouble(RoundPrice(stopMarketOrder.StopPrice, GetMinTick(contract)));
            }
            else if (stopLimitOrder != null)
            {
                var minTick = GetMinTick(contract);
                ibOrder.LmtPrice = Convert.ToDouble(RoundPrice(stopLimitOrder.LimitPrice, minTick));
                ibOrder.AuxPrice = Convert.ToDouble(RoundPrice(stopLimitOrder.StopPrice, minTick));
            }

            // add financial advisor properties
            if (IsFinancialAdvisor)
            {
                // https://interactivebrokers.github.io/tws-api/financial_advisor.html#gsc.tab=0

                var orderProperties = order.Properties as InteractiveBrokersOrderProperties;
                if (orderProperties != null)
                {
                    if (!string.IsNullOrWhiteSpace(orderProperties.Account))
                    {
                        // order for a single managed account
                        ibOrder.Account = orderProperties.Account;
                    }
                    else if (!string.IsNullOrWhiteSpace(orderProperties.FaProfile))
                    {
                        // order for an account profile
                        ibOrder.FaProfile = orderProperties.FaProfile;

                    }
                    else if (!string.IsNullOrWhiteSpace(orderProperties.FaGroup))
                    {
                        // order for an account group
                        ibOrder.FaGroup = orderProperties.FaGroup;
                        ibOrder.FaMethod = orderProperties.FaMethod;

                        if (ibOrder.FaMethod == "PctChange")
                        {
                            ibOrder.FaPercentage = orderProperties.FaPercentage.ToString();
                            ibOrder.TotalQuantity = 0;
                        }
                    }
                }
            }

            // not yet supported
            //ibOrder.ParentId =
            //ibOrder.OcaGroup =

            return ibOrder;
        }

        private Order ConvertOrder(IBApi.Order ibOrder, Contract contract)
        {
            // this function is called by GetOpenOrders which is mainly used by the setup handler to
            // initialize algorithm state.  So the only time we'll be executing this code is when the account
            // has orders sitting and waiting from before algo initialization...
            // because of this we can't get the time accurately

            Order order;
            var mappedSymbol = MapSymbol(contract);
            var direction = ConvertOrderDirection(ibOrder.Action);
            var quantitySign = direction == OrderDirection.Sell ? -1 : 1;
            var orderType = ConvertOrderType(ibOrder);
            switch (orderType)
            {
                case OrderType.Market:
                    order = new MarketOrder(mappedSymbol,
                        Convert.ToInt32(ibOrder.TotalQuantity) * quantitySign,
                        new DateTime() // not sure how to get this data
                        );
                    break;

                case OrderType.MarketOnOpen:
                    order = new MarketOnOpenOrder(mappedSymbol,
                        Convert.ToInt32(ibOrder.TotalQuantity) * quantitySign,
                        new DateTime());
                    break;

                case OrderType.MarketOnClose:
                    order = new MarketOnCloseOrder(mappedSymbol,
                        Convert.ToInt32(ibOrder.TotalQuantity) * quantitySign,
                        new DateTime()
                        );
                    break;

                case OrderType.Limit:
                    order = new LimitOrder(mappedSymbol,
                        Convert.ToInt32(ibOrder.TotalQuantity) * quantitySign,
                        Convert.ToDecimal(ibOrder.LmtPrice),
                        new DateTime()
                        );
                    break;

                case OrderType.StopMarket:
                    order = new StopMarketOrder(mappedSymbol,
                        Convert.ToInt32(ibOrder.TotalQuantity) * quantitySign,
                        Convert.ToDecimal(ibOrder.AuxPrice),
                        new DateTime()
                        );
                    break;

                case OrderType.StopLimit:
                    order = new StopLimitOrder(mappedSymbol,
                        Convert.ToInt32(ibOrder.TotalQuantity) * quantitySign,
                        Convert.ToDecimal(ibOrder.AuxPrice),
                        Convert.ToDecimal(ibOrder.LmtPrice),
                        new DateTime()
                        );
                    break;

                default:
                    throw new InvalidEnumArgumentException("orderType", (int) orderType, typeof (OrderType));
            }

            order.BrokerId.Add(ibOrder.OrderId.ToString());

            return order;
        }

        /// <summary>
        /// Creates an IB contract from the order.
        /// </summary>
        /// <param name="symbol">The symbol whose contract we need to create</param>
        /// <param name="exchange">The exchange where the order will be placed, defaults to 'Smart'</param>
        /// <returns>A new IB contract for the order</returns>
        private Contract CreateContract(Symbol symbol, string exchange = null)
        {
            var securityType = ConvertSecurityType(symbol.ID.SecurityType);
            var ibSymbol = _symbolMapper.GetBrokerageSymbol(symbol);
            var contract = new Contract
            {
                Symbol = ibSymbol,
                Exchange = exchange ?? "Smart",
                SecType = securityType,
                Currency = "USD"
            };
            if (symbol.ID.SecurityType == SecurityType.Forex)
            {
                // forex is special, so rewrite some of the properties to make it work
                contract.Exchange = "IDEALPRO";
                contract.Symbol = ibSymbol.Substring(0, 3);
                contract.Currency = ibSymbol.Substring(3);
            }

            if (symbol.ID.SecurityType == SecurityType.Equity)
            {
                contract.PrimaryExch = GetPrimaryExchange(contract);
            }

            if (symbol.ID.SecurityType == SecurityType.Option)
            {
                contract.LastTradeDateOrContractMonth = symbol.ID.Date.ToString(DateFormat.EightCharacter);
                contract.Right = symbol.ID.OptionRight == OptionRight.Call ? IB.RightType.Call : IB.RightType.Put;
                contract.Strike = Convert.ToDouble(symbol.ID.StrikePrice);
                contract.Symbol = ibSymbol;
                contract.Multiplier = _securityProvider.GetSecurity(symbol)?.SymbolProperties.ContractMultiplier.ToString(CultureInfo.InvariantCulture) ?? "100";
                contract.TradingClass = GetTradingClass(contract);
            }

            if (symbol.ID.SecurityType == SecurityType.Future)
            {
                // if Market.USA is specified we automatically find exchange from the prioritized list
                // Otherwise we convert Market.* markets into IB exchanges if we have them in our map

                contract.Symbol = ibSymbol;
                contract.LastTradeDateOrContractMonth = symbol.ID.Date.ToString(DateFormat.EightCharacter);

                if (symbol.ID.Market == Market.USA)
                {
                    contract.Exchange = "";
                    contract.Exchange = GetFuturesContractExchange(contract);
                }
                else
                {
                    contract.Exchange = _futuresExchanges.ContainsKey(symbol.ID.Market) ?
                                            _futuresExchanges[symbol.ID.Market] :
                                            symbol.ID.Market;
                }
            }

            return contract;
        }

        /// <summary>
        /// Maps OrderDirection enumeration
        /// </summary>
        private OrderDirection ConvertOrderDirection(string direction)
        {
            switch (direction)
            {
                case IB.ActionSide.Buy: return OrderDirection.Buy;
                case IB.ActionSide.Sell: return OrderDirection.Sell;
                case IB.ActionSide.Undefined: return OrderDirection.Hold;
                default:
                    throw new ArgumentException(direction, "direction");
            }
        }

        /// <summary>
        /// Maps OrderDirection enumeration
        /// </summary>
        private static string ConvertOrderDirection(OrderDirection direction)
        {
            switch (direction)
            {
                case OrderDirection.Buy:  return IB.ActionSide.Buy;
                case OrderDirection.Sell: return IB.ActionSide.Sell;
                case OrderDirection.Hold: return IB.ActionSide.Undefined;
                default:
                    throw new InvalidEnumArgumentException("direction", (int) direction, typeof (OrderDirection));
            }
        }

        /// <summary>
        /// Maps OrderType enum
        /// </summary>
        private static string ConvertOrderType(OrderType type)
        {
            switch (type)
            {
                case OrderType.Market:          return IB.OrderType.Market;
                case OrderType.Limit:           return IB.OrderType.Limit;
                case OrderType.StopMarket:      return IB.OrderType.Stop;
                case OrderType.StopLimit:       return IB.OrderType.StopLimit;
                case OrderType.MarketOnOpen:    return IB.OrderType.Market;
                case OrderType.MarketOnClose:   return IB.OrderType.MarketOnClose;
                default:
                    throw new InvalidEnumArgumentException("type", (int)type, typeof(OrderType));
            }
        }

        /// <summary>
        /// Maps OrderType enum
        /// </summary>
        private static OrderType ConvertOrderType(IBApi.Order order)
        {
            switch (order.OrderType)
            {
                case IB.OrderType.Limit:            return OrderType.Limit;
                case IB.OrderType.Stop:             return OrderType.StopMarket;
                case IB.OrderType.StopLimit:        return OrderType.StopLimit;
                case IB.OrderType.MarketOnClose:    return OrderType.MarketOnClose;

                case IB.OrderType.Market:
                    if (order.Tif == IB.TimeInForce.MarketOnOpen)
                    {
                        return OrderType.MarketOnOpen;
                    }
                    return OrderType.Market;

                default:
                    throw new ArgumentException(order.OrderType, "order.OrderType");
            }
        }

        /// <summary>
        /// Maps IB's OrderStats enum
        /// </summary>
        private static OrderStatus ConvertOrderStatus(string status)
        {
            switch (status)
            {
                case IB.OrderStatus.ApiPending:
                case IB.OrderStatus.PendingSubmit:
                case IB.OrderStatus.PreSubmitted:
                    return OrderStatus.New;

                case IB.OrderStatus.ApiCancelled:
                case IB.OrderStatus.PendingCancel:
                case IB.OrderStatus.Cancelled:
                    return OrderStatus.Canceled;

                case IB.OrderStatus.Submitted:
                    return OrderStatus.Submitted;

                case IB.OrderStatus.Filled:
                    return OrderStatus.Filled;

                case IB.OrderStatus.PartiallyFilled:
                    return OrderStatus.PartiallyFilled;

                case IB.OrderStatus.Error:
                    return OrderStatus.Invalid;

                case IB.OrderStatus.Inactive:
                    Log.Error("InteractiveBrokersBrokerage.ConvertOrderStatus(): Inactive order");
                    return OrderStatus.None;

                case IB.OrderStatus.None:
                    return OrderStatus.None;

                // not sure how to map these guys
                default:
                    throw new ArgumentException(status, "status");
            }
        }

        /// <summary>
        /// Maps SecurityType enum
        /// </summary>
        private static string ConvertSecurityType(SecurityType type)
        {
            switch (type)
            {
                case SecurityType.Equity:
                    return IB.SecurityType.Stock;

                case SecurityType.Option:
                    return IB.SecurityType.Option;

                case SecurityType.Commodity:
                    return IB.SecurityType.Commodity;

                case SecurityType.Forex:
                    return IB.SecurityType.Cash;

                case SecurityType.Future:
                    return IB.SecurityType.Future;

                case SecurityType.Base:
                    throw new ArgumentException("InteractiveBrokers does not support SecurityType.Base");

                default:
                    throw new InvalidEnumArgumentException("type", (int)type, typeof(SecurityType));
            }
        }

        /// <summary>
        /// Maps SecurityType enum
        /// </summary>
        private static SecurityType ConvertSecurityType(string type)
        {
            switch (type)
            {
                case IB.SecurityType.Stock:
                    return SecurityType.Equity;

                case IB.SecurityType.Option:
                    return SecurityType.Option;

                case IB.SecurityType.Commodity:
                    return SecurityType.Commodity;

                case IB.SecurityType.Cash:
                    return SecurityType.Forex;

                case IB.SecurityType.Future:
                    return SecurityType.Future;

                // we don't map these security types to anything specific yet, load them as custom data instead of throwing
                case IB.SecurityType.Index:
                case IB.SecurityType.FutureOption:
                case IB.SecurityType.Bag:
                case IB.SecurityType.Bond:
                case IB.SecurityType.Warrant:
                case IB.SecurityType.Bill:
                case IB.SecurityType.Undefined:
                    return SecurityType.Base;

                default:
                    throw new ArgumentOutOfRangeException("type");
            }
        }

        /// <summary>
        /// Maps Resolution to IB representation
        /// </summary>
        /// <param name="resolution"></param>
        /// <returns></returns>
        private string ConvertResolution(Resolution resolution)
        {
            switch(resolution)
            {
                case Resolution.Tick:
                case Resolution.Second:
                    return IB.BarSize.OneSecond;
                case Resolution.Minute:
                    return IB.BarSize.OneMinute;
                case Resolution.Hour:
                    return IB.BarSize.OneHour;
                case Resolution.Daily:
                default:
                    return IB.BarSize.OneDay;
            }
        }

        /// <summary>
        /// Maps Resolution to IB span
        /// </summary>
        /// <param name="resolution"></param>
        /// <returns></returns>
        private string ConvertResolutionToDuration(Resolution resolution)
        {
            switch (resolution)
            {
                case Resolution.Tick:
                case Resolution.Second:
                    return "60 S";
                case Resolution.Minute:
                    return "1 D";
                case Resolution.Hour:
                    return "1 M";
                case Resolution.Daily:
                default:
                    return "1 Y";
            }
        }

        private static TradeBar ConvertTradeBar(Symbol symbol, Resolution resolution, IB.HistoricalDataEventArgs historyBar)
        {
            var time = resolution != Resolution.Daily ?
                Time.UnixTimeStampToDateTime(Convert.ToDouble(historyBar.Bar.Time, CultureInfo.InvariantCulture)) :
                DateTime.ParseExact(historyBar.Bar.Time, "yyyyMMdd", CultureInfo.InvariantCulture);

            return new TradeBar(time, symbol, (decimal)historyBar.Bar.Open, (decimal)historyBar.Bar.High, (decimal)historyBar.Bar.Low,
                (decimal)historyBar.Bar.Close, historyBar.Bar.Volume, resolution.ToTimeSpan());
        }

        /// <summary>
        /// Creates a holding object from the UpdatePortfolioEventArgs
        /// </summary>
        private Holding CreateHolding(IB.UpdatePortfolioEventArgs e)
        {
            var currencySymbol = Currencies.GetCurrencySymbol(e.Contract.Currency);
            var symbol = MapSymbol(e.Contract);

            var multiplier = Convert.ToDecimal(e.Contract.Multiplier);
            if (multiplier == 0m) multiplier = 1m;

            return new Holding
            {
                Symbol = symbol,
                Type = ConvertSecurityType(e.Contract.SecType),
                Quantity = e.Position,
                AveragePrice = Convert.ToDecimal(e.AverageCost) / multiplier,
                MarketPrice = Convert.ToDecimal(e.MarketPrice),
                ConversionRate = 1m, // this will be overwritten when GetAccountHoldings is called to ensure fresh values
                CurrencySymbol = currencySymbol
            };
        }

        /// <summary>
        /// Maps the IB Contract's symbol to a QC symbol
        /// </summary>
        private Symbol MapSymbol(Contract contract)
        {
            var securityType = ConvertSecurityType(contract.SecType);
            var ibSymbol = securityType == SecurityType.Forex ? contract.Symbol + contract.Currency : contract.Symbol;
            var market = securityType == SecurityType.Forex ? Market.FXCM : Market.USA;

            if (securityType == SecurityType.Future)
            {
                var contractDate = DateTime.ParseExact(contract.LastTradeDateOrContractMonth, DateFormat.EightCharacter, CultureInfo.InvariantCulture);

                return _symbolMapper.GetLeanSymbol(ibSymbol, securityType, market, contractDate);
            }
            else if (securityType == SecurityType.Option)
            {
                var expiryDate = DateTime.ParseExact(contract.LastTradeDateOrContractMonth, DateFormat.EightCharacter, CultureInfo.InvariantCulture);
                var right = contract.Right == IB.RightType.Call ? OptionRight.Call : OptionRight.Put;
                var strike = Convert.ToDecimal(contract.Strike);

                return _symbolMapper.GetLeanSymbol(ibSymbol, securityType, market, expiryDate, strike, right);
            }

            return _symbolMapper.GetLeanSymbol(ibSymbol, securityType, market);
        }

        private static decimal RoundPrice(decimal input, decimal minTick)
        {
            if (minTick == 0) return minTick;
            return Math.Round(input/minTick)*minTick;
        }

        /// <summary>
        /// Handles the threading issues of creating an IB order ID
        /// </summary>
        /// <returns>The new IB ID</returns>
        private int GetNextBrokerageOrderId()
        {
            // spin until we get a next valid id, this should only execute if we create a new instance
            // and immediately try to place an order
            while (_nextValidId == 0) { Thread.Yield(); }

            return Interlocked.Increment(ref _nextValidId);
        }

        private int GetNextRequestId()
        {
            return Interlocked.Increment(ref _nextRequestId);
        }

        private int GetNextTickerId()
        {
            return Interlocked.Increment(ref _nextTickerId);
        }

        /// <summary>
        /// Increments the client ID for communication with the gateway
        /// </summary>
        private static int IncrementClientId()
        {
            return Interlocked.Increment(ref _nextClientId);
        }

        /// <summary>
        /// This function is used to decide whether or not we should kill an algorithm
        /// when we lose contact with IB servers. IB performs server resets nightly
        /// and on Fridays they take everything down, so we'll prevent killing algos
        /// on Saturdays completely for the time being.
        /// </summary>
        private static bool IsWithinScheduledServerResetTimes()
        {
            bool result;
            var time = DateTime.UtcNow.ConvertFromUtc(TimeZones.NewYork);

            // don't kill algos on Saturdays if we don't have a connection
            if (time.DayOfWeek == DayOfWeek.Saturday)
            {
                result = true;
            }
            else
            {
                var timeOfDay = time.TimeOfDay;
                // from 11:45 -> 12:45 is the IB reset times, we'll go from 11:00pm->1:30am for safety margin
                result = timeOfDay > new TimeSpan(23, 0, 0) || timeOfDay < new TimeSpan(1, 30, 0);
            }

            Log.Trace("InteractiveBrokersBrokerage.IsWithinScheduledServerResetTimes(): " + result);

            return result;
        }

        private void HandleBrokerTime(object sender, IB.CurrentTimeUtcEventArgs e)
        {
            // keep track of clock drift
            _brokerTimeDiff = e.CurrentTimeUtc.Subtract(DateTime.UtcNow);
        }

        private TimeSpan _brokerTimeDiff = new TimeSpan(0);


        /// <summary>
        /// IDataQueueHandler interface implementation
        /// </summary>
        ///
        public IEnumerable<BaseData> GetNextTicks()
        {
            Tick[] ticks;

            lock (_ticks)
            {
                ticks = _ticks.ToArray();
                _ticks.Clear();
            }

            foreach (var tick in ticks)
            {
                yield return tick;

                lock (_sync)
                {
                    if (_underlyings.ContainsKey(tick.Symbol))
                    {
                        var underlyingTick = tick.Clone();
                        underlyingTick.Symbol = _underlyings[tick.Symbol];
                        yield return underlyingTick;
                    }
                }
            }
        }

        /// <summary>
        /// Adds the specified symbols to the subscription
        /// </summary>
        /// <param name="job">Job we're subscribing for:</param>
        /// <param name="symbols">The symbols to be added keyed by SecurityType</param>
        public void Subscribe(LiveNodePacket job, IEnumerable<Symbol> symbols)
        {
            try
            {
                foreach (var symbol in symbols)
                {
                    if (CanSubscribe(symbol))
                    {
                        lock (_sync)
                        {
                            Log.Trace("InteractiveBrokersBrokerage.Subscribe(): Subscribe Request: " + symbol.Value);

                            if (!_subscribedSymbols.ContainsKey(symbol))
                            {
                                // processing canonical option and futures symbols
                                var subscribeSymbol = symbol;

                                // we subscribe to the underlying
                                if (symbol.ID.SecurityType == SecurityType.Option && symbol.IsCanonical())
                                {
                                    subscribeSymbol = symbol.Underlying;
                                    _underlyings.Add(subscribeSymbol, symbol);
                                }

                                // we ignore futures canonical symbol
                                if (symbol.ID.SecurityType == SecurityType.Future && symbol.IsCanonical())
                                {
                                    return;
                                }

                                var id = GetNextTickerId();
                                var contract = CreateContract(subscribeSymbol);

                                _requestInformation[id] = "Subscribe: " + contract;

                                _messagingRateLimiter.WaitToProceed();

                                // track subscription time for minimum delay in unsubscribe
                                _subscriptionTimes[id] = DateTime.UtcNow;

                                // we would like to receive OI (101)
                                Client.ClientSocket.reqMktData(id, contract, "101", false, false, new List<TagValue>());

                                _subscribedSymbols[symbol] = id;
                                _subscribedTickets[id] = subscribeSymbol;

                                Log.Trace("InteractiveBrokersBrokerage.Subscribe(): Subscribe Processed: {0} ({1}) # {2}", symbol.Value, contract.ToString(), id);
                            }
                        }
                    }
                }
            }
            catch (Exception err)
            {
                Log.Error("InteractiveBrokersBrokerage.Subscribe(): " + err.Message);
            }
        }

        /// <summary>
        /// Removes the specified symbols to the subscription
        /// </summary>
        /// <param name="job">Job we're processing.</param>
        /// <param name="symbols">The symbols to be removed keyed by SecurityType</param>
        public void Unsubscribe(LiveNodePacket job, IEnumerable<Symbol> symbols)
        {
            try
            {
                foreach (var symbol in symbols)
                {
                    if (CanSubscribe(symbol))
                    {
                        lock (_sync)
                        {
                            Log.Trace("InteractiveBrokersBrokerage.Unsubscribe(): Unsubscribe Request: " + symbol.Value);

                            if (symbol.ID.SecurityType == SecurityType.Option && symbol.ID.StrikePrice == 0.0m)
                            {
                                _underlyings.Remove(symbol.Underlying);
                            }

                            int id;
                            if (_subscribedSymbols.TryRemove(symbol, out id))
                            {
                                _messagingRateLimiter.WaitToProceed();

                                // ensure minimum time span has elapsed since the symbol was subscribed
                                DateTime subscriptionTime;
                                if (_subscriptionTimes.TryGetValue(id, out subscriptionTime))
                                {
                                    var timeSinceSubscription = DateTime.UtcNow - subscriptionTime;
                                    if (timeSinceSubscription < _minimumTimespanBeforeUnsubscribe)
                                    {
                                        var delay = Convert.ToInt32((_minimumTimespanBeforeUnsubscribe - timeSinceSubscription).TotalMilliseconds);
                                        Thread.Sleep(delay);
                                    }

                                    _subscriptionTimes.Remove(id);
                                }

                                Client.ClientSocket.cancelMktData(id);

                                Symbol s;
                                _subscribedTickets.TryRemove(id, out s);
                            }
                        }
                    }
                }
            }
            catch (Exception err)
            {
                Log.Error("InteractiveBrokersBrokerage.Unsubscribe(): " + err.Message);
            }
        }

        /// <summary>
        /// Returns true if this data provide can handle the specified symbol
        /// </summary>
        /// <param name="symbol">The symbol to be handled</param>
        /// <returns>True if this data provider can get data for the symbol, false otherwise</returns>
        private bool CanSubscribe(Symbol symbol)
        {
            var market = symbol.ID.Market;
            var securityType = symbol.ID.SecurityType;

            if (symbol.Value.ToLower().IndexOf("universe") != -1) return false;

            return
                (securityType == SecurityType.Equity && market == Market.USA) ||
                (securityType == SecurityType.Forex && market == Market.FXCM) ||
                (securityType == SecurityType.Option && market == Market.USA) ||
                (securityType == SecurityType.Future);
        }

        /// <summary>
        /// Returns a timestamp for a tick converted to the exchange time zone
        /// </summary>
        private DateTime GetRealTimeTickTime(Symbol symbol)
        {
            var time = DateTime.UtcNow.Add(_brokerTimeDiff);

            DateTimeZone exchangeTimeZone;
            if (!_symbolExchangeTimeZones.TryGetValue(symbol, out exchangeTimeZone))
            {
                // read the exchange time zone from market-hours-database
                exchangeTimeZone = MarketHoursDatabase.FromDataFolder().GetExchangeHours(symbol.ID.Market, symbol, symbol.SecurityType).TimeZone;
                _symbolExchangeTimeZones.Add(symbol, exchangeTimeZone);
            }

            return time.ConvertFromUtc(exchangeTimeZone);
        }

        private void HandleTickPrice(object sender, IB.TickPriceEventArgs e)
        {
            Symbol symbol;

            if (!_subscribedTickets.TryGetValue(e.TickerId, out symbol)) return;

            var price = Convert.ToDecimal(e.Price);

            var tick = new Tick();
            // in the event of a symbol change this will break since we'll be assigning the
            // new symbol to the permtick which won't be known by the algorithm
            tick.Symbol = symbol;
            tick.Time = GetRealTimeTickTime(symbol);
            var securityType = symbol.ID.SecurityType;
            tick.Value = price;

            if (e.Price <= 0 &&
                securityType != SecurityType.Future &&
                securityType != SecurityType.Option)
                return;

            int quantity;
            switch (e.Field)
            {
                case IBApi.TickType.BID:

                    tick.TickType = TickType.Quote;
                    tick.BidPrice = price;
                    _lastBidSizes.TryGetValue(symbol, out quantity);
                    tick.Quantity = quantity;
                    _lastBidPrices[symbol] = price;
                    break;

                case IBApi.TickType.ASK:

                    tick.TickType = TickType.Quote;
                    tick.AskPrice = price;
                    _lastAskSizes.TryGetValue(symbol, out quantity);
                    tick.Quantity = quantity;
                    _lastAskPrices[symbol] = price;
                    break;

                case IBApi.TickType.LAST:

                    tick.TickType = TickType.Trade;
                    tick.Value = price;
                    _lastPrices[symbol] = price;
                    break;

                default:
                    return;
            }

            lock (_ticks)
                if (tick.IsValid()) _ticks.Add(tick);
        }

        /// <summary>
        /// Modifies the quantity received from IB based on the security type
        /// </summary>
        public static int AdjustQuantity(SecurityType type, int size)
        {
            switch (type)
            {
                case SecurityType.Equity:
                    return size * 100;
                default:
                    return size;
            }
        }

        private void HandleTickSize(object sender, IB.TickSizeEventArgs e)
        {
            Symbol symbol;

            if (!_subscribedTickets.TryGetValue(e.TickerId, out symbol)) return;

            var tick = new Tick();
            // in the event of a symbol change this will break since we'll be assigning the
            // new symbol to the permtick which won't be known by the algorithm
            tick.Symbol = symbol;
            var securityType = symbol.ID.SecurityType;
            tick.Quantity = AdjustQuantity(securityType, e.Size);
            tick.Time = GetRealTimeTickTime(symbol);

            if (tick.Quantity == 0) return;

            switch (e.Field)
            {
                case IBApi.TickType.BID_SIZE:

                    tick.TickType = TickType.Quote;

                    _lastBidPrices.TryGetValue(symbol, out tick.BidPrice);
                    _lastBidSizes[symbol] = (int)tick.Quantity;

                    tick.Value = tick.BidPrice;
                    tick.BidSize = tick.Quantity;
                    break;

                case IBApi.TickType.ASK_SIZE:

                    tick.TickType = TickType.Quote;

                    _lastAskPrices.TryGetValue(symbol, out tick.AskPrice);
                    _lastAskSizes[symbol] = (int)tick.Quantity;

                    tick.Value = tick.AskPrice;
                    tick.AskSize = tick.Quantity;
                    break;


                case IBApi.TickType.LAST_SIZE:
                    tick.TickType = TickType.Trade;

                    decimal lastPrice;
                    _lastPrices.TryGetValue(symbol, out lastPrice);
                    _lastVolumes[symbol] = (int)tick.Quantity;

                    tick.Value = lastPrice;

                    break;

                case IBApi.TickType.OPEN_INTEREST:
                case IBApi.TickType.OPTION_CALL_OPEN_INTEREST:
                case IBApi.TickType.OPTION_PUT_OPEN_INTEREST:

                    if (symbol.ID.SecurityType == SecurityType.Option || symbol.ID.SecurityType == SecurityType.Future)
                    {
                        if (!_openInterests.ContainsKey(symbol) || _openInterests[symbol] != e.Size)
                        {
                            tick.TickType = TickType.OpenInterest;
                            tick.Value = e.Size;
                            _openInterests[symbol] = e.Size;
                        }
                    }
                    break;

                default:
                    return;
            }
            lock (_ticks)
                if (tick.IsValid()) _ticks.Add(tick);
        }

        /// <summary>
        /// Method returns a collection of Symbols that are available at the broker.
        /// </summary>
        /// <param name="lookupName">String representing the name to lookup</param>
        /// <param name="securityType">Expected security type of the returned symbols (if any)</param>
        /// <param name="securityCurrency">Expected security currency(if any)</param>
        /// <param name="securityExchange">Expected security exchange name(if any)</param>
        /// <returns></returns>
        public IEnumerable<Symbol> LookupSymbols(string lookupName, SecurityType securityType, string securityCurrency = null, string securityExchange = null)
        {
            // connect will throw if it fails
            Connect();

            // setting up exchange defaults and filters
            var exchangeSpecifier = securityType == SecurityType.Future ? securityExchange ?? "" : securityExchange ?? "Smart";
            var futuresExchanges = _futuresExchanges.Values.Reverse().ToArray();
            Func<string, int> exchangeFilter = exchange => securityType == SecurityType.Future ? Array.IndexOf(futuresExchanges, exchange) : 0;

            // setting up lookup request
            var contract = new Contract
            {
                Symbol = _symbolMapper.GetBrokerageRootSymbol(lookupName),
                Currency = securityCurrency ?? "USD",
                Exchange = exchangeSpecifier,
                SecType = ConvertSecurityType(securityType)
            };

            Log.Trace("InteractiveBrokersBrokerage.LookupSymbols(): Requesting symbol list for " + contract.Symbol + " ...");

            if (securityType == SecurityType.Option)
            {
                // IB requests for full option chains are rate limited and responses can be delayed up to a minute for each underlying,
                // so we fetch them from the OCC website instead of using the IB API.

                var underlyingSymbol = Symbol.Create(contract.Symbol, SecurityType.Equity, Market.USA);
                var symbols = _algorithm.OptionChainProvider.GetOptionContractList(underlyingSymbol, DateTime.Today).ToList();

                Log.Trace("InteractiveBrokersBrokerage.LookupSymbols(): Returning {0} contracts for {1}", symbols.Count, contract.Symbol);

                return symbols;
            }

            // processing request
            var results = FindContracts(contract);

            // filtering results
            var filteredResults =
                    results
                    .Select(x => x.Summary)
                    .GroupBy(x => x.Exchange)
                    .OrderByDescending(g => exchangeFilter(g.Key))
                    .FirstOrDefault();

            Log.Trace("InteractiveBrokersBrokerage.LookupSymbols(): Returning {0} symbol(s)", filteredResults != null ? filteredResults.Count() : 0);

            // returning results
            return filteredResults != null ? filteredResults.Select(MapSymbol) : Enumerable.Empty<Symbol>();
        }

        /// <summary>
        /// Gets the history for the requested security
        /// </summary>
        /// <param name="request">The historical data request</param>
        /// <returns>An enumerable of bars covering the span specified in the request</returns>
        /// <remarks>For IB history limitations see https://www.interactivebrokers.com/en/software/api/apiguide/tables/historical_data_limitations.htm </remarks>
        public override IEnumerable<BaseData> GetHistory(HistoryRequest request)
        {
            // skipping universe and canonical symbols
            if (!CanSubscribe(request.Symbol) ||
                (request.Symbol.ID.SecurityType == SecurityType.Option && request.Symbol.IsCanonical()) ||
                (request.Symbol.ID.SecurityType == SecurityType.Future && request.Symbol.IsCanonical()))
            {
                yield break;
            }

            // preparing the data for IB request
            var contract = CreateContract(request.Symbol);
            var resolution = ConvertResolution(request.Resolution);
            var duration = ConvertResolutionToDuration(request.Resolution);
            var startTime = request.Resolution == Resolution.Daily ? request.StartTimeUtc.Date : request.StartTimeUtc;
            var endTime = request.Resolution == Resolution.Daily ? request.EndTimeUtc.Date : request.EndTimeUtc;

            Log.Trace("InteractiveBrokersBrokerage::GetHistory(): Submitting request: {0}({1}): {2} {3} UTC -> {4} UTC",
                request.Symbol.Value, contract, request.Resolution, startTime, endTime);

            DateTimeZone exchangeTimeZone;
            if (!_symbolExchangeTimeZones.TryGetValue(request.Symbol, out exchangeTimeZone))
            {
                // read the exchange time zone from market-hours-database
                exchangeTimeZone = MarketHoursDatabase.FromDataFolder().GetExchangeHours(request.Symbol.ID.Market, request.Symbol, request.Symbol.SecurityType).TimeZone;
                _symbolExchangeTimeZones.Add(request.Symbol, exchangeTimeZone);
            }

            IEnumerable<BaseData> history;
            if (request.Symbol.SecurityType == SecurityType.Forex || request.Symbol.SecurityType == SecurityType.Cfd)
            {
                // Forex and CFD need two separate IB requests for Bid and Ask,
                // each pair of TradeBars will be joined into a single QuoteBar
                var historyBid = GetHistory(request, contract, startTime, endTime, exchangeTimeZone, duration, resolution, HistoricalDataType.Bid);
                var historyAsk = GetHistory(request, contract, startTime, endTime, exchangeTimeZone, duration, resolution, HistoricalDataType.Ask);

                history = historyBid.Join(historyAsk,
                    bid => bid.Time,
                    ask => ask.Time,
                    (bid, ask) => new QuoteBar(
                        bid.Time,
                        bid.Symbol,
                        new Bar(bid.Open, bid.High, bid.Low, bid.Close),
                        0,
                        new Bar(ask.Open, ask.High, ask.Low, ask.Close),
                        0,
                        bid.Period));
            }
            else
            {
                // other assets will have TradeBars
                history = GetHistory(request, contract, startTime, endTime, exchangeTimeZone, duration, resolution, HistoricalDataType.Trades);
            }

            // cleaning the data before returning it back to user
            var requestStartTime = request.StartTimeUtc.ConvertFromUtc(exchangeTimeZone);
            var requestEndTime = request.EndTimeUtc.ConvertFromUtc(exchangeTimeZone);

            foreach (var bar in history.Where(bar => bar.Time >= requestStartTime && bar.EndTime <= requestEndTime))
            {
                yield return bar;
            }

            Log.Trace("InteractiveBrokersBrokerage::GetHistory() Download completed");
        }

        private IEnumerable<TradeBar> GetHistory(
            HistoryRequest request,
            Contract contract,
            DateTime startTime,
            DateTime endTime,
            DateTimeZone exchangeTimeZone,
            string duration,
            string resolution,
            string dataType)
        {
            const int timeOut = 60; // seconds timeout

            var history = new List<TradeBar>();
            var dataDownloading = new AutoResetEvent(false);
            var dataDownloaded = new AutoResetEvent(false);

            var useRegularTradingHours = Convert.ToInt32(!request.IncludeExtendedMarketHours);

            // making multiple requests if needed in order to download the history
            while (endTime >= startTime)
            {
                var pacing = false;
                var historyPiece = new List<TradeBar>();
                var historicalTicker = GetNextTickerId();

                _requestInformation[historicalTicker] = "GetHistory: " + contract;

                EventHandler<IB.HistoricalDataEventArgs> clientOnHistoricalData = (sender, args) =>
                {
                    if (args.RequestId == historicalTicker)
                    {
                        var bar = ConvertTradeBar(request.Symbol, request.Resolution, args);
                        if (request.Resolution != Resolution.Daily)
                        {
                            bar.Time = bar.Time.ConvertFromUtc(exchangeTimeZone);
                        }

                        historyPiece.Add(bar);
                        dataDownloading.Set();
                    }
                };

                EventHandler<IB.HistoricalDataEndEventArgs> clientOnHistoricalDataEnd = (sender, args) =>
                {
                    if (args.RequestId == historicalTicker)
                    {
                        dataDownloaded.Set();
                    }
                };

                EventHandler<IB.ErrorEventArgs> clientOnError = (sender, args) =>
                {
                    if (args.Code == 162 && args.Message.Contains("pacing violation"))
                    {
                        // pacing violation happened
                        pacing = true;
                    }
                    if (args.Code == 162 && args.Message.Contains("no data"))
                    {
                        dataDownloaded.Set();
                    }
                };

                Client.Error += clientOnError;
                Client.HistoricalData += clientOnHistoricalData;
                Client.HistoricalDataEnd += clientOnHistoricalDataEnd;

                _messagingRateLimiter.WaitToProceed();

                Client.ClientSocket.reqHistoricalData(historicalTicker, contract, endTime.ToString("yyyyMMdd HH:mm:ss UTC"),
                    duration, resolution, dataType, useRegularTradingHours, 2, false, new List<TagValue>());

                var waitResult = 0;
                while (waitResult == 0)
                {
                    waitResult = WaitHandle.WaitAny(new WaitHandle[] {dataDownloading, dataDownloaded}, timeOut*1000);
                }

                Client.Error -= clientOnError;
                Client.HistoricalData -= clientOnHistoricalData;
                Client.HistoricalDataEnd -= clientOnHistoricalDataEnd;

                if (waitResult == WaitHandle.WaitTimeout)
                {
                    if (pacing)
                    {
                        // we received 'pacing violation' error from IB. So we had to wait
                        Log.Trace("InteractiveBrokersBrokerage::GetHistory() Pacing violation. Paused for {0} secs.", timeOut);
                        continue;
                    }

                    Log.Trace("InteractiveBrokersBrokerage::GetHistory() History request timed out ({0} sec)", timeOut);
                    break;
                }

                // if no data has been received this time, we exit
                if (!historyPiece.Any())
                {
                    break;
                }

                var filteredPiece = historyPiece.OrderBy(x => x.Time);

                history.InsertRange(0, filteredPiece);

                // moving endTime to the new position to proceed with next request (if needed)
                endTime = filteredPiece.First().Time;
            }

            return history;
        }

        /// <summary>
        /// Returns true if an existing session was detected and IBController clicked the "Exit Application" button
        /// </summary>
        /// <remarks>
        /// For this method to work, the following setting is required in the IBController.ini file:
        /// ExistingSessionDetectedAction=secondary
        /// </remarks>
        private static bool ExistingSessionDetected(List<string> ibcLogLines)
        {
            return IbControllerLogContainsMessage(ibcLogLines, "End this session and let the other session proceed");
        }

        /// <summary>
        /// Returns true if an IB security dialog (2FA/code card) was detected by IBController
        /// </summary>
        private static bool SecurityDialogDetected(List<string> ibcLogLines)
        {
            return IbControllerLogContainsMessage(ibcLogLines, "Second Factor Authentication") ||
                   IbControllerLogContainsMessage(ibcLogLines, "Security Code Card Authentication");
        }

        /// <summary>
        /// Reads the current IBController log file
        /// </summary>
        /// <returns>A list containing the lines of the file</returns>
        private static List<string> LoadCurrentIbControllerLogFile()
        {
            // find the current IBController log file name
            var ibControllerLogPath = Path.Combine(Config.Get("ib-controller-dir"), "Logs");
            var files = Directory.GetFiles(ibControllerLogPath, "ibc-*.txt");
            var lastLogUpdateTime = DateTime.MinValue;
            var ibControllerLogFileName = string.Empty;
            foreach (var file in files)
            {
                var time = File.GetLastWriteTimeUtc(file);
                if (time > lastLogUpdateTime)
                {
                    lastLogUpdateTime = time;
                    ibControllerLogFileName = file;
                }
            }

            return ibControllerLogFileName.IsNullOrEmpty()
                ? new List<string>()
                : File.ReadAllLines(ibControllerLogFileName).ToList();
        }

        /// <summary>
        /// Searches for a message in the IBController log file
        /// </summary>
        /// <param name="lines">The lines of text of the IBController log file</param>
        /// <param name="message">The message text to find</param>
        /// <returns>true if the message was found</returns>
        private static bool IbControllerLogContainsMessage(List<string> lines, string message)
        {
            // read the lines and find the message
            var separatorLine = new string('-', 60);
            var index = lines.FindLastIndex(x => x.Contains(separatorLine));

            return index >= 0 && lines.Skip(index + 1).Any(line => line.Contains(message));
        }

        /// <summary>
        /// Check if IB Gateway running, restart if not
        /// </summary>
        public void CheckIbGateway()
        {
            Log.Trace("InteractiveBrokersBrokerage.CheckIbGateway(): start");
            if (!InteractiveBrokersGatewayRunner.IsRunning())
            {
                Log.Trace("InteractiveBrokersBrokerage.CheckIbGateway(): IB Gateway not running. Restarting...");
                _resetEventRestartGateway.Set();
            }
            Log.Trace("InteractiveBrokersBrokerage.CheckIbGateway(): end");
        }

        private readonly ConcurrentDictionary<Symbol, int> _subscribedSymbols = new ConcurrentDictionary<Symbol, int>();
        private readonly ConcurrentDictionary<int, Symbol> _subscribedTickets = new ConcurrentDictionary<int, Symbol>();
        private readonly Dictionary<Symbol, Symbol> _underlyings = new Dictionary<Symbol, Symbol>();
        private readonly ConcurrentDictionary<Symbol, decimal> _lastPrices = new ConcurrentDictionary<Symbol, decimal>();
        private readonly ConcurrentDictionary<Symbol, int> _lastVolumes = new ConcurrentDictionary<Symbol, int>();
        private readonly ConcurrentDictionary<Symbol, decimal> _lastBidPrices = new ConcurrentDictionary<Symbol, decimal>();
        private readonly ConcurrentDictionary<Symbol, int> _lastBidSizes = new ConcurrentDictionary<Symbol, int>();
        private readonly ConcurrentDictionary<Symbol, decimal> _lastAskPrices = new ConcurrentDictionary<Symbol, decimal>();
        private readonly ConcurrentDictionary<Symbol, int> _lastAskSizes = new ConcurrentDictionary<Symbol, int>();
        private readonly ConcurrentDictionary<Symbol, int> _openInterests = new ConcurrentDictionary<Symbol, int>();
        private readonly List<Tick> _ticks = new List<Tick>();

        private static class AccountValueKeys
        {
            public const string CashBalance = "CashBalance";
            // public const string AccruedCash = "AccruedCash";
            // public const string NetLiquidationByCurrency = "NetLiquidationByCurrency";
        }

        // these are fatal errors from IB
        private static readonly HashSet<int> ErrorCodes = new HashSet<int>
        {
            100, 101, 103, 138, 139, 142, 143, 144, 145, 200, 203, 300,301,302,306,308,309,310,311,316,317,320,321,322,323,324,326,327,330,331,332,333,344,346,354,357,365,366,381,384,401,414,431,432,438,501,502,503,504,505,506,507,508,510,511,512,513,514,515,516,517,518,519,520,521,522,523,524,525,526,527,528,529,530,531,10000,10001,10005,10013,10015,10016,10021,10022,10023,10024,10025,10026,10027,1300
        };

        // these are warning messages from IB
        private static readonly HashSet<int> WarningCodes = new HashSet<int>
        {
            102, 104, 105, 106, 107, 109, 110, 111, 113, 114, 115, 116, 117, 118, 119, 120, 121, 122, 123, 124, 125, 126, 129, 131, 132, 133, 134, 135, 136, 137, 140, 141, 146, 151, 152, 153, 154, 155, 156, 157, 158, 159, 160, 161, 162, 163, 164, 165, 166, 167, 168, 201, 202, 303,313,314,315,319,325,328,329,334,335,336,337,338,339,340,341,342,343,345,347,348,349,350,352,353,355,356,358,359,360,361,362,363,364,367,368,369,370,371,372,373,374,375,376,377,378,379,380,382,383,385,386,387,388,389,390,391,392,393,394,395,396,397,398,399,400,402,403,404,405,406,407,408,409,410,411,412,413,417,418,419,420,421,422,423,424,425,426,427,428,429,430,433,434,435,436,437,439,440,441,442,443,444,445,446,447,448,449,450,1100,10002,10003,10006,10007,10008,10009,10010,10011,10012,10014,10018,10019,10020,10052,1101,1102,2100,2101,2102,2103,2104,2105,2106,2107,2108,2109,2110,2148
        };

        // these require us to issue invalidated order events
        private static readonly HashSet<int> InvalidatingCodes = new HashSet<int>
        {
            105, 106, 107, 109, 110, 111, 113, 114, 115, 116, 117, 118, 119, 120, 121, 122, 123, 124, 125, 126, 129, 131, 132, 133, 134, 135, 136, 137, 140, 141, 146, 147, 148, 151, 152, 153, 154, 155, 156, 157, 158, 159, 160, 161, 163, 167, 168, 201, 313,314,315,325,328,329,334,335,336,337,338,339,340,341,342,343,345,347,348,349,350,352,353,355,356,358,359,360,361,362,363,364,367,368,369,370,371,372,373,374,375,376,377,378,379,380,382,383,387,388,389,390,391,392,393,394,395,396,397,398,400,401,402,403,405,406,407,408,409,410,411,412,413,417,418,419,421,423,424,427,428,429,433,434,435,436,437,439,440,441,442,443,444,445,446,447,448,449,10002,10006,10007,10008,10009,10010,10011,10012,10014,10020,2102
        };
    }
}
