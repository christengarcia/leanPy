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
 *
*/

using QuantConnect.Brokerages;
using QuantConnect.Interfaces;
using QuantConnect.Lean.Engine.DataFeeds.Transport;
using QuantConnect.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.Net;
using QuantConnect.Lean.Engine.DataFeeds;

namespace QuantConnect.ToolBox.IQFeed
{
    /// <summary>
    /// Class implements several interfaces to support IQFeed symbol mapping to LEAN and symbol lookup
    /// </summary>
    public class IQFeedDataQueueUniverseProvider : IDataQueueUniverseProvider, ISymbolMapper
    {
        // IQFeed CSV file column nomenclature
        private const int columnSymbol = 0;
        private const int columnDescription = 1;
        private const int columnExchange = 2;
        private const int columnListedMarket = 3;
        private const int columnSecurityType = 4;
        private const int columnSIC = 5;
        private const int columnFrontMonth = 6;
        private const int columnNAICS = 7;
        private const int totalColumns = 8;

        private const string NewLine = "\r\n";
        private const char Tabulation = '\t';

        private IDataCacheProvider _dataCacheProvider = new ZipDataCacheProvider(new DefaultDataProvider());

        // Database of all symbols
        // We store symbol data in memory by default (e.g. Equity, FX),
        // and we store root symbol placeholder (one per underlying) for those symbols that are loaded in memory on demand (e.g. Equity/Index options, Futures) 
        private List<SymbolData> _symbolUniverse = new List<SymbolData>();

        // tickets and symbols are isomorphic
        private Dictionary<Symbol, string> _symbols = new Dictionary<Symbol, string>();
        private Dictionary<string, Symbol> _tickers = new Dictionary<string, Symbol>();

        // we have a special treatment of futures, because IQFeed renamed exchange tickers and doesn't include 
        // futures expiration dates in the symbol universe file. We fix this: 
        // We map those tickers back to their original names using the map below
        private Dictionary<string, string> _iqFeedNameMap = new Dictionary<string, string>();

        // Map of IQFeed exchange names to QC markets
        // Prioritized list of exchanges used to find right futures contract 
        private readonly Dictionary<string, string> _futuresExchanges = new Dictionary<string, string>
        {
            { "CME", Market.Globex },
            { "NYMEX", Market.NYMEX },
            { "CBOT", Market.CBOT },
            { "ICEFU", Market.ICE },
            { "CFE", Market.CBOE  }
        };

        // futures fundamental data resolver
        private readonly SymbolFundamentalData _symbolFundamentalData;

        public IQFeedDataQueueUniverseProvider()
        {
            _symbolFundamentalData = new SymbolFundamentalData();
            _symbolFundamentalData.Connect();
            _symbolFundamentalData.SetClientName("SymbolFundamentalData");

            var symbols = LoadSymbols();
            UpdateCollections(symbols);
        }

        /// <summary>
        /// Converts a Lean symbol instance to IQFeed ticker
        /// </summary>
        /// <param name="symbol">A Lean symbol instance</param>
        /// <returns>IQFeed ticker</returns>
        public string GetBrokerageSymbol(Symbol symbol)
        {
            return _symbols.ContainsKey(symbol) ? _symbols[symbol] : string.Empty;
        }

        /// <summary>
        /// Converts IQFeed ticker to a Lean symbol instance
        /// </summary>
        /// <param name="ticker">IQFeed ticker</param>
        /// <param name="securityType">The security type</param>
        /// <param name="market">The market</param>
        /// <param name="expirationDate">Expiration date of the security(if applicable)</param>
        /// <param name="strike">The strike of the security (if applicable)</param>
        /// <param name="optionRight">The option right of the security (if applicable)</param>
        /// <returns>A new Lean Symbol instance</returns>
        public Symbol GetLeanSymbol(string ticker, SecurityType securityType, string market, DateTime expirationDate = default(DateTime), decimal strike = 0, OptionRight optionRight = 0)
        {
            return _tickers.ContainsKey(ticker) ? _tickers[ticker] : Symbol.Empty;
        }

        /// <summary>
        /// Method returns a collection of Symbols that are available at IQFeed. 
        /// </summary>
        /// <param name="lookupName">String representing the name to lookup</param>
        /// <param name="securityType">Expected security type of the returned symbols (if any)</param>
        /// <param name="securityCurrency">Expected security currency(if any)</param>
        /// <param name="securityExchange">Expected security exchange name(if any)</param>
        /// <returns></returns>
        public IEnumerable<Symbol> LookupSymbols(string lookupName, SecurityType securityType, string securityCurrency = null, string securityExchange = null)
        {
            Func<Symbol, string> lookupFunc;

            // for option, futures contract we search the underlying
            if (securityType == SecurityType.Option ||
                securityType == SecurityType.Future)
            {
                lookupFunc = symbol => symbol.HasUnderlying ? symbol.Underlying.Value : string.Empty;
            }
            else
            {
                lookupFunc = symbol => symbol.Value;
            }

            var result = _symbolUniverse.Where(x => lookupFunc(x.Symbol) == lookupName &&
                                            x.Symbol.ID.SecurityType == securityType && 
                                            (securityCurrency == null || x.SecurityCurrency == securityCurrency) && 
                                            (securityExchange == null || x.SecurityExchange == securityExchange))
                                         .ToList();

            bool onDemandRequests = result.All(symbolData => !symbolData.IsDataLoaded());

            if (onDemandRequests)
            {
                var exchanges = securityType == SecurityType.Future ? 
                                    _futuresExchanges.Values.Reverse().ToArray() : 
                                    new string[] { };

                // sorting list of available contracts by exchange priority, taking the top 1
                var symbolData = 
                    result
                    .OrderByDescending(e => Array.IndexOf(exchanges, e))
                    .First();

                // we check if the result contains the data that needs to be loaded on demand (e.g. options, futures)
                var loadedData = LoadSymbolOnDemand(symbolData);

                // Replace placeholder item in _symbolUniverse with the data loaded on demand
                UpdateCollectionsOnDemand(symbolData, loadedData);
                
                // if we found some data that was loaded on demand, then we have to re-run the query to include that data into method output

                result = _symbolUniverse.Where(x => lookupFunc(x.Symbol) == lookupName &&
                                            x.Symbol.ID.SecurityType == securityType &&
                                            (securityCurrency == null || x.SecurityCurrency == securityCurrency) &&
                                            (securityExchange == null || x.SecurityExchange == securityExchange))
                                        .ToList();
            }

            return result.Select(x => x.Symbol);
        }

        /// <summary>
        /// Private method updates internal collections of the class with new data loaded on demand (usually options, futures)
        /// </summary>
        /// <param name="placeholderSymbolData">Old data that contained reference to the symbol cache file</param>
        /// <param name="loadedData">New loaded data</param>
        private void UpdateCollectionsOnDemand(SymbolData placeholderSymbolData, IEnumerable<SymbolData> loadedData)
        {
            // Replace placeholder item in _symbolUniverse with the data loaded on demand
            _symbolUniverse.Remove(placeholderSymbolData);

            UpdateCollections(loadedData);
        }

        /// <summary>
        /// Private method updates internal collections of the class with data loaded from the universe
        /// </summary>
        /// <param name="loadedData">New loaded data</param>
        private void UpdateCollections(IEnumerable<SymbolData> loadedData)
        {
            _symbolUniverse.AddRange(loadedData);

            var cleanData = loadedData.Where(kv => kv.IsDataLoaded()).ToList();

            foreach (var symbolData in cleanData)
            {
                if (!_symbols.ContainsKey(symbolData.Symbol))
                    _symbols.Add(symbolData.Symbol, symbolData.Ticker);

                if (!_tickers.ContainsKey(symbolData.Ticker))
                    _tickers.Add(symbolData.Ticker, symbolData.Symbol);
            }
        }

        /// <summary>
        /// Private method performs initial loading of data from IQFeed universe: 
        /// - method loads FX,equities, indices as is into memory
        /// - method prepares ondemand data for options and futures and stores it to disk
        /// - method updates futures mapping files if required
        /// </summary>
        /// <returns></returns>
        private IEnumerable<SymbolData> LoadSymbols()
        {
            // default URI
            const string uri = "http://www.dtniq.com/product/mktsymbols_v2.zip";

            if (!Directory.Exists(Globals.Cache)) Directory.CreateDirectory(Globals.Cache);

            // we try to check if we already downloaded the file and it is in cache. If yes, we use it. Otherwise, download new file. 
            IStreamReader reader;

            // we update the files every week
            var dayOfWeek = DateTimeFormatInfo.CurrentInfo.Calendar.GetWeekOfYear(DateTime.Today, CalendarWeekRule.FirstDay, DayOfWeek.Monday);
            var thisYearWeek = DateTime.Today.ToString("yyyy") + "-" + dayOfWeek.ToString();

            var todayZipFileName = "IQFeed-symbol-universe-" + thisYearWeek + ".zip";
            var todayFullZipName = Path.Combine(Globals.Cache, todayZipFileName);

            var todayCsvFileName = "mktsymbols_v2.txt";
            var todayFullCsvName = Path.Combine(Globals.Cache, todayCsvFileName);

            var iqfeedNameMapFileName = "IQFeed-symbol-map.json";
            var iqfeedNameMapFullName = Path.Combine("IQFeed", iqfeedNameMapFileName);

            var mapExists = File.Exists(iqfeedNameMapFullName);
            var universeExists = File.Exists(todayFullZipName);

            if (mapExists)
            {
                Log.Trace("Loading IQFeed futures symbol map file...");
                _iqFeedNameMap = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(iqfeedNameMapFullName));
            }

