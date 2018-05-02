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

using Python.Runtime;
using QuantConnect.Algorithm;
using QuantConnect.Configuration;
using QuantConnect.Data;
using QuantConnect.Data.Fundamental;
using QuantConnect.Data.Market;
using QuantConnect.Indicators;
using QuantConnect.Interfaces;
using QuantConnect.Lean.Engine;
using QuantConnect.Lean.Engine.DataFeeds;
using QuantConnect.Python;
using QuantConnect.Securities;
using QuantConnect.Securities.Future;
using QuantConnect.Securities.Option;
using QuantConnect.Statistics;
using QuantConnect.Util;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace QuantConnect.Jupyter
{
    /// <summary>
    /// Provides access to data for quantitative analysis
    /// </summary>
    public class QuantBook : QCAlgorithm
    {
        private dynamic _pandas;
        private IDataCacheProvider _dataCacheProvider;
        
        /// <summary>
        /// <see cref = "QuantBook" /> constructor.
        /// Provides access to data for quantitative analysis
        /// </summary>
        public QuantBook() : base()
        {
            try
            {
                using (Py.GIL())
                {
                    _pandas = Py.Import("pandas");
                }

                // By default, set start date to end data which is yesterday
                SetStartDate(EndDate);

                // Sets PandasConverter
                SetPandasConverter();

                // Initialize History Provider
                var composer = new Composer();
                var algorithmHandlers = LeanEngineAlgorithmHandlers.FromConfiguration(composer);
                _dataCacheProvider = new ZipDataCacheProvider(algorithmHandlers.DataProvider);

                var mapFileProvider = algorithmHandlers.MapFileProvider;
                HistoryProvider = composer.GetExportedValueByTypeName<IHistoryProvider>(Config.Get("history-provider", "SubscriptionDataReaderHistoryProvider"));
                HistoryProvider.Initialize(null, algorithmHandlers.DataProvider, _dataCacheProvider, mapFileProvider, algorithmHandlers.FactorFileProvider, null);

                SetOptionChainProvider(new CachingOptionChainProvider(new BacktestingOptionChainProvider()));
                SetFutureChainProvider(new CachingFutureChainProvider(new BacktestingFutureChainProvider()));
            }
            catch (Exception exception)
            {
                throw new Exception("QuantBook.Main(): " + exception);
            }
        }

        /// <summary>
        /// Get fundamental data from given symbols
        /// </summary>
        /// <param name="pyObject">The symbols to retrieve fundamental data for</param>
        /// <param name="selector">Selects a value from the Fundamental data to filter the request output</param>
        /// <param name="start">The start date of selected data</param>
        /// <param name="end">The end date of selected data</param>
        /// <returns></returns>
        public PyObject GetFundamental(PyObject tickers, string selector, DateTime? start = null, DateTime? end = null)
        {
            if (string.IsNullOrWhiteSpace(selector))
            {
                return "Invalid selector. Cannot be None, empty or consist only of white-space characters".ToPython();
            }

            using (Py.GIL())
            {
                // If tickers are not a PyList, we create one
                if (!PyList.IsListType(tickers))
                {
                    var tmp = new PyList();
                    tmp.Append(tickers);
                    tickers = tmp;
                }

                var list = new List<Tuple<Symbol, DateTime, object>>();

                foreach (var ticker in tickers)
                {
                    var symbol = QuantConnect.Symbol.Create(ticker.ToString(), SecurityType.Equity, Market.USA);
                    var dir = new DirectoryInfo(Path.Combine(Globals.DataFolder, "equity", symbol.ID.Market, "fundamental", "fine", symbol.Value.ToLower()));
                    if (!dir.Exists) continue;

                    var config = new SubscriptionDataConfig(typeof(FineFundamental), symbol, Resolution.Daily, TimeZones.NewYork, TimeZones.NewYork, false, false, false);

                    foreach (var fileName in dir.EnumerateFiles())
                    {
                        var date = DateTime.ParseExact(fileName.Name.Substring(0, 8), DateFormat.EightCharacter, CultureInfo.InvariantCulture);
                        if (date < start || date > end) continue;

                        var factory = new TextSubscriptionDataSourceReader(_dataCacheProvider, config, date, false);
                        var source = new SubscriptionDataSource(fileName.FullName, SubscriptionTransportMedium.LocalFile);
                        var value = factory.Read(source).Select(x => GetPropertyValue(x, selector)).First();

                        list.Add(Tuple.Create(symbol, date, value));
                    }
                }

                var data = new PyDict();
                foreach (var item in list.GroupBy(x => x.Item1))
                {
                    var index = item.Select(x => x.Item2);
                    data.SetItem(item.Key, _pandas.Series(item.Select(x => x.Item3).ToList(), index));
                }

                return _pandas.DataFrame(data);
            }
        }

        /// <summary>
        /// Gets <see cref="OptionHistory"/> object for a given symbol, date and resolution
        /// </summary>
        /// <param name="symbol">The symbol to retrieve historical option data for</param>
        /// <param name="date">Date of the data</param>
        /// <param name="resolution">The resolution to request</param>
        /// <returns>A <see cref="OptionHistory"/> object that contains historical option data.</returns>
        public OptionHistory GetOptionHistory(Symbol symbol, DateTime start, DateTime? end = null, Resolution? resolution = null)
        {
            if (!end.HasValue || end.Value.Date == start.Date)
            {
                end = start.AddDays(1);
            }

            var option = Securities[symbol] as Option;
            var underlying = AddEquity(symbol.Underlying.Value, option.Resolution);

            var allSymbols = new List<Symbol>();
            for (var date = start; date < end; date = date.AddDays(1))
            {
                if (option.Exchange.DateIsOpen(date))
                {
                    allSymbols.AddRange(OptionChainProvider.GetOptionContractList(symbol.Underlying, date));
                }
            }
            var symbols = base.History(symbol.Underlying, start, end.Value, resolution)
                .SelectMany(x => option.ContractFilter.Filter(new OptionFilterUniverse(allSymbols.Distinct(), x)))
                .Distinct().Concat(new[] { symbol.Underlying });

            return new OptionHistory(History(symbols, start, end.Value, resolution));
        }

        /// <summary>
        /// Gets <see cref="FutureHistory"/> object for a given symbol, date and resolution
        /// </summary>
        /// <param name="symbol">The symbol to retrieve historical future data for</param>
        /// <param name="start">Date of the data</param>
        /// <param name="resolution">The resolution to request</param>
        /// <returns>A <see cref="FutureHistory"/> object that contains historical future data.</returns>
        public FutureHistory GetFutureHistory(Symbol symbol, DateTime start, DateTime? end = null, Resolution? resolution = null)
        {
            if (!end.HasValue || end.Value.Date == start.Date)
            {
                end = start.AddDays(1);
            }

            var future = Securities[symbol] as Future;

            var allSymbols = new List<Symbol>();
            for (var date = start; date < end; date = date.AddDays(1))
            {
                if (future.Exchange.DateIsOpen(date))
                {
                    allSymbols.AddRange(FutureChainProvider.GetFutureContractList(future.Symbol, date));
                }
            }
            var symbols = allSymbols.Distinct();

            return new FutureHistory(History(symbols, start, end.Value, resolution));
        }

        /// <summary>
        /// Gets the historical data of an indicator for the specified symbol. The exact number of bars will be returned. 
        /// The symbol must exist in the Securities collection.
        /// </summary>
        /// <param name="symbol">The symbol to retrieve historical data for</param>
        /// <param name="periods">The number of bars to request</param>
        /// <param name="resolution">The resolution to request</param>
        /// <param name="selector">Selects a value from the BaseData to send into the indicator, if null defaults to the Value property of BaseData (x => x.Value)</param>
        /// <returns>pandas.DataFrame of historical data of an indicator</returns>
        public PyObject Indicator(IndicatorBase<IndicatorDataPoint> indicator, Symbol symbol, int period, Resolution? resolution = null, Func<IBaseData, decimal> selector = null)
        {
            var history = History<IBaseData>(symbol, period, resolution);
            return Indicator(indicator, history, selector);
        }

        /// <summary>
        /// Gets the historical data of a bar indicator for the specified symbol. The exact number of bars will be returned. 
        /// The symbol must exist in the Securities collection.
        /// </summary>
        /// <param name="symbol">The symbol to retrieve historical data for</param>
        /// <param name="periods">The number of bars to request</param>
        /// <param name="resolution">The resolution to request</param>
        /// <param name="selector">Selects a value from the BaseData to send into the indicator, if null defaults to the Value property of BaseData (x => x.Value)</param>
        /// <returns>pandas.DataFrame of historical data of a bar indicator</returns>
        public PyObject Indicator(IndicatorBase<IBaseDataBar> indicator, Symbol symbol, int period, Resolution? resolution = null, Func<IBaseData, IBaseDataBar> selector = null)
        {
            var history = History<IBaseDataBar>(symbol, period, resolution);
            return Indicator(indicator, history, selector);
        }

        /// <summary>
        /// Gets the historical data of a bar indicator for the specified symbol. The exact number of bars will be returned. 
        /// The symbol must exist in the Securities collection.
        /// </summary>
        /// <param name="symbol">The symbol to retrieve historical data for</param>
        /// <param name="periods">The number of bars to request</param>
        /// <param name="resolution">The resolution to request</param>
        /// <param name="selector">Selects a value from the BaseData to send into the indicator, if null defaults to the Value property of BaseData (x => x.Value)</param>
        /// <returns>pandas.DataFrame of historical data of a bar indicator</returns>
        public PyObject Indicator(IndicatorBase<TradeBar> indicator, Symbol symbol, int period, Resolution? resolution = null, Func<IBaseData, TradeBar> selector = null)
        {
            var history = History<TradeBar>(symbol, period, resolution);
            return Indicator(indicator, history, selector);
        }

        /// <summary>
        /// Gets the historical data of an indicator for the specified symbol. The exact number of bars will be returned. 
        /// The symbol must exist in the Securities collection.
        /// </summary>
        /// <param name="indicator">Indicator</param>
        /// <param name="symbol">The symbol to retrieve historical data for</param>
        /// <param name="span">The span over which to retrieve recent historical data</param>
        /// <param name="resolution">The resolution to request</param>
        /// <param name="selector">Selects a value from the BaseData to send into the indicator, if null defaults to the Value property of BaseData (x => x.Value)</param>
        /// <returns>pandas.DataFrame of historical data of an indicator</returns>
        public PyObject Indicator(IndicatorBase<IndicatorDataPoint> indicator, Symbol symbol, TimeSpan span, Resolution? resolution = null, Func<IBaseData, decimal> selector = null)
        {
            var history = base.History<IBaseData>(symbol, span, resolution);
            return Indicator(indicator, history, selector);
        }

        /// <summary>
        /// Gets the historical data of a bar indicator for the specified symbol. The exact number of bars will be returned. 
        /// The symbol must exist in the Securities collection.
        /// </summary>
        /// <param name="indicator">Indicator</param>
        /// <param name="symbol">The symbol to retrieve historical data for</param>
        /// <param name="span">The span over which to retrieve recent historical data</param>
        /// <param name="resolution">The resolution to request</param>
        /// <param name="selector">Selects a value from the BaseData to send into the indicator, if null defaults to the Value property of BaseData (x => x.Value)</param>
        /// <returns>pandas.DataFrame of historical data of a bar indicator</returns>
        public PyObject Indicator(IndicatorBase<IBaseDataBar> indicator, Symbol symbol, TimeSpan span, Resolution? resolution = null, Func<IBaseData, IBaseDataBar> selector = null)
        {
            var history = base.History<IBaseDataBar>(symbol, span, resolution);
            return Indicator(indicator, history, selector);
        }

        /// <summary>
        /// Gets the historical data of a bar indicator for the specified symbol. The exact number of bars will be returned. 
        /// The symbol must exist in the Securities collection.
        /// </summary>
        /// <param name="indicator">Indicator</param>
        /// <param name="symbol">The symbol to retrieve historical data for</param>
        /// <param name="span">The span over which to retrieve recent historical data</param>
        /// <param name="resolution">The resolution to request</param>
        /// <param name="selector">Selects a value from the BaseData to send into the indicator, if null defaults to the Value property of BaseData (x => x.Value)</param>
        /// <returns>pandas.DataFrame of historical data of a bar indicator</returns>
        public PyObject Indicator(IndicatorBase<TradeBar> indicator, Symbol symbol, TimeSpan span, Resolution? resolution = null, Func<IBaseData, TradeBar> selector = null)
        {
            var history = base.History<TradeBar>(symbol, span, resolution);
            return Indicator(indicator, history, selector);
        }

        /// <summary>
        /// Gets the historical data of an indicator for the specified symbol. The exact number of bars will be returned. 
        /// The symbol must exist in the Securities collection.
        /// </summary>
        /// <param name="indicator">Indicator</param>
        /// <param name="symbol">The symbol to retrieve historical data for</param>
        /// <param name="start">The start time in the algorithm's time zone</param>
        /// <param name="end">The end time in the algorithm's time zone</param>
        /// <param name="resolution">The resolution to request</param>
        /// <param name="selector">Selects a value from the BaseData to send into the indicator, if null defaults to the Value property of BaseData (x => x.Value)</param>
        /// <returns>pandas.DataFrame of historical data of an indicator</returns>
        public PyObject Indicator(IndicatorBase<IndicatorDataPoint> indicator, Symbol symbol, DateTime start, DateTime end, Resolution? resolution = null, Func<IBaseData, decimal> selector = null)
        {
            var history = History<IBaseData>(symbol, start, end, resolution);
            return Indicator(indicator, history, selector);
        }

        /// <summary>
        /// Gets the historical data of a bar indicator for the specified symbol. The exact number of bars will be returned. 
        /// The symbol must exist in the Securities collection.
        /// </summary>
        /// <param name="indicator">Indicator</param>
        /// <param name="symbol">The symbol to retrieve historical data for</param>
        /// <param name="start">The start time in the algorithm's time zone</param>
        /// <param name="end">The end time in the algorithm's time zone</param>
        /// <param name="resolution">The resolution to request</param>
        /// <param name="selector">Selects a value from the BaseData to send into the indicator, if null defaults to the Value property of BaseData (x => x.Value)</param>
        /// <returns>pandas.DataFrame of historical data of a bar indicator</returns>
        public PyObject Indicator(IndicatorBase<IBaseDataBar> indicator, Symbol symbol, DateTime start, DateTime end, Resolution? resolution = null, Func<IBaseData, IBaseDataBar> selector = null)
        {
            var history = History<IBaseDataBar>(symbol, start, end, resolution);
            return Indicator(indicator, history, selector);
        }

        /// <summary>
        /// Gets the historical data of a bar indicator for the specified symbol. The exact number of bars will be returned. 
        /// The symbol must exist in the Securities collection.
        /// </summary>
        /// <param name="indicator">Indicator</param>
        /// <param name="symbol">The symbol to retrieve historical data for</param>
        /// <param name="start">The start time in the algorithm's time zone</param>
        /// <param name="end">The end time in the algorithm's time zone</param>
        /// <param name="resolution">The resolution to request</param>
        /// <param name="selector">Selects a value from the BaseData to send into the indicator, if null defaults to the Value property of BaseData (x => x.Value)</param>
        /// <returns>pandas.DataFrame of historical data of a bar indicator</returns>
        public PyObject Indicator(IndicatorBase<TradeBar> indicator, Symbol symbol, DateTime start, DateTime end, Resolution? resolution = null, Func<IBaseData, TradeBar> selector = null)
        {
            var history = History<TradeBar>(symbol, start, end, resolution);
            return Indicator(indicator, history, selector);
        }

        /// <summary>
        /// Gets Portfolio Statistics from a pandas.DataFrame with equity and benchmark values
        /// </summary>
        /// <param name="dataFrame">pandas.DataFrame with the information required to compute the Portfolio statistics</param>
        /// <returns><see cref="PortfolioStatistics"/> object wrapped in a <see cref="PyDict"/> with the portfolio statistics.</returns>
        public PyDict GetPortfolioStatistics(PyObject dataFrame)
        {
            var dictBenchmark = new SortedDictionary<DateTime, double>();
            var dictEquity = new SortedDictionary<DateTime, double>();
            var dictPL = new SortedDictionary<DateTime, double>();

            using (Py.GIL())
            {
                var result = new PyDict();

                try
                {
                    // Converts the data from pandas.DataFrame into dictionaries keyed by time
                    var df = ((dynamic)dataFrame).dropna();
                    dictBenchmark = GetDictionaryFromSeries((PyObject)df["benchmark"]);
                    dictEquity = GetDictionaryFromSeries((PyObject)df["equity"]);
                    dictPL = GetDictionaryFromSeries((PyObject)df["equity"].pct_change());
                }
                catch (PythonException e)
                {
                    result.SetItem("Runtime Error", e.Message.ToPython());
                    return result;
                }

                // Convert the double into decimal
                var equity = new SortedDictionary<DateTime, decimal>(dictEquity.ToDictionary(kvp => kvp.Key, kvp => (decimal)kvp.Value));
                var profitLoss = new SortedDictionary<DateTime, decimal>(dictPL.ToDictionary(kvp => kvp.Key, kvp => double.IsNaN(kvp.Value) ? 0 : (decimal)kvp.Value));

                // Gets the last value of the day of the benchmark and equity
                var listBenchmark = CalculateDailyRateOfChange(dictBenchmark);
                var listPerformance = CalculateDailyRateOfChange(dictEquity);

                // Gets the startting capital
                var startingCapital = Convert.ToDecimal(dictEquity.FirstOrDefault().Value);

                // Compute portfolio statistics
                var stats = new PortfolioStatistics(profitLoss, equity, listPerformance, listBenchmark, startingCapital);

                result.SetItem("Average Win (%)", Convert.ToDouble(stats.AverageWinRate * 100).ToPython());
                result.SetItem("Average Loss (%)", Convert.ToDouble(stats.AverageLossRate * 100).ToPython());
                result.SetItem("Compounding Annual Return (%)", Convert.ToDouble(stats.CompoundingAnnualReturn * 100m).ToPython());
                result.SetItem("Drawdown (%)", Convert.ToDouble(stats.Drawdown * 100).ToPython());
                result.SetItem("Expectancy", Convert.ToDouble(stats.Expectancy).ToPython());
                result.SetItem("Net Profit (%)", Convert.ToDouble(stats.TotalNetProfit * 100).ToPython());
                result.SetItem("Sharpe Ratio", Convert.ToDouble(stats.SharpeRatio).ToPython());
                result.SetItem("Win Rate (%)", Convert.ToDouble(stats.WinRate * 100).ToPython());
                result.SetItem("Loss Rate (%)", Convert.ToDouble(stats.LossRate * 100).ToPython());
                result.SetItem("Profit-Loss Ratio", Convert.ToDouble(stats.ProfitLossRatio).ToPython());
                result.SetItem("Alpha", Convert.ToDouble(stats.Alpha).ToPython());
                result.SetItem("Beta", Convert.ToDouble(stats.Beta).ToPython());
                result.SetItem("Annual Standard Deviation", Convert.ToDouble(stats.AnnualStandardDeviation).ToPython());
                result.SetItem("Annual Variance", Convert.ToDouble(stats.AnnualVariance).ToPython());
                result.SetItem("Information Ratio", Convert.ToDouble(stats.InformationRatio).ToPython());
                result.SetItem("Tracking Error", Convert.ToDouble(stats.TrackingError).ToPython());
                result.SetItem("Treynor Ratio", Convert.ToDouble(stats.TreynorRatio).ToPython());

                return result;
            }
        }

        /// <summary>
        /// Converts a pandas.Series into a <see cref="SortedDictionary{DateTime, Double}"/>
        /// </summary>
        /// <param name="series">pandas.Series to be converted</param>
        /// <returns><see cref="SortedDictionary{DateTime, Double}"/> with pandas.Series information</returns>
        private SortedDictionary<DateTime, double> GetDictionaryFromSeries(PyObject series)
        {
            var dictionary = new SortedDictionary<DateTime, double>();

            var pyDict = new PyDict(((dynamic)series).to_dict());
            foreach (PyObject item in pyDict.Items())
            {
                var key = (DateTime)item[0].AsManagedObject(typeof(DateTime));
                var value = (double)item[1].AsManagedObject(typeof(double));
                dictionary.Add(key, value);
            }

            return dictionary;
        }

        /// <summary>
        /// Calculates the daily rate of change 
        /// </summary>
        /// <param name="dictionary"><see cref="IDictionary{DateTime, Double}"/> with prices keyed by time</param>
        /// <returns><see cref="List{Double}"/> with daily rate of change</returns>
        private List<double> CalculateDailyRateOfChange(IDictionary<DateTime, double> dictionary)
        {
            var daily = dictionary.GroupBy(kvp => kvp.Key.Date)
                .ToDictionary(x => x.Key, v => v.LastOrDefault().Value)
                .Values.ToArray();

            var rocp = new double[daily.Length];
            for (var i = 1; i < daily.Length; i++)
            {
                rocp[i] = (daily[i] - daily[i - 1]) / daily[i - 1];
            }
            rocp[0] = 0;

            return rocp.ToList();
        }

        /// <summary>
        /// Gets the historical data of an indicator and convert it into pandas.DataFrame
        /// </summary>
        /// <param name="indicator">Indicator</param>
        /// <param name="history">Historical data used to calculate the indicator</param>
        /// <param name="selector">Selects a value from the BaseData to send into the indicator, if null defaults to the Value property of BaseData (x => x.Value)</param>
        /// <returns>pandas.DataFrame containing the historical data of <param name="indicator"></returns>
        private PyObject Indicator(IndicatorBase<IndicatorDataPoint> indicator, IEnumerable<IBaseData> history, Func<IBaseData, decimal> selector = null)
        {
            // Reset the indicator
            indicator.Reset();
            
            // Create a dictionary of the properties
            var name = indicator.GetType().Name;

            var properties = indicator.GetType().GetProperties()
                .Where(x => x.PropertyType.IsGenericType)
                .ToDictionary(x => x.Name, y => new List<IndicatorDataPoint>());
            properties.Add(name, new List<IndicatorDataPoint>());

            indicator.Updated += (s, e) =>
            {
                if (!indicator.IsReady)
                {
                    return;
                }

                foreach (var kvp in properties)
                {
                    var dataPoint = kvp.Key == name ? e : GetPropertyValue(s, kvp.Key + ".Current");
                    kvp.Value.Add((IndicatorDataPoint)dataPoint);
                }
            };

            selector = selector ?? (x => x.Value);

            foreach (var bar in history)
            {
                var value = selector(bar);
                indicator.Update(bar.EndTime, value);
            }

            return PandasConverter.GetIndicatorDataFrame(properties);
        }

        /// <summary>
        /// Gets the historical data of an bar indicator and convert it into pandas.DataFrame
        /// </summary>
        /// <param name="indicator">Bar indicator</param>
        /// <param name="history">Historical data used to calculate the indicator</param>
        /// <param name="selector">Selects a value from the BaseData to send into the indicator, if null defaults to the Value property of BaseData (x => x.Value)</param>
        /// <returns>pandas.DataFrame containing the historical data of <param name="indicator"></returns>
        private PyObject Indicator<T>(IndicatorBase<T> indicator, IEnumerable<T> history, Func<IBaseData, T> selector = null)
            where T : IBaseData
        {
            // Reset the indicator
            indicator.Reset();

            // Create a dictionary of the properties
            var name = indicator.GetType().Name;

            var properties = indicator.GetType().GetProperties()
                .Where(x => x.PropertyType.IsGenericType)
                .ToDictionary(x => x.Name, y => new List<IndicatorDataPoint>());
            properties.Add(name, new List<IndicatorDataPoint>());

            indicator.Updated += (s, e) =>
            {
                if (!indicator.IsReady)
                {
                    return;
                }

                foreach (var kvp in properties)
                {
                    var dataPoint = kvp.Key == name ? e : GetPropertyValue(s, kvp.Key + ".Current");
                    kvp.Value.Add((IndicatorDataPoint)dataPoint);
                }
            };
            
            selector = selector ?? (x => (T)x);
            
            foreach (var bar in history)
            {
                indicator.Update(selector(bar));
            }

            return PandasConverter.GetIndicatorDataFrame(properties);
        }
        
        /// <summary>
        /// Gets a value of a property
        /// </summary>
        /// <param name="baseData">Object with the desired property</param>
        /// <param name="fullName">Property name</param>
        /// <returns>Property value</returns>
        private object GetPropertyValue(object baseData, string fullName)
        {
            foreach (var name in fullName.Split('.'))
            {
                if (baseData == null) return null;

                var info = baseData.GetType().GetProperty(name);

                baseData = info?.GetValue(baseData, null);
            }

            return baseData;
        }
    }
}