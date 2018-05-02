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
using System.Globalization;
using QuantConnect.Logging;
using QuantConnect.Util;

namespace QuantConnect.Data.Market
{
    /// <summary>
    /// QuoteBar class for second and minute resolution data: 
    /// An OHLC implementation of the QuantConnect BaseData class with parameters for candles.
    /// </summary>
    public class QuoteBar : BaseData, IBaseDataBar
    {
        // scale factor used in QC equity/forex data files
        private const decimal _scaleFactor = 1 / 10000m;

        /// <summary>
        /// Average bid size
        /// </summary>
        public decimal LastBidSize { get; set; }
        
        /// <summary>
        /// Average ask size
        /// </summary>
        public decimal LastAskSize { get; set; }

        /// <summary>
        /// Bid OHLC
        /// </summary>
        public Bar Bid { get; set; }

        /// <summary>
        /// Ask OHLC
        /// </summary>
        public Bar Ask { get; set; }

        /// <summary>
        /// Opening price of the bar: Defined as the price at the start of the time period.
        /// </summary>
        public decimal Open
        {
            get
            {
                if (Bid != null && Ask != null)
                {
                    if (Bid.Open != 0m && Ask.Open != 0m)
                        return (Bid.Open + Ask.Open) / 2m;

                    if (Bid.Open != 0)
                        return Bid.Open;

                    if (Ask.Open != 0)
                        return Ask.Open;

                    return 0m;
                }
                if (Bid != null)
                {
                    return Bid.Open;
                }
                if (Ask != null)
                {
                    return Ask.Open;
                }
                return 0m;
            }
        }

        /// <summary>
        /// High price of the QuoteBar during the time period.
        /// </summary>
        public decimal High
        {
            get
            {
                if (Bid != null && Ask != null)
                {
                    if (Bid.High != 0m && Ask.High != 0m)
                        return (Bid.High + Ask.High) / 2m;

                    if (Bid.High != 0)
                        return Bid.High;

                    if (Ask.High != 0)
                        return Ask.High;

                    return 0m;
                }
                if (Bid != null)
                {
                    return Bid.High;
                }
                if (Ask != null)
                {
                    return Ask.High;
                }
                return 0m;
            }
        }

        /// <summary>
        /// Low price of the QuoteBar during the time period.
        /// </summary>
        public decimal Low
        {
            get
            {
                if (Bid != null && Ask != null)
                {
                    if (Bid.Low != 0m && Ask.Low != 0m)
                        return (Bid.Low + Ask.Low) / 2m;

                    if (Bid.Low != 0)
                        return Bid.Low;

                    if (Ask.Low != 0)
                        return Ask.Low;

                    return 0m;
                }
                if (Bid != null)
                {
                    return Bid.Low;
                }
                if (Ask != null)
                {
                    return Ask.Low;
                }
                return 0m;
            }
        }

        /// <summary>
        /// Closing price of the QuoteBar. Defined as the price at Start Time + TimeSpan.
        /// </summary>
        public decimal Close
        {
            get
            {
                if (Bid != null && Ask != null)
                {
                    if (Bid.Close != 0m && Ask.Close != 0m)
                        return (Bid.Close + Ask.Close) / 2m;

                    if (Bid.Close != 0)
                        return Bid.Close;

                    if (Ask.Close != 0)
                        return Ask.Close;

                    return 0m;
                }
                if (Bid != null)
                {
                    return Bid.Close;
                }
                if (Ask != null)
                {
                    return Ask.Close;
                }
                return Value;
            }
        }

        /// <summary>
        /// The closing time of this bar, computed via the Time and Period
        /// </summary>
        public override DateTime EndTime
        {
            get { return Time + Period; }
            set { Period = value - Time; }
        }

        /// <summary>
        /// The period of this quote bar, (second, minute, daily, ect...)
        /// </summary>
        public TimeSpan Period { get; set; }

        /// <summary>
        /// Default initializer to setup an empty quotebar.
        /// </summary>
        public QuoteBar()
        {
            Symbol = Symbol.Empty;
            Time = new DateTime();
            Bid = new Bar();
            Ask = new Bar();
            Value = 0;
            Period = TimeSpan.FromMinutes(1);
            DataType = MarketDataType.QuoteBar;
        }