            if (!universeExists)
            {
                Log.Trace("Loading and unzipping IQFeed symbol universe file ({0})...", uri);

                using (var client = new WebClient())
                {
                    client.Proxy = WebRequest.GetSystemWebProxy();
                    client.DownloadFile(uri, todayFullZipName);
                }

                Compression.Unzip(todayFullZipName, Globals.Cache, true);
            }
            else
            {
                Log.Trace("Found up-to-date IQFeed symbol universe file in local cache. Loading it...");
            }

            var symbolCache = new Dictionary<Symbol, SymbolData>();
            var symbolUniverse = new List<SymbolData>();

            long currentPosition = 0;
            long prevPosition = 0;

            reader = new LocalFileSubscriptionStreamReader(_dataCacheProvider, todayFullCsvName);

            while (!reader.EndOfStream)
            {
                prevPosition = currentPosition;

                var line = reader.ReadLine();

                currentPosition += line.Length + NewLine.Length; // file position 'estimator' for ASCII file of IQFeed universe

                var columns = line.Split(Tabulation);

                if (columns.Length != totalColumns)
                {
                    Log.Trace("Discrepancy found while parsing IQFeed symbol universe file. Expected 8 columns, but arrived {0}. Line: {1}", columns.Length, line);
                    continue;
                }

                switch (columns[columnSecurityType])
                {
                    case "INDEX":
                    case "EQUITY":

                        // we load equities/indices in memory
                        symbolUniverse.Add(new SymbolData
                        {
                            Symbol = Symbol.Create(columns[columnSymbol], SecurityType.Equity, Market.USA),
                            SecurityCurrency = "USD",
                            SecurityExchange = Market.USA,
                            Ticker = columns[columnSymbol]
                        });
                        break;

                    case "IEOPTION":

                        var ticker = columns[columnSymbol];
                        var result = SymbolRepresentation.ParseOptionTickerIQFeed(ticker);
                        var optionUnderlying = result.Underlying;
                        var canonicalSymbol = Symbol.Create(optionUnderlying, SecurityType.Option, Market.USA);

                        if (!symbolCache.ContainsKey(canonicalSymbol))
                        {
                            var placeholderSymbolData = new SymbolData
                            {
                                Symbol = canonicalSymbol,
                                SecurityCurrency = "USD",
                                SecurityExchange = Market.USA,
                                StartPosition = prevPosition,
                                EndPosition = currentPosition
                            };

                            symbolCache.Add(canonicalSymbol, placeholderSymbolData);
                        }
                        else
                        {
                            symbolCache[canonicalSymbol].EndPosition = currentPosition;
                        }

                        break;

                    case "FOREX":

                        // we use FXCM symbols only
                        if (columns[columnSymbol].EndsWith(".FXCM"))
                        {
                            var symbol = columns[columnSymbol].Replace(".FXCM", string.Empty);

                            symbolUniverse.Add(new SymbolData
                            {
                                Symbol = Symbol.Create(symbol, SecurityType.Forex, Market.FXCM),
                                SecurityCurrency = "USD",
                                SecurityExchange = Market.FXCM,
                                Ticker = columns[columnSymbol]
                            });
                        }
                        break;

                    case "FUTURE":

                        // we are not interested in designated continuous contracts 
                        if (columns[columnSymbol].EndsWith("#"))
                            continue;

                        var futuresTicker = columns[columnSymbol].TrimStart(new [] { '@' });

                        var parsed = SymbolRepresentation.ParseFutureTicker(futuresTicker);
                        var underlyingString = parsed.Underlying;

                        if (_iqFeedNameMap.ContainsKey(underlyingString))
                            underlyingString = _iqFeedNameMap[underlyingString];
                        else
                        {
                            if (!mapExists)
                            {
                                if (!_iqFeedNameMap.ContainsKey(underlyingString))
                                {
                                    // if map is not created yet, we request this information from IQFeed
                                    var exchangeSymbol = _symbolFundamentalData.Request(columns[columnSymbol]).Item2;
                                    if (!string.IsNullOrEmpty(exchangeSymbol))
                                    {
                                        _iqFeedNameMap[underlyingString] = exchangeSymbol;
                                        underlyingString = exchangeSymbol;
                                    }
                                }
                            }
                        }

                        var market = _futuresExchanges.ContainsKey(columns[columnExchange]) ? _futuresExchanges[columns[columnExchange]] : Market.USA;
                        canonicalSymbol = Symbol.Create(underlyingString, SecurityType.Future, market);

                        if (!symbolCache.ContainsKey(canonicalSymbol))
                        {
                            var placeholderSymbolData = new SymbolData
                            {
                                Symbol = canonicalSymbol,
                                SecurityCurrency = "USD",
                                SecurityExchange = market,
                                StartPosition = prevPosition,
                                EndPosition = currentPosition
                            };

                            symbolCache.Add(canonicalSymbol, placeholderSymbolData);
                        }
                        else
                        {
                            symbolCache[canonicalSymbol].EndPosition = currentPosition;
                        }

                        break;

                    default:

                        continue;
                }
            }

