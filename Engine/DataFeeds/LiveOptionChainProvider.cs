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
using System.Net;
using QuantConnect.Interfaces;

namespace QuantConnect.Lean.Engine.DataFeeds
{
    /// <summary>
    /// An implementation of <see cref="IOptionChainProvider"/> that fetches the list of contracts
    /// from the Options Clearing Corporation (OCC) website
    /// </summary>
    public class LiveOptionChainProvider : IOptionChainProvider
    {
        /// <summary>
        /// Gets the list of option contracts for a given underlying symbol
        /// </summary>
        /// <param name="symbol">The underlying symbol</param>
        /// <param name="date">The date for which to request the option chain (only used in backtesting)</param>
        /// <returns>The list of option contracts</returns>
        public IEnumerable<Symbol> GetOptionContractList(Symbol symbol, DateTime date)
        {
            if (symbol.SecurityType != SecurityType.Equity)
            {
                throw new NotSupportedException($"LiveOptionChainProvider.GetOptionContractList(): SecurityType.Equity is expected but was {symbol.SecurityType}");
            }

            return FindOptionContracts(symbol.Value);
        }

        /// <summary>
        /// Retrieve the list of option contracts for an underlying symbol from the OCC website
        /// </summary>
        private static IEnumerable<Symbol> FindOptionContracts(string underlyingSymbol)
        {
            var symbols = new List<Symbol>();

            using (var client = new WebClient())
            {
                // download the text file
                var url = "https://www.theocc.com/webapps/series-search?symbolType=U&symbol=" + underlyingSymbol;
                var fileContent = client.DownloadString(url);

                // read the lines, skipping the headers
                var lines = fileContent.Split(new[] { "\r\n" }, StringSplitOptions.None).Skip(7);

                // parse the lines, creating the Lean option symbols
                foreach (var line in lines)
                {
                    var fields = line.Split('\t');

                    var ticker = fields[0].Trim();
                    if (ticker != underlyingSymbol)
                        continue;

                    var expiryDate = new DateTime(fields[2].ToInt32(), fields[3].ToInt32(), fields[4].ToInt32());
                    var strike = (fields[5] + "." + fields[6]).ToDecimal();

                    if (fields[7].Contains("C"))
                    {
                        symbols.Add(Symbol.CreateOption(underlyingSymbol, Market.USA, OptionStyle.American, OptionRight.Call, strike, expiryDate));
                    }

                    if (fields[7].Contains("P"))
                    {
                        symbols.Add(Symbol.CreateOption(underlyingSymbol, Market.USA, OptionStyle.American, OptionRight.Put, strike, expiryDate));
                    }
                }
            }

            return symbols;
        }
    }
}