        /// <summary>
        /// Initialize Quote Bar with Bid(OHLC) and Ask(OHLC) Values:
        /// </summary>
        /// <param name="time">DateTime Timestamp of the bar</param>
        /// <param name="symbol">Market MarketType Symbol</param>
        /// <param name="bid">Bid OLHC bar</param>
        /// <param name="lastBidSize">Average bid size over period</param>
        /// <param name="ask">Ask OLHC bar</param>
        /// <param name="lastAskSize">Average ask size over period</param>
        /// <param name="period">The period of this bar, specify null for default of 1 minute</param>
        public QuoteBar(DateTime time, Symbol symbol, IBar bid, decimal lastBidSize, IBar ask, decimal lastAskSize, TimeSpan? period = null)
        {
            Symbol = symbol;
            Time = time;
            Bid = bid == null ? null : new Bar(bid.Open, bid.High, bid.Low, bid.Close);
            Ask = ask == null ? null : new Bar(ask.Open, ask.High, ask.Low, ask.Close);
            if (Bid != null) LastBidSize = lastBidSize;
            if (Ask != null) LastAskSize = lastAskSize;
            Value = Close;
            Period = period ?? TimeSpan.FromMinutes(1);
            DataType = MarketDataType.QuoteBar;
        }

        /// <summary>
        /// Update the quotebar - build the bar from this pricing information:
        /// </summary>
        /// <param name="lastTrade">The last trade price</param>
        /// <param name="bidPrice">Current bid price</param>
        /// <param name="askPrice">Current asking price</param>
        /// <param name="volume">Volume of this trade</param>
        /// <param name="bidSize">The size of the current bid, if available, if not, pass 0</param>
        /// <param name="askSize">The size of the current ask, if available, if not, pass 0</param>
        public override void Update(decimal lastTrade, decimal bidPrice, decimal askPrice, decimal volume, decimal bidSize, decimal askSize)
        {
            // update our bid and ask bars - handle null values, this is to give good values for midpoint OHLC
            if (Bid == null && bidPrice != 0) Bid = new Bar();
            if (Bid != null) Bid.Update(bidPrice);

            if (Ask == null && askPrice != 0) Ask = new Bar();
            if (Ask != null) Ask.Update(askPrice);

            if (bidSize > 0) 
            {
                LastBidSize = bidSize;
            }
            
            if (askSize > 0)
            {
                LastAskSize = askSize;
            }

            // be prepared for updates without trades
            if (lastTrade != 0) Value = lastTrade;
            else if (askPrice != 0) Value = askPrice;
            else if (bidPrice != 0) Value = bidPrice;
        }

        /// <summary>
        /// QuoteBar Reader: Fetch the data from the QC storage and feed it line by line into the engine.
        /// </summary>
        /// <param name="config">Symbols, Resolution, DataType, </param>
        /// <param name="line">Line from the data file requested</param>
        /// <param name="date">Date of this reader request</param>
        /// <param name="isLiveMode">true if we're in live mode, false for backtesting mode</param>
        /// <returns>Enumerable iterator for returning each line of the required data.</returns>
        public override BaseData Reader(SubscriptionDataConfig config, string line, DateTime date, bool isLiveMode)
        {
            var csvLength = line.Split(',').Length;

            try
            {
                // "Scaffold" code - simple check to see how the data is formatted and decide how to parse appropriately
                // TODO: Once all FX is reprocessed to QuoteBars, remove this check
                if (csvLength > 5)
                {
                    switch (config.SecurityType)
                    {
                        case SecurityType.Equity:
                            return ParseEquity(config, line, date);

                        case SecurityType.Forex:
                        case SecurityType.Crypto:
                            return ParseForex(config, line, date);

                        case SecurityType.Cfd:
                            return ParseCfd(config, line, date);

                        case SecurityType.Option:
                            return ParseOption(config, line, date);

                        case SecurityType.Future:
                            return ParseFuture(config, line, date);
                        
                    }
                }

                // Parse as trade
                return ParseTradeAsQuoteBar(config, date, line);
            }
            catch (Exception err)
            {
                Log.Error("QuoteBar.Reader(): Error parsing line: '{0}', Symbol: {1}, SecurityType: {2}, Resolution: {3}, Date: {4}, Message: {5}", 
                    line, config.Symbol.Value, config.SecurityType, config.Resolution, date.ToString("yyyy-MM-dd"), err);
            }

            // if we couldn't parse it above return a default instance
            return new QuoteBar { Symbol = config.Symbol, Period = config.Increment };
        }

        private static bool HasShownWarning;