            if (!mapExists)
            {
                Log.Trace("Saving IQFeed futures symbol map file...");
                File.WriteAllText(iqfeedNameMapFullName, JsonConvert.SerializeObject(_iqFeedNameMap));
            }

            symbolUniverse.AddRange(symbolCache.Values);

            Log.Trace("Finished loading IQFeed symbol universe file.");

            return symbolUniverse;
        }


        /// <summary>
        /// Private method loads all option or future contracts for a particular underlying 
        /// symbol (placeholder) on demand by walking through the IQFeed universe file
        /// </summary>
        /// <param name="placeholder">Underlying symbol</param>
        /// <returns></returns>
        private IEnumerable<SymbolData> LoadSymbolOnDemand(SymbolData placeholder)
        {
            var dayOfWeek = DateTimeFormatInfo.CurrentInfo.Calendar.GetWeekOfYear(DateTime.Today, CalendarWeekRule.FirstDay, DayOfWeek.Monday);
            var thisYearWeek = DateTime.Today.ToString("yyyy") + "-" + dayOfWeek.ToString();

            var todayCsvFileName = "mktsymbols_v2.txt";
            var todayFullCsvName = Path.Combine(Globals.Cache, todayCsvFileName);

            var reader = new LocalFileSubscriptionStreamReader(_dataCacheProvider, todayFullCsvName, placeholder.StartPosition);

            Log.Trace("Loading data on demand for {0}...", placeholder.Symbol.Underlying.Value);

            var symbolUniverse = new List<SymbolData>();

            long currentPosition = placeholder.StartPosition;

            while (!reader.EndOfStream && currentPosition <= placeholder.EndPosition)
            {
                var line = reader.ReadLine();

                currentPosition += line.Length + NewLine.Length;

                var columns = line.Split(Tabulation);

                if (columns.Length != totalColumns)
                {
                    continue;
                }

                switch (columns[columnSecurityType])
                {
                    case "IEOPTION":

                        var ticker = columns[columnSymbol];
                        var result = SymbolRepresentation.ParseOptionTickerIQFeed(ticker);

                        symbolUniverse.Add(new SymbolData
                        {
                            Symbol = Symbol.CreateOption(result.Underlying,
                                                        Market.USA,
                                                        OptionStyle.American,
                                                        result.OptionRight,
                                                        result.OptionStrike,
                                                        result.ExpirationDate),
                            SecurityCurrency = "USD",
                            SecurityExchange = Market.USA,
                            Ticker = columns[columnSymbol]
                        });

                        break;

                    case "FUTURE":

                        if (columns[columnSymbol].EndsWith("#"))
                        {
                            continue;
                        }

                        var futuresTicker = columns[columnSymbol].TrimStart(new[] { '@' });

                        var parsed = SymbolRepresentation.ParseFutureTicker(futuresTicker);
                        var underlyingString = parsed.Underlying;
                        var market = Market.USA;

                        if (_iqFeedNameMap.ContainsKey(underlyingString))
                            underlyingString = _iqFeedNameMap[underlyingString];

                        if (underlyingString != placeholder.Symbol.Underlying.Value)
                        {
                            continue;
                        }

                        // Futures contracts have different idiosyncratic expiration dates that IQFeed symbol universe file doesn't contain
                        // We request IQFeed explicitly for the exact expiration data of each contract

                        var expirationDate = _symbolFundamentalData.Request(columns[columnSymbol]).Item1; 

                        if (expirationDate == DateTime.MinValue)
                        {
                            // contract is outdated
                            continue;
                        }

                        symbolUniverse.Add(new SymbolData
                        {
                            Symbol = Symbol.CreateFuture(underlyingString,
                                                        market,
                                                        expirationDate),
                            SecurityCurrency = "USD",
                            SecurityExchange = market,
                            Ticker = columns[columnSymbol]
                        });

                        break;

                    default:

                        continue;
                }
            }

            return symbolUniverse;
        }


        // Class stores symbol data in memory if symbol is loaded in memory by default (e.g. Equity, FX),
        // and stores quick access parameters for those symbols that are loaded in memory on demand (e.g. Equity/Index options, Futures) 
        class SymbolData
        {
            public string Ticker { get; set; }

            public string SecurityCurrency { get; set; }

            public string SecurityExchange { get; set; }

            public Symbol Symbol { get; set; }

            public long StartPosition { get; set; }

            public long EndPosition { get; set; }

            /// <summary>
            /// Method returns true if the object contains all the needed data. Otherwise, false
            /// </summary>
            /// <returns></returns>
            public bool IsDataLoaded()
            {
                return !string.IsNullOrEmpty(Ticker);
            }

            protected bool Equals(SymbolData other)
            {
                return string.Equals(Ticker, other.Ticker) &&
                    string.Equals(SecurityCurrency, other.SecurityCurrency) &&
                    string.Equals(SecurityExchange, other.SecurityExchange) &&
                    Equals(Symbol, other.Symbol) &&
                    Equals(StartPosition, other.StartPosition) &&
                    Equals(EndPosition, other.EndPosition);
            }

            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj)) return false;
                if (ReferenceEquals(this, obj)) return true;
                if (obj.GetType() != this.GetType()) return false;
                return Equals((SymbolData)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hashCode = (Ticker != null ? Ticker.GetHashCode() : 0);
                    hashCode = (hashCode * 397) ^ (SecurityCurrency != null ? SecurityCurrency.GetHashCode() : 0);
                    hashCode = (hashCode * 397) ^ (SecurityExchange != null ? SecurityExchange.GetHashCode() : 0);
                    hashCode = (hashCode * 397) ^ (Symbol != null ? Symbol.GetHashCode() : 0);
                    hashCode = (hashCode * 397) ^ StartPosition.GetHashCode();
                    hashCode = (hashCode * 397) ^ EndPosition.GetHashCode();
                    return hashCode;
                }
            }
        }

        /// <summary>
        /// Private class that helps requesting IQFeed fundamental data 
        /// </summary>
        public class SymbolFundamentalData : IQLevel1Client
        {
            public SymbolFundamentalData(): base(80)
            {
            }

            /// <summary>
            /// Method returns two fields of the fundamental data that we need: expiration date (tuple field 1),
            /// and exchange root symbol (tuple field 2)
            /// </summary>
            public Tuple<DateTime, string> Request(string ticker)
            {
                const int timeout = 180; // sec
                var manualResetEvent = new ManualResetEvent(false);

                var expiry = DateTime.MinValue;
                var rootSymbol = string.Empty;

                EventHandler<Level1FundamentalEventArgs> dataEventHandler = (sender, e) =>
                {
                    if (e.Symbol == ticker)
                    {
                        expiry = e.ExpirationDate;
                        rootSymbol = e.ExchangeRoot;

                        manualResetEvent.Set();
                    }
                };
                EventHandler<Level1SummaryUpdateEventArgs> noDataEventHandler = (sender, e) =>
                {
                    if (e.Symbol == ticker && e.NotFound)
                    {
                        manualResetEvent.Set();
                    }
                };

                Level1FundamentalEvent += dataEventHandler;
                Level1SummaryUpdateEvent += noDataEventHandler;

                Subscribe(ticker);

                if (!manualResetEvent.WaitOne(timeout * 1000))
                {
                    Log.Error("SymbolFundamentalData.Request() failed to receive response from IQFeed within {0} seconds", timeout);
                }

                Unsubscribe(ticker);

                Level1SummaryUpdateEvent -= noDataEventHandler;

                Level1FundamentalEvent -= dataEventHandler;

                return Tuple.Create(expiry, rootSymbol);
            }
        }
        
    }
}
