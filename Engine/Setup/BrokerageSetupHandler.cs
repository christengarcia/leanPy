﻿/*
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
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using QuantConnect.AlgorithmFactory;
using QuantConnect.Brokerages;
using QuantConnect.Brokerages.InteractiveBrokers;
using QuantConnect.Configuration;
using QuantConnect.Data.Market;
using QuantConnect.Interfaces;
using QuantConnect.Lean.Engine.DataFeeds;
using QuantConnect.Lean.Engine.RealTime;
using QuantConnect.Lean.Engine.Results;
using QuantConnect.Lean.Engine.TransactionHandlers;
using QuantConnect.Logging;
using QuantConnect.Packets;
using QuantConnect.Securities;
using QuantConnect.Util;

namespace QuantConnect.Lean.Engine.Setup
{
    /// <summary>
    /// Defines a set up handler that initializes the algorithm instance using values retrieved from the user's brokerage account
    /// </summary>
    public class BrokerageSetupHandler : ISetupHandler
    {
        /// <summary>
        /// Any errors from the initialization stored here:
        /// </summary>
        public List<Exception> Errors { get; set; }

        /// <summary>
        /// Get the maximum runtime for this algorithm job.
        /// </summary>
        public TimeSpan MaximumRuntime { get; private set; }

        /// <summary>
        /// Algorithm starting capital for statistics calculations
        /// </summary>
        public decimal StartingPortfolioValue { get; private set; }

        /// <summary>
        /// Start date for analysis loops to search for data.
        /// </summary>
        public DateTime StartingDate { get; private set; }

        /// <summary>
        /// Maximum number of orders for the algorithm run -- applicable for backtests only.
        /// </summary>
        public int MaxOrders { get; private set; }

        // saves ref to algo so we can call quit if runtime error encountered
        private IBrokerageFactory _factory;

        /// <summary>
        /// Initializes a new BrokerageSetupHandler
        /// </summary>
        public BrokerageSetupHandler()
        {
            Errors = new List<Exception>();
            MaximumRuntime = TimeSpan.FromDays(10*365);
            MaxOrders = int.MaxValue;
        }

        /// <summary>
        /// Create a new instance of an algorithm from a physical dll path.
        /// </summary>
        /// <param name="assemblyPath">The path to the assembly's location</param>
        /// <param name="algorithmNodePacket">Details of the task required</param>
        /// <returns>A new instance of IAlgorithm, or throws an exception if there was an error</returns>
        public IAlgorithm CreateAlgorithmInstance(AlgorithmNodePacket algorithmNodePacket, string assemblyPath)
        {
            string error;
            IAlgorithm algorithm;

            // limit load times to 10 seconds and force the assembly to have exactly one derived type
            var loader = new Loader(algorithmNodePacket.Language, TimeSpan.FromSeconds(15), names => names.SingleOrAlgorithmTypeName(Config.Get("algorithm-type-name")));
            var complete = loader.TryCreateAlgorithmInstanceWithIsolator(assemblyPath, algorithmNodePacket.RamAllocation, out algorithm, out error);
            if (!complete) throw new Exception(error + " Try re-building algorithm and remove duplicate QCAlgorithm base classes.");

            return algorithm;
        }

        /// <summary>
        /// Creates the brokerage as specified by the job packet
        /// </summary>
        /// <param name="algorithmNodePacket">Job packet</param>
        /// <param name="uninitializedAlgorithm">The algorithm instance before Initialize has been called</param>
        /// <param name="factory">The brokerage factory</param>
        /// <returns>The brokerage instance, or throws if error creating instance</returns>
        public IBrokerage CreateBrokerage(AlgorithmNodePacket algorithmNodePacket, IAlgorithm uninitializedAlgorithm, out IBrokerageFactory factory)
        {
            var liveJob = algorithmNodePacket as LiveNodePacket;
            if (liveJob == null)
            {
                throw new ArgumentException("BrokerageSetupHandler.CreateBrokerage requires a live node packet");
            }

            // find the correct brokerage factory based on the specified brokerage in the live job packet
            _factory = Composer.Instance.Single<IBrokerageFactory>(brokerageFactory => brokerageFactory.BrokerageType.MatchesTypeName(liveJob.Brokerage));
            factory = _factory;

            // initialize the correct brokerage using the resolved factory
            var brokerage = _factory.CreateBrokerage(liveJob, uninitializedAlgorithm);

            return brokerage;
        }

        /// <summary>
        /// Primary entry point to setup a new algorithm
        /// </summary>
        /// <param name="algorithm">Algorithm instance</param>
        /// <param name="brokerage">New brokerage output instance</param>
        /// <param name="job">Algorithm job task</param>
        /// <param name="resultHandler">The configured result handler</param>
        /// <param name="transactionHandler">The configurated transaction handler</param>
        /// <param name="realTimeHandler">The configured real time handler</param>
        /// <returns>True on successfully setting up the algorithm state, or false on error.</returns>
        public bool Setup(IAlgorithm algorithm, IBrokerage brokerage, AlgorithmNodePacket job, IResultHandler resultHandler, ITransactionHandler transactionHandler, IRealTimeHandler realTimeHandler)
        {
            // verify we were given the correct job packet type
            var liveJob = job as LiveNodePacket;
            if (liveJob == null)
            {
                AddInitializationError("BrokerageSetupHandler requires a LiveNodePacket");
                return false;
            }

            algorithm.Name = liveJob.GetAlgorithmName();

            // verify the brokerage was specified
            if (string.IsNullOrWhiteSpace(liveJob.Brokerage))
            {
                AddInitializationError("A brokerage must be specified");
                return false;
            }


            // attach to the message event to relay brokerage specific initialization messages
            EventHandler<BrokerageMessageEvent> brokerageOnMessage = (sender, args) =>
            {
                if (args.Type == BrokerageMessageType.Error)
                {
                    AddInitializationError(string.Format("Brokerage Error Code: {0} - {1}", args.Code, args.Message));
                }
            };

            try
            {
                Log.Trace("BrokerageSetupHandler.Setup(): Initializing algorithm...");

                resultHandler.SendStatusUpdate(AlgorithmStatus.Initializing, "Initializing algorithm...");

                //Execute the initialize code:
                var controls = job.Controls;
                var isolator = new Isolator();
                var initializeComplete = isolator.ExecuteWithTimeLimit(TimeSpan.FromSeconds(300), () =>
                {
                    try
                    {
                        //Set the default brokerage model before initialize
                        algorithm.SetBrokerageModel(_factory.BrokerageModel);

                        //Margin calls are disabled by default in live mode
                        algorithm.Portfolio.MarginCallModel = MarginCallModel.Null;

                        //Set our parameters
                        algorithm.SetParameters(job.Parameters);
                        algorithm.SetAvailableDataTypes(GetConfiguredDataFeeds());

                        //Algorithm is live, not backtesting:
                        algorithm.SetLiveMode(true);

                        //Initialize the algorithm's starting date
                        algorithm.SetDateTime(DateTime.UtcNow);

                        //Set the source impl for the event scheduling
                        algorithm.Schedule.SetEventSchedule(realTimeHandler);

                        // set the option chain provider
                        algorithm.SetOptionChainProvider(new CachingOptionChainProvider(new LiveOptionChainProvider()));

                        // set the future chain provider
                        algorithm.SetFutureChainProvider(new CachingFutureChainProvider(new LiveFutureChainProvider()));

                        // If we're going to receive market data from IB,
                        // set the default subscription limit to 100,
                        // algorithms can override this setting in the Initialize method
                        if (brokerage is InteractiveBrokersBrokerage &&
                            liveJob.DataQueueHandler.EndsWith("InteractiveBrokersBrokerage"))
                        {
                            algorithm.Settings.DataSubscriptionLimit = 100;
                        }

                        //Initialise the algorithm, get the required data:
                        algorithm.Initialize();

                        //Finalize Initialization
                        algorithm.PostInitialize();

                        if (liveJob.Brokerage != "PaperBrokerage")
                        {
                            //Zero the CashBook - we'll populate directly from brokerage
                            foreach (var kvp in algorithm.Portfolio.CashBook)
                            {
                                kvp.Value.SetAmount(0);
                            }
                        }
                    }
                    catch (Exception err)
                    {
                        AddInitializationError(err.ToString(), err);
                    }
                }, controls.RamAllocation);

                if (!initializeComplete)
                {
                    AddInitializationError("Initialization timed out.");
                    return false;
                }

                // let the world know what we're doing since logging in can take a minute
                resultHandler.SendStatusUpdate(AlgorithmStatus.LoggingIn, "Logging into brokerage...");

                brokerage.Message += brokerageOnMessage;

                Log.Trace("BrokerageSetupHandler.Setup(): Connecting to brokerage...");
                try
                {
                    // this can fail for various reasons, such as already being logged in somewhere else
                    brokerage.Connect();
                }
                catch (Exception err)
                {
                    Log.Error(err);
                    AddInitializationError(string.Format("Error connecting to brokerage: {0}. " +
                        "This may be caused by incorrect login credentials or an unsupported account type.", err.Message), err);
                    return false;
                }

                if (!brokerage.IsConnected)
                {
                    // if we're reporting that we're not connected, bail
                    AddInitializationError("Unable to connect to brokerage.");
                    return false;
                }

                Log.Trace("BrokerageSetupHandler.Setup(): Fetching cash balance from brokerage...");
                try
                {
                    // set the algorithm's cash balance for each currency
                    var cashBalance = brokerage.GetCashBalance();
                    foreach (var cash in cashBalance)
                    {
                        Log.Trace("BrokerageSetupHandler.Setup(): Setting " + cash.Symbol + " cash to " + cash.Amount);
                        algorithm.Portfolio.SetCash(cash.Symbol, cash.Amount, cash.ConversionRate);
                    }
                }
                catch (Exception err)
                {
                    Log.Error(err);
                    AddInitializationError("Error getting cash balance from brokerage: " + err.Message, err);
                    return false;
                }

                Log.Trace("BrokerageSetupHandler.Setup(): Fetching open orders from brokerage...");
                try
                {
                    GetOpenOrders(algorithm, resultHandler, transactionHandler, brokerage);
                }
                catch (Exception err)
                {
                    Log.Error(err);
                    AddInitializationError("Error getting open orders from brokerage: " + err.Message, err);
                    return false;
                }

                Log.Trace("BrokerageSetupHandler.Setup(): Fetching holdings from brokerage...");
                try
                {
                    // populate the algorithm with the account's current holdings
                    var holdings = brokerage.GetAccountHoldings();
                    var supportedSecurityTypes = new HashSet<SecurityType>
                    {
                        SecurityType.Equity, SecurityType.Forex, SecurityType.Cfd, SecurityType.Option, SecurityType.Future, SecurityType.Crypto
                    };
                    var minResolution = new Lazy<Resolution>(() => algorithm.Securities.Select(x => x.Value.Resolution).DefaultIfEmpty(Resolution.Second).Min());
                    foreach (var holding in holdings)
                    {
                        Log.Trace("BrokerageSetupHandler.Setup(): Has existing holding: " + holding);

                        // verify existing holding security type
                        if (!supportedSecurityTypes.Contains(holding.Type))
                        {
                            Log.Error("BrokerageSetupHandler.Setup(): Unsupported security type: " + holding.Type + "-" + holding.Symbol.Value);
                            AddInitializationError("Found unsupported security type in existing brokerage holdings: " + holding.Type + ". " +
                                "QuantConnect currently supports the following security types: " + string.Join(",", supportedSecurityTypes));

                            // keep aggregating these errors
                            continue;
                        }

                        if (!algorithm.Portfolio.ContainsKey(holding.Symbol))
                        {
                            Log.Trace("BrokerageSetupHandler.Setup(): Adding unrequested security: " + holding.Symbol.Value);

                            if (holding.Type == SecurityType.Option)
                            {
                                // add current option contract to the system
                                algorithm.AddOptionContract(holding.Symbol, minResolution.Value, true, 1.0m);
                            }
                            else if (holding.Type == SecurityType.Future)
                            {
                                // add current future contract to the system
                                algorithm.AddFutureContract(holding.Symbol, minResolution.Value, true, 1.0m);
                            }
                            else
                            {
                                // for items not directly requested set leverage to 1 and at the min resolution
                                algorithm.AddSecurity(holding.Type, holding.Symbol.Value, minResolution.Value, null, true, 1.0m, false);
                            }
                        }

                        algorithm.Portfolio[holding.Symbol].SetHoldings(holding.AveragePrice, holding.Quantity);
                        algorithm.Securities[holding.Symbol].SetMarketPrice(new TradeBar
                        {
                            Time = DateTime.Now,
                            Open = holding.MarketPrice,
                            High = holding.MarketPrice,
                            Low = holding.MarketPrice,
                            Close = holding.MarketPrice,
                            Volume = 0,
                            Symbol = holding.Symbol,
                            DataType = MarketDataType.TradeBar
                        });
                    }
                }
                catch (Exception err)
                {
                    Log.Error(err);
                    AddInitializationError("Error getting account holdings from brokerage: " + err.Message, err);
                    return false;
                }

                //Set the starting portfolio value for the strategy to calculate performance:
                StartingPortfolioValue = algorithm.Portfolio.TotalPortfolioValue;
                StartingDate = DateTime.Now;
            }
            catch (Exception err)
            {
                AddInitializationError(err.ToString(), err);
            }
            finally
            {
                if (brokerage != null)
                {
                    brokerage.Message -= brokerageOnMessage;
                }
            }

            return Errors.Count == 0;
        }

        /// <summary>
        /// Get the open orders from a brokerage. Adds <see cref="Orders.Order"/> and <see cref="Orders.OrderTicket"/> to the transaction handler
        /// </summary>
        /// <param name="algorithm">Algorithm instance</param>
        /// <param name="resultHandler">The configured result handler</param>
        /// <param name="transactionHandler">The configurated transaction handler</param>
        /// <param name="brokerage">Brokerage output instance</param>
        protected void GetOpenOrders(IAlgorithm algorithm, IResultHandler resultHandler, ITransactionHandler transactionHandler, IBrokerage brokerage)
        {
            // populate the algorithm with the account's outstanding orders  resultHandler
            var openOrders = brokerage.GetOpenOrders();
            foreach (var order in openOrders)
            {
                // be sure to assign order IDs such that we increment from the SecurityTransactionManager to avoid ID collisions
                Log.Trace("BrokerageSetupHandler.Setup(): Has open order: " + order.Symbol.Value + " - " + order.Quantity);
                resultHandler.DebugMessage($"BrokerageSetupHandler.Setup(): Open order detected.  Creating order tickets for open order {order.Symbol.Value} with quantity {order.Quantity}. Beware that this order ticket may not accurately reflect the quantity of the order if the open order is partially filled.");
                order.Id = algorithm.Transactions.GetIncrementOrderId();
                transactionHandler.Orders.AddOrUpdate(order.Id, order, (i, o) => order);
                transactionHandler.OrderTickets.AddOrUpdate(order.Id, order.ToOrderTicket(algorithm.Transactions));
            }
        }

        /// <summary>
        /// Get the available data feeds from config.json,
        /// If none available, throw an error
        /// </summary>
        private static Dictionary<SecurityType, List<TickType>> GetConfiguredDataFeeds()
        {
            var dataFeedsConfigString = Config.Get("security-data-feeds");

            Dictionary<SecurityType, List<TickType>> dataFeeds = new Dictionary<SecurityType, List<TickType>>();
            if (dataFeedsConfigString != string.Empty)
            {
                dataFeeds = JsonConvert.DeserializeObject<Dictionary<SecurityType, List<TickType>>>(dataFeedsConfigString);
            }

            return dataFeeds;
        }


        /// <summary>
        /// Adds initializaion error to the Errors list
        /// </summary>
        /// <param name="message">The error message to be added</param>
        /// <param name="inner">The inner exception being wrapped</param>
        private void AddInitializationError(string message, Exception inner = null)
        {
            Errors.Add(new AlgorithmSetupException("During the algorithm initialization, the following exception has occurred: " + message, inner));
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        /// <filterpriority>2</filterpriority>
        public void Dispose()
        {
            if (_factory != null)
            {
                _factory.Dispose();
            }
        }
    }
}