        /// <summary>
        /// "Scaffold" code - If the data being read is formatted as a TradeBar, use this method to deserialize it
        /// TODO: Once all Forex data refactored to use QuoteBar formatted data, remove this method
        /// </summary>
        /// <param name="config">Symbols, Resolution, DataType, </param>
        /// <param name="line">Line from the data file requested</param>
        /// <param name="date">Date of this reader request</param>
        /// <returns><see cref="QuoteBar"/> with the bid/ask prices set to same values</returns>
        [Obsolete("All Forex data should use Quotes instead of Trades.")]
        private QuoteBar ParseTradeAsQuoteBar(SubscriptionDataConfig config, DateTime date, string line)
        {
            if (!HasShownWarning)
            {
                Logging.Log.Error("QuoteBar.ParseTradeAsQuoteBar(): Data formatted as Trade when Quote format was expected.  Support for this will disappear June 2017.");
                HasShownWarning = true;
            }

            var quoteBar = new QuoteBar
            {
                Period = config.Increment,
                Symbol = config.Symbol
            };

            var csv = line.ToCsv(5);
            if (config.Resolution == Resolution.Daily || config.Resolution == Resolution.Hour)
            {
                // hourly and daily have different time format, and can use slow, robust c# parser.
                quoteBar.Time = DateTime.ParseExact(csv[0], DateFormat.TwelveCharacter, CultureInfo.InvariantCulture).ConvertTo(config.DataTimeZone, config.ExchangeTimeZone);
            }
            else
            {
                //Fast decimal conversion
                quoteBar.Time = date.Date.AddMilliseconds(csv[0].ToInt32()).ConvertTo(config.DataTimeZone, config.ExchangeTimeZone);
            }

            var bid = new Bar
            {
                Open = csv[1].ToDecimal(),
                High = csv[2].ToDecimal(),
                Low = csv[3].ToDecimal(),
                Close = csv[4].ToDecimal()
            };

            var ask = new Bar
            {
                Open = csv[1].ToDecimal(),
                High = csv[2].ToDecimal(),
                Low = csv[3].ToDecimal(),
                Close = csv[4].ToDecimal()
            };

            quoteBar.Ask = ask;
            quoteBar.Bid = bid;
            quoteBar.Value = quoteBar.Close;

            return quoteBar;
        }

        /// <summary>
        /// Parse a quotebar representing a future with a scaling factor
        /// </summary>
        /// <param name="config">Symbols, Resolution, DataType</param>
        /// <param name="line">Line from the data file requested</param>
        /// <param name="date">Date of this reader request</param>
        /// <returns><see cref="QuoteBar"/> with the bid/ask set to same values</returns>
        public QuoteBar ParseFuture(SubscriptionDataConfig config, string line, DateTime date)
        {
            return ParseQuote(config, date, line, false);
        }

        /// <summary>
        /// Parse a quotebar representing an option with a scaling factor
        /// </summary>
        /// <param name="config">Symbols, Resolution, DataType</param>
        /// <param name="line">Line from the data file requested</param>
        /// <param name="date">Date of this reader request</param>
        /// <returns><see cref="QuoteBar"/> with the bid/ask set to same values</returns>
        public QuoteBar ParseOption(SubscriptionDataConfig config, string line, DateTime date)
        {
            return ParseQuote(config, date, line, true);
        }

        /// <summary>
        /// Parse a quotebar representing a cfd without a scaling factor
        /// </summary>
        /// <param name="config">Symbols, Resolution, DataType</param>
        /// <param name="line">Line from the data file requested</param>
        /// <param name="date">Date of this reader request</param>
        /// <returns><see cref="QuoteBar"/> with the bid/ask set to same values</returns>
        public QuoteBar ParseCfd(SubscriptionDataConfig config, string line, DateTime date)
        {
            return ParseQuote(config, date, line, false);
        }

        /// <summary>
        /// Parse a quotebar representing a forex without a scaling factor
        /// </summary>
        /// <param name="config">Symbols, Resolution, DataType</param>
        /// <param name="line">Line from the data file requested</param>
        /// <param name="date">Date of this reader request</param>
        /// <returns><see cref="QuoteBar"/> with the bid/ask set to same values</returns>
        public QuoteBar ParseForex(SubscriptionDataConfig config, string line, DateTime date)
        {
            return ParseQuote(config, date, line, false);
        }

        /// <summary>
        /// Parse a quotebar representing an equity with a scaling factor
        /// </summary>
        /// <param name="config">Symbols, Resolution, DataType</param>
        /// <param name="line">Line from the data file requested</param>
        /// <param name="date">Date of this reader request</param>
        /// <returns><see cref="QuoteBar"/> with the bid/ask set to same values</returns>
        public QuoteBar ParseEquity(SubscriptionDataConfig config, string line, DateTime date)
        {
            return ParseQuote(config, date, line, true);
        }


        /// <summary>
        /// "Scaffold" code - If the data being read is formatted as a QuoteBar, use this method to deserialize it
        /// TODO: Once all Forex data refactored to use QuoteBar formatted data, use only this method
        /// </summary>
        /// <param name="config">Symbols, Resolution, DataType, </param>
        /// <param name="line">Line from the data file requested</param>
        /// <param name="date">Date of this reader request</param>
        /// <param name="useScaleFactor">Whether the data has a scaling factor applied</param>
        /// <returns><see cref="QuoteBar"/> with the bid/ask prices set appropriately</returns>
        private QuoteBar ParseQuote(SubscriptionDataConfig config, DateTime date, string line, bool useScaleFactor)
        {
            var scaleFactor = useScaleFactor
                              ? _scaleFactor 
                              : 1;

            var quoteBar = new QuoteBar
            {
                Period = config.Increment,
                Symbol = config.Symbol
            };

            var csv = line.ToCsv(10);
            if (config.Resolution == Resolution.Daily || config.Resolution == Resolution.Hour)
            {
                // hourly and daily have different time format, and can use slow, robust c# parser.
                quoteBar.Time = DateTime.ParseExact(csv[0], DateFormat.TwelveCharacter, CultureInfo.InvariantCulture).ConvertTo(config.DataTimeZone, config.ExchangeTimeZone);
            }
            else
            {
                // Using custom "ToDecimal" conversion for speed on high resolution data.
                quoteBar.Time = date.Date.AddMilliseconds(csv[0].ToInt32()).ConvertTo(config.DataTimeZone, config.ExchangeTimeZone);
            }

            // only create the bid if it exists in the file
            if (csv[1].Length != 0 || csv[2].Length != 0 || csv[3].Length != 0 || csv[4].Length != 0)
            {
                quoteBar.Bid = new Bar
                {
                    Open = config.GetNormalizedPrice(csv[1].ToDecimal() * scaleFactor),
                    High = config.GetNormalizedPrice(csv[2].ToDecimal() * scaleFactor),
                    Low = config.GetNormalizedPrice(csv[3].ToDecimal() * scaleFactor),
                    Close = config.GetNormalizedPrice(csv[4].ToDecimal() * scaleFactor)
                };
                quoteBar.LastBidSize = csv[5].ToDecimal();
            }
            else
            {
                quoteBar.Bid = null;
            }

            // only create the ask if it exists in the file
            if (csv[6].Length != 0 || csv[7].Length != 0 || csv[8].Length != 0 || csv[9].Length != 0)
            {
                quoteBar.Ask = new Bar
                {
                    Open = config.GetNormalizedPrice(csv[6].ToDecimal() * scaleFactor),
                    High = config.GetNormalizedPrice(csv[7].ToDecimal() * scaleFactor),
                    Low = config.GetNormalizedPrice(csv[8].ToDecimal() * scaleFactor),
                    Close = config.GetNormalizedPrice(csv[9].ToDecimal() * scaleFactor)
                };
                quoteBar.LastAskSize = csv[10].ToDecimal();
            }
            else
            {
                quoteBar.Ask = null;
            }

            quoteBar.Value = quoteBar.Close;

            return quoteBar;
        }

        /// <summary>
        /// Get Source for Custom Data File
        /// >> What source file location would you prefer for each type of usage:
        /// </summary>
        /// <param name="config">Configuration object</param>
        /// <param name="date">Date of this source request if source spread across multiple files</param>
        /// <param name="isLiveMode">true if we're in live mode, false for backtesting mode</param>
        /// <returns>String source location of the file</returns>
        public override SubscriptionDataSource GetSource(SubscriptionDataConfig config, DateTime date, bool isLiveMode)
        {
            if (isLiveMode)
            {
                return new SubscriptionDataSource(string.Empty, SubscriptionTransportMedium.LocalFile);
            }

            var source = LeanData.GenerateZipFilePath(Globals.DataFolder, config.Symbol, date, config.Resolution, config.TickType);
            if (config.SecurityType == SecurityType.Option ||
                config.SecurityType == SecurityType.Future)
            {
                source += "#" + LeanData.GenerateZipEntryName(config.Symbol, date, config.Resolution, config.TickType);
            }
            return new SubscriptionDataSource(source, SubscriptionTransportMedium.LocalFile, FileFormat.Csv);
        }

        /// <summary>
        /// Return a new instance clone of this quote bar, used in fill forward
        /// </summary>
        /// <returns>A clone of the current quote bar</returns>
        public override BaseData Clone()
        {
            return new QuoteBar
            {
                Ask = Ask == null ? null : Ask.Clone(),
                Bid = Bid == null ? null : Bid.Clone(),
                LastAskSize = LastAskSize,
                LastBidSize = LastBidSize,
                Symbol = Symbol,
                Time = Time,
                Period = Period,
                Value = Value,
                DataType = DataType
            };
        }


        /// <summary>
        /// Collapses QuoteBars into TradeBars object when
        ///  algorithm requires FX data, but calls OnData(<see cref="TradeBars"/>)
        /// TODO: (2017) Remove this method in favor of using OnData(<see cref="Slice"/>)
        /// </summary>
        /// <returns><see cref="TradeBars"/></returns>
        [Obsolete("For backwards compatibility only.  When FX data is traded, all algorithms should use OnData(Slice)")]
        public TradeBar Collapse()
        {
            return new TradeBar(EndTime, Symbol, Open, High, Low, Close, 0);
        }
    }
}
