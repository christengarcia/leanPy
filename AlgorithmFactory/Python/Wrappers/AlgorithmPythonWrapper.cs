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

using NodaTime;
using Python.Runtime;
using QuantConnect.Algorithm;
using QuantConnect.Algorithm.Framework.Alphas;
using QuantConnect.Benchmarks;
using QuantConnect.Brokerages;
using QuantConnect.Data;
using QuantConnect.Data.UniverseSelection;
using QuantConnect.Exceptions;
using QuantConnect.Interfaces;
using QuantConnect.Notifications;
using QuantConnect.Orders;
using QuantConnect.Python;
using QuantConnect.Scheduling;
using QuantConnect.Securities;
using QuantConnect.Securities.Future;
using QuantConnect.Securities.Option;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace QuantConnect.AlgorithmFactory.Python.Wrappers
{
    /// <summary>
    /// Creates and wraps the algorithm written in python.
    /// </summary>
    public class AlgorithmPythonWrapper : IAlgorithm
    {
        private readonly dynamic _algorithm = null;
        private readonly IAlgorithm _baseAlgorithm;
        private readonly bool _isOnDataDefined = false;

        /// <summary>
        /// <see cref = "AlgorithmPythonWrapper"/> constructor.
        /// Creates and wraps the algorithm written in python.
        /// </summary>
        /// <param name="moduleName">Name of the module that can be found in the PYTHONPATH</param>
        public AlgorithmPythonWrapper(string moduleName)
        {
            try
            {
                using (Py.GIL())
                {
                    Logging.Log.Trace($"AlgorithmPythonWrapper(): Python version {PythonEngine.Version}: Importing python module {moduleName}");

                    var module = Py.Import(moduleName);

                    foreach (var name in module.Dir())
                    {
                        Type type;
                        var attr = module.GetAttr(name.ToString());
                        var repr = attr.Repr().GetStringBetweenChars('\'', '\'');

                        if (repr.StartsWith(moduleName) &&                // Must be defined in the module
                            attr.TryConvert(out type) &&                  // Must be a Type
                            typeof(QCAlgorithm).IsAssignableFrom(type))   // Must inherit from QCAlgorithm
                        {
                            Logging.Log.Trace("AlgorithmPythonWrapper(): Creating IAlgorithm instance.");

                            _algorithm = attr.Invoke();

                            // Set pandas
                            _algorithm.SetPandasConverter();

                            // IAlgorithm reference for LEAN internal C# calls (without going from C# to Python and back)
                            _baseAlgorithm = _algorithm.AsManagedObject(type);

                            // determines whether OnData method was defined or inherits from QCAlgorithm
                            // If it is not, OnData from the base class will not be called
                            var pythonType = (_algorithm as PyObject).GetAttr("OnData").GetPythonType();
                            _isOnDataDefined = pythonType.Repr().Equals("<class \'method\'>");
                        }
                    }

                    // If _algorithm could not be set, throw exception
                    if (_algorithm == null)
                    {
                        throw new Exception("Please ensure that one class inherits from QCAlgorithm or QCAlgorithmFramework.");
                    }
                }
            }
            catch (Exception e)
            {
                // perform exception interpretation for error in module import
                var interpreter = StackExceptionInterpreter.CreateFromAssemblies(AppDomain.CurrentDomain.GetAssemblies());
                e = interpreter.Interpret(e, interpreter);

                throw new Exception($"AlgorithmPythonWrapper(): {interpreter.GetExceptionMessageHeader(e)}");
            }
        }

        /// <summary>
        /// AlgorithmId for the backtest
        /// </summary>
        public string AlgorithmId => _baseAlgorithm.AlgorithmId;

        /// <summary>
        /// Gets the function used to define the benchmark. This function will return
        /// the value of the benchmark at a requested date/time
        /// </summary>
        public IBenchmark Benchmark => _baseAlgorithm.Benchmark;

        /// <summary>
        /// Gets the brokerage message handler used to decide what to do
        /// with each message sent from the brokerage
        /// </summary>
        public IBrokerageMessageHandler BrokerageMessageHandler
        {
            get
            {
                return _baseAlgorithm.BrokerageMessageHandler;
            }

            set
            {
                SetBrokerageMessageHandler(value);
            }
        }

        /// <summary>
        /// Gets the brokerage model used to emulate a real brokerage
        /// </summary>
        public IBrokerageModel BrokerageModel => _baseAlgorithm.BrokerageModel;

        /// <summary>
        /// Debug messages from the strategy:
        /// </summary>
        public ConcurrentQueue<string> DebugMessages => _baseAlgorithm.DebugMessages;

        /// <summary>
        /// Get Requested Backtest End Date
        /// </summary>
        public DateTime EndDate => _baseAlgorithm.EndDate;

        /// <summary>
        /// Error messages from the strategy:
        /// </summary>
        public ConcurrentQueue<string> ErrorMessages => _baseAlgorithm.ErrorMessages;

        /// <summary>
        /// Gets or sets the history provider for the algorithm
        /// </summary>
        public IHistoryProvider HistoryProvider
        {
            get
            {
                return _baseAlgorithm.HistoryProvider;
            }

            set
            {
                SetHistoryProvider(value);
            }
        }

        /// <summary>
        /// Gets a flag indicating whether or not this algorithm uses the QCAlgorithmFramework
        /// </summary>
        public bool IsFrameworkAlgorithm => _baseAlgorithm.IsFrameworkAlgorithm;

        /// <summary>
        /// Gets whether or not this algorithm is still warming up
        /// </summary>
        public bool IsWarmingUp => _baseAlgorithm.IsWarmingUp;

        /// <summary>
        /// Algorithm is running on a live server.
        /// </summary>
        public bool LiveMode => _baseAlgorithm.LiveMode;

        /// <summary>
        /// Log messages from the strategy:
        /// </summary>
        public ConcurrentQueue<string> LogMessages => _baseAlgorithm.LogMessages;

        /// <summary>
        /// Public name for the algorithm.
        /// </summary>
        /// <remarks>Not currently used but preserved for API integrity</remarks>
        public string Name
        {
            get
            {
                return _baseAlgorithm.Name;
            }
            set
            {
                _baseAlgorithm.Name = value;
            }
        }

        /// <summary>
        /// Notification manager for storing and processing live event messages
        /// </summary>
        public NotificationManager Notify => _baseAlgorithm.Notify;

        /// <summary>
        /// Security portfolio management class provides wrapper and helper methods for the Security.Holdings class such as
        /// IsLong, IsShort, TotalProfit
        /// </summary>
        /// <remarks>Portfolio is a wrapper and helper class encapsulating the Securities[].Holdings objects</remarks>
        public SecurityPortfolioManager Portfolio => _baseAlgorithm.Portfolio;

        /// <summary>
        /// Gets the run time error from the algorithm, or null if none was encountered.
        /// </summary>
        public Exception RunTimeError
        {
            get
            {
                return _baseAlgorithm.RunTimeError;
            }

            set
            {
                SetRunTimeError(value);
            }
        }

        /// <summary>
        /// Customizable dynamic statistics displayed during live trading:
        /// </summary>
        public ConcurrentDictionary<string, string> RuntimeStatistics => _baseAlgorithm.RuntimeStatistics;

        /// <summary>
        /// Gets schedule manager for adding/removing scheduled events
        /// </summary>
        public ScheduleManager Schedule => _baseAlgorithm.Schedule;

        /// <summary>
        /// Security object collection class stores an array of objects representing representing each security/asset
        /// we have a subscription for.
        /// </summary>
        /// <remarks>It is an IDictionary implementation and can be indexed by symbol</remarks>
        public SecurityManager Securities => _baseAlgorithm.Securities;

        /// <summary>
        /// Gets an instance that is to be used to initialize newly created securities.
        /// </summary>
        public ISecurityInitializer SecurityInitializer => _baseAlgorithm.SecurityInitializer;

        /// <summary>
        /// Gets the Trade Builder to generate trades from executions
        /// </summary>
        public ITradeBuilder TradeBuilder => _baseAlgorithm.TradeBuilder;

        /// <summary>
        /// Gets the user settings for the algorithm
        /// </summary>
        public AlgorithmSettings Settings => _baseAlgorithm.Settings;

        /// <summary>
        /// Gets the option chain provider, used to get the list of option contracts for an underlying symbol
        /// </summary>
        public IOptionChainProvider OptionChainProvider => _baseAlgorithm.OptionChainProvider;

        /// <summary>
        /// Gets the future chain provider, used to get the list of future contracts for an underlying symbol
        /// </summary>
        public IFutureChainProvider FutureChainProvider => _baseAlgorithm.FutureChainProvider;

        /// <summary>
        /// Algorithm start date for backtesting, set by the SetStartDate methods.
        /// </summary>
        public DateTime StartDate => _baseAlgorithm.StartDate;

        /// <summary>
        /// Gets or sets the current status of the algorithm
        /// </summary>
        public AlgorithmStatus Status
        {
            get
            {
                return _baseAlgorithm.Status;
            }

            set
            {
                SetStatus(value);
            }
        }

        /// <summary>
        /// Set the state of a live deployment
        /// </summary>
        /// <param name="status">Live deployment status</param>
        public void SetStatus(AlgorithmStatus status) => _baseAlgorithm.SetStatus(status);

        /// <summary>
        /// Set the available <see cref="TickType"/> supported by each <see cref="SecurityType"/> in <see cref="SecurityManager"/>
        /// </summary>
        /// <param name="availableDataTypes">>The different <see cref="TickType"/> each <see cref="Security"/> supports</param>
        public void SetAvailableDataTypes(Dictionary<SecurityType, List<TickType>> availableDataTypes) => _baseAlgorithm.SetAvailableDataTypes(availableDataTypes);

        /// <summary>
        /// Sets the option chain provider, used to get the list of option contracts for an underlying symbol
        /// </summary>
        /// <param name="optionChainProvider">The option chain provider</param>
        public void SetOptionChainProvider(IOptionChainProvider optionChainProvider) => _baseAlgorithm.SetOptionChainProvider(optionChainProvider);

        /// <summary>
        /// Sets the future chain provider, used to get the list of future contracts for an underlying symbol
        /// </summary>
        /// <param name="futureChainProvider">The future chain provider</param>
        public void SetFutureChainProvider(IFutureChainProvider futureChainProvider) => _baseAlgorithm.SetFutureChainProvider(futureChainProvider);

        /// <summary>
        /// Event fired when an algorithm generates a insight
        /// </summary>
        public event AlgorithmEvent<GeneratedInsightsCollection> InsightsGenerated
        {
            add
            {
                _baseAlgorithm.InsightsGenerated += value;
            }

            remove
            {
                _baseAlgorithm.InsightsGenerated -= value;
            }
        }

        /// <summary>
        /// Data subscription manager controls the information and subscriptions the algorithms recieves.
        /// Subscription configurations can be added through the Subscription Manager.
        /// </summary>
        public SubscriptionManager SubscriptionManager => _baseAlgorithm.SubscriptionManager;

        /// <summary>
        /// Current date/time in the algorithm's local time zone
        /// </summary>
        public DateTime Time => _baseAlgorithm.Time;

        /// <summary>
        /// Gets the time zone of the algorithm
        /// </summary>
        public DateTimeZone TimeZone => _baseAlgorithm.TimeZone;

        /// <summary>
        /// Security transaction manager class controls the store and processing of orders.
        /// </summary>
        /// <remarks>The orders and their associated events are accessible here. When a new OrderEvent is recieved the algorithm portfolio is updated.</remarks>
        public SecurityTransactionManager Transactions => _baseAlgorithm.Transactions;

        /// <summary>
        /// Gets the collection of universes for the algorithm
        /// </summary>
        public UniverseManager UniverseManager => _baseAlgorithm.UniverseManager;

        /// <summary>
        /// Gets the subscription settings to be used when adding securities via universe selection
        /// </summary>
        public UniverseSettings UniverseSettings => _baseAlgorithm.UniverseSettings;

        /// <summary>
        /// Current date/time in UTC.
        /// </summary>
        public DateTime UtcTime => _baseAlgorithm.UtcTime;

        /// <summary>
        /// Set a required SecurityType-symbol and resolution for algorithm
        /// </summary>
        /// <param name="securityType">SecurityType Enum: Equity, Commodity, FOREX or Future</param>
        /// <param name="symbol">Symbol Representation of the MarketType, e.g. AAPL</param>
        /// <param name="resolution">Resolution of the MarketType required: MarketData, Second or Minute</param>
        /// <param name="market">The market the requested security belongs to, such as 'usa' or 'fxcm'</param>
        /// <param name="fillDataForward">If true, returns the last available data even if none in that timeslice.</param>
        /// <param name="leverage">leverage for this security</param>
        /// <param name="extendedMarketHours">ExtendedMarketHours send in data from 4am - 8pm, not used for FOREX</param>
        public Security AddSecurity(SecurityType securityType, string symbol, Resolution resolution, string market, bool fillDataForward, decimal leverage, bool extendedMarketHours)
            => _baseAlgorithm.AddSecurity(securityType, symbol, resolution, market, fillDataForward, leverage, extendedMarketHours);

        /// <summary>
        /// Creates and adds a new single <see cref="Future"/> contract to the algorithm
        /// </summary>
        /// <param name="symbol">The futures contract symbol</param>
        /// <param name="resolution">The <see cref="Resolution"/> of market data, Tick, Second, Minute, Hour, or Daily. Default is <see cref="Resolution.Minute"/></param>
        /// <param name="fillDataForward">If true, returns the last available data even if none in that timeslice. Default is <value>true</value></param>
        /// <param name="leverage">The requested leverage for this equity. Default is set by <see cref="SecurityInitializer"/></param>
        /// <returns>The new <see cref="Future"/> security</returns>
        public Future AddFutureContract(Symbol symbol, Resolution resolution = Resolution.Minute, bool fillDataForward = true, decimal leverage = 0m)
            => _baseAlgorithm.AddFutureContract(symbol, resolution, fillDataForward, leverage);

        /// <summary>
        /// Creates and adds a new single <see cref="Option"/> contract to the algorithm
        /// </summary>
        /// <param name="symbol">The option contract symbol</param>
        /// <param name="resolution">The <see cref="Resolution"/> of market data, Tick, Second, Minute, Hour, or Daily. Default is <see cref="Resolution.Minute"/></param>
        /// <param name="fillDataForward">If true, returns the last available data even if none in that timeslice. Default is <value>true</value></param>
        /// <param name="leverage">The requested leverage for this equity. Default is set by <see cref="SecurityInitializer"/></param>
        /// <returns>The new <see cref="Option"/> security</returns>
        public Option AddOptionContract(Symbol symbol, Resolution resolution = Resolution.Minute, bool fillDataForward = true, decimal leverage = 0m)
            => _baseAlgorithm.AddOptionContract(symbol, resolution, fillDataForward, leverage);

        /// <summary>
        /// Invoked at the end of every time step. This allows the algorithm
        /// to process events before advancing to the next time step.
        /// </summary>
        public void OnEndOfTimeStep()
        {
            _baseAlgorithm.OnEndOfTimeStep();
        }

        /// <summary>
        /// Send debug message
        /// </summary>
        /// <param name="message">String message</param>
        public void Debug(string message) => _baseAlgorithm.Debug(message);

        /// <summary>
        /// Send an error message for the algorithm
        /// </summary>
        /// <param name="message">String message</param>
        public void Error(string message) => _baseAlgorithm.Error(message);

        /// <summary>
        /// Add a Chart object to algorithm collection
        /// </summary>
        /// <param name="chart">Chart object to add to collection.</param>
        public void AddChart(Chart chart) => _baseAlgorithm.AddChart(chart);

        /// <summary>
        /// Get the chart updates since the last request:
        /// </summary>
        /// <param name="clearChartData"></param>
        /// <returns>List of Chart Updates</returns>
        public List<Chart> GetChartUpdates(bool clearChartData = false) => _baseAlgorithm.GetChartUpdates(clearChartData);

        /// <summary>
        /// Gets whether or not this algorithm has been locked and fully initialized
        /// </summary>
        public bool GetLocked() => _baseAlgorithm.GetLocked();

        /// <summary>
        /// Gets the parameter with the specified name. If a parameter
        /// with the specified name does not exist, null is returned
        /// </summary>
        /// <param name="name">The name of the parameter to get</param>
        /// <returns>The value of the specified parameter, or null if not found</returns>
        public string GetParameter(string name) => _baseAlgorithm.GetParameter(name);

        /// <summary>
        /// Gets the history requests required for provide warm up data for the algorithm
        /// </summary>
        /// <returns></returns>
        public IEnumerable<HistoryRequest> GetWarmupHistoryRequests() => _baseAlgorithm.GetWarmupHistoryRequests();

        /// <summary>
        /// Initialise the Algorithm and Prepare Required Data:
        /// </summary>
        public void Initialize()
        {
            using (Py.GIL())
            {
                _algorithm.Initialize();
            }
        }

        /// <summary>
        /// Liquidate your portfolio holdings:
        /// </summary>
        /// <param name="symbolToLiquidate">Specific asset to liquidate, defaults to all.</param>
        /// <param name="tag">Custom tag to know who is calling this.</param>
        /// <returns>list of order ids</returns>
        public List<int> Liquidate(Symbol symbolToLiquidate = null, string tag = "Liquidated") => _baseAlgorithm.Liquidate(symbolToLiquidate, tag);

        /// <summary>
        /// Save entry to the Log
        /// </summary>
        /// <param name="message">String message</param>
        public void Log(string message) => _baseAlgorithm.Log(message);

        /// <summary>
        /// Brokerage disconnected event handler. This method is called when the brokerage connection is lost.
        /// </summary>
        public void OnBrokerageDisconnect()
        {
            using (Py.GIL())
            {
                _algorithm.OnBrokerageDisconnect();
            }
        }

        /// <summary>
        /// Brokerage message event handler. This method is called for all types of brokerage messages.
        /// </summary>
        public void OnBrokerageMessage(BrokerageMessageEvent messageEvent)
        {
            using (Py.GIL())
            {
                _algorithm.OnBrokerageMessage(messageEvent);
            }
        }

        /// <summary>
        /// Brokerage reconnected event handler. This method is called when the brokerage connection is restored after a disconnection.
        /// </summary>
        public void OnBrokerageReconnect()
        {
            using (Py.GIL())
            {
                _algorithm.OnBrokerageReconnect();
            }
        }

        /// <summary>
        /// v3.0 Handler for all data types
        /// </summary>
        /// <param name="slice">The current slice of data</param>
        public void OnData(Slice slice)
        {
            if (_isOnDataDefined)
            {
                using (Py.GIL())
                {
                    _algorithm.OnData(SubscriptionManager.HasCustomData ? new PythonSlice(slice) : slice);
                }
            }
        }

        /// <summary>
        /// Used to send data updates to algorithm framework models
        /// </summary>
        /// <param name="slice">The current data slice</param>
        public void OnFrameworkData(Slice slice) => _baseAlgorithm.OnFrameworkData(slice);

        /// <summary>
        /// Call this event at the end of the algorithm running.
        /// </summary>
        public void OnEndOfAlgorithm()
        {
            using (Py.GIL())
            {
                _algorithm.OnEndOfAlgorithm();
            }
        }

        /// <summary>
        /// End of a trading day event handler. This method is called at the end of the algorithm day (or multiple times if trading multiple assets).
        /// </summary>
        /// <remarks>Method is called 10 minutes before closing to allow user to close out position.</remarks>
        public void OnEndOfDay()
        {
            try
            {
                using (Py.GIL())
                {
                    _algorithm.OnEndOfDay();
                }
            }
            // If OnEndOfDay is not defined in the script, but OnEndOfDay(Symbol) is, a python exception occurs
            // Only throws if there is an error in its implementation body
            catch (PythonException exception)
            {
                if (!exception.Message.StartsWith("TypeError : OnEndOfDay()"))
                {
                    _baseAlgorithm.SetRunTimeError(exception);
                }
            }
        }

        /// <summary>
        /// End of a trading day event handler. This method is called at the end of the algorithm day (or multiple times if trading multiple assets).
        /// </summary>
        /// <remarks>
        /// This method is left for backwards compatibility and is invoked via <see cref="OnEndOfDay(Symbol)"/>, if that method is
        /// override then this method will not be called without a called to base.OnEndOfDay(string)
        /// </remarks>
        /// <param name="symbol">Asset symbol for this end of day event. Forex and equities have different closing hours.</param>
        public void OnEndOfDay(Symbol symbol)
        {
            try
            {
                using (Py.GIL())
                {
                    _algorithm.OnEndOfDay(symbol);
                }
            }
            // If OnEndOfDay(Symbol) is not defined in the script, but OnEndOfDay is, a python exception occurs
            // Only throws if there is an error in its implementation body
            catch (PythonException exception)
            {
                if (!exception.Message.StartsWith("TypeError : OnEndOfDay()"))
                {
                    _baseAlgorithm.SetRunTimeError(exception);
                }
            }
        }

        /// <summary>
        /// Margin call event handler. This method is called right before the margin call orders are placed in the market.
        /// </summary>
        /// <param name="requests">The orders to be executed to bring this algorithm within margin limits</param>
        public void OnMarginCall(List<SubmitOrderRequest> requests)
        {
            try
            {
                using (Py.GIL())
                {
                    var pyRequests = _algorithm.OnMarginCall(requests) as PyObject;

                    // If the method does not return or returns a non-iterable PyObject, throw an exception
                    if (pyRequests == null || !pyRequests.IsIterable())
                    {
                        throw new Exception("OnMarginCall must return a non-empty list of SubmitOrderRequest");
                    }

                    requests.Clear();

                    foreach (PyObject pyRequest in pyRequests)
                    {
                        SubmitOrderRequest request;
                        if (TryConvert(pyRequest, out request))
                        {
                            requests.Add(request);
                        }
                    }

                    // If the PyObject is an empty list or its items are not SubmitOrderRequest objects, throw an exception
                    if (requests.Count == 0)
                    {
                        throw new Exception("OnMarginCall must return a non-empty list of SubmitOrderRequest");
                    }
                }
            }
            catch (PythonException pythonException)
            {
                // Pythonnet generated error due to List conversion
                if (pythonException.Message.Contains("TypeError : No method matches given arguments"))
                {
                    _baseAlgorithm.OnMarginCall(requests);
                }
                // User code generated error
                else
                {
                    throw pythonException;
                }
            }
        }

        /// <summary>
        /// Margin call warning event handler. This method is called when Portoflio.MarginRemaining is under 5% of your Portfolio.TotalPortfolioValue
        /// </summary>
        public void OnMarginCallWarning()
        {
            using (Py.GIL())
            {
                _algorithm.OnMarginCallWarning();
            }
        }

        /// <summary>
        /// EXPERTS ONLY:: [-!-Async Code-!-]
        /// New order event handler: on order status changes (filled, partially filled, cancelled etc).
        /// </summary>
        /// <param name="newEvent">Event information</param>
        public void OnOrderEvent(OrderEvent newEvent)
        {
            using (Py.GIL())
            {
                _algorithm.OnOrderEvent(newEvent);
            }
        }

        /// <summary>
        /// Option assignment event handler. On an option assignment event for short legs the resulting information is passed to this method.
        /// </summary>
        /// <param name="assignmentEvent">Option exercise event details containing details of the assignment</param>
        /// <remarks>This method can be called asynchronously and so should only be used by seasoned C# experts. Ensure you use proper locks on thread-unsafe objects</remarks>
        public void OnAssignmentOrderEvent(OrderEvent assignmentEvent)
        {
            using (Py.GIL())
            {
                _algorithm.OnAssignmentOrderEvent(assignmentEvent);
            }
        }

        /// <summary>
        /// Event fired each time the we add/remove securities from the data feed
        /// </summary>
        /// <param name="changes">Security additions/removals for this time step</param>
        public void OnSecuritiesChanged(SecurityChanges changes)
        {
            using (Py.GIL())
            {
                _algorithm.OnSecuritiesChanged(changes);
            }
        }

        /// <summary>
        /// Used to send security changes to algorithm framework models
        /// </summary>
        /// <param name="changes">Security additions/removals for this time step</param>
        public void OnFrameworkSecuritiesChanged(SecurityChanges changes)
        {
            using (Py.GIL())
            {
                _algorithm.OnFrameworkSecuritiesChanged(changes);
            }
        }

        /// <summary>
        /// Called by setup handlers after Initialize and allows the algorithm a chance to organize
        /// the data gather in the Initialize method
        /// </summary>
        public void PostInitialize()
        {
            _baseAlgorithm.PostInitialize();
        }

        /// <summary>
        /// Called when the algorithm has completed initialization and warm up.
        /// </summary>
        public void OnWarmupFinished()
        {
            using (Py.GIL())
            {
                _algorithm.OnWarmupFinished();
            }
        }

        /// <summary>
        /// Removes the security with the specified symbol. This will cancel all
        /// open orders and then liquidate any existing holdings
        /// </summary>
        /// <param name="symbol">The symbol of the security to be removed</param>
        public bool RemoveSecurity(Symbol symbol) => _baseAlgorithm.RemoveSecurity(symbol);

        /// <summary>
        /// Set the algorithm Id for this backtest or live run. This can be used to identify the order and equity records.
        /// </summary>
        /// <param name="algorithmId">unique 32 character identifier for backtest or live server</param>
        public void SetAlgorithmId(string algorithmId) => _baseAlgorithm.SetAlgorithmId(algorithmId);

        /// <summary>
        /// Sets the implementation used to handle messages from the brokerage.
        /// The default implementation will forward messages to debug or error
        /// and when a <see cref="BrokerageMessageType.Error"/> occurs, the algorithm
        /// is stopped.
        /// </summary>
        /// <param name="handler">The message handler to use</param>
        public void SetBrokerageMessageHandler(IBrokerageMessageHandler handler) => _baseAlgorithm.SetBrokerageMessageHandler(handler);

        /// <summary>
        /// Sets the brokerage model used to resolve transaction models, settlement models,
        /// and brokerage specified ordering behaviors.
        /// </summary>
        /// <param name="brokerageModel">The brokerage model used to emulate the real
        /// brokerage</param>
        public void SetBrokerageModel(IBrokerageModel brokerageModel) => _baseAlgorithm.SetBrokerageModel(brokerageModel);

        /// <summary>
        /// Set the starting capital for the strategy
        /// </summary>
        /// <param name="startingCash">decimal starting capital, default $100,000</param>
        public void SetCash(decimal startingCash) => _baseAlgorithm.SetCash(startingCash);

        /// <summary>
        /// Set the cash for the specified symbol
        /// </summary>
        /// <param name="symbol">The cash symbol to set</param>
        /// <param name="startingCash">Decimal cash value of portfolio</param>
        /// <param name="conversionRate">The current conversion rate for the</param>
        public void SetCash(string symbol, decimal startingCash, decimal conversionRate) => _baseAlgorithm.SetCash(symbol, startingCash, conversionRate);

        /// <summary>
        /// Set the DateTime Frontier: This is the master time and is
        /// </summary>
        /// <param name="time"></param>
        public void SetDateTime(DateTime time) => _baseAlgorithm.SetDateTime(time);

        /// <summary>
        /// Set the runtime error
        /// </summary>
        /// <param name="exception">Represents error that occur during execution</param>
        public void SetRunTimeError(Exception exception) => _baseAlgorithm.SetRunTimeError(exception);

        /// <summary>
        /// Sets <see cref="IsWarmingUp"/> to false to indicate this algorithm has finished its warm up
        /// </summary>
        public void SetFinishedWarmingUp()
        {
            _baseAlgorithm.SetFinishedWarmingUp();

            // notify the algorithm
            OnWarmupFinished();
        }

        /// <summary>
        /// Set the historical data provider
        /// </summary>
        /// <param name="historyProvider">Historical data provider</param>
        public void SetHistoryProvider(IHistoryProvider historyProvider) => _baseAlgorithm.SetHistoryProvider(historyProvider);

        /// <summary>
        /// Set live mode state of the algorithm run: Public setter for the algorithm property LiveMode.
        /// </summary>
        /// <param name="live">Bool live mode flag</param>
        public void SetLiveMode(bool live) => _baseAlgorithm.SetLiveMode(live);

        /// <summary>
        /// Set the algorithm as initialized and locked. No more cash or security changes.
        /// </summary>
        public void SetLocked() => _baseAlgorithm.SetLocked();

        /// <summary>
        /// Set the maximum number of orders the algortihm is allowed to process.
        /// </summary>
        /// <param name="max">Maximum order count int</param>
        public void SetMaximumOrders(int max) => _baseAlgorithm.SetMaximumOrders(max);

        /// <summary>
        /// Sets the parameters from the dictionary
        /// </summary>
        /// <param name="parameters">Dictionary containing the parameter names to values</param>
        public void SetParameters(Dictionary<string, string> parameters) => _baseAlgorithm.SetParameters(parameters);

        /// <summary>
        /// Tries to convert a PyObject into a C# object
        /// </summary>
        /// <typeparam name="T">Type of the C# object</typeparam>
        /// <param name="pyObject">PyObject to be converted</param>
        /// <param name="result">C# object that of type T</param>
        /// <returns>True if successful conversion</returns>
        private bool TryConvert<T>(PyObject pyObject, out T result)
        {
            result = default(T);
            var type = (Type)pyObject.GetPythonType().AsManagedObject(typeof(Type));

            if (type == typeof(T))
            {
                result = (T)pyObject.AsManagedObject(typeof(T));
            }

            return type == typeof(T);
        }

        /// <summary>
        /// Returns a <see cref = "string"/> that represents the current <see cref = "AlgorithmPythonWrapper"/> object.
        /// </summary>
        /// <returns></returns>
        public override string ToString() => _algorithm == null ? base.ToString() : _algorithm.Repr();
    }
}