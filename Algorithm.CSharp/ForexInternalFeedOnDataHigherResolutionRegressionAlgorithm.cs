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
using QuantConnect.Data;

namespace QuantConnect.Algorithm.CSharp
{
    /// <summary>
    /// This algorithm is a test case for adding forex symbols at a higher resolution of an existing internal feed.
    /// The second symbol is added in the OnData method.
    /// </summary>
    public class ForexInternalFeedOnDataHigherResolutionRegressionAlgorithm : QCAlgorithm
    {
        private readonly Dictionary<Symbol, int> _dataPointsPerSymbol = new Dictionary<Symbol, int>();
        private bool _added;

        /// <summary>
        /// Initialise the data and resolution required, as well as the cash and start-end dates for your algorithm. All algorithms must initialized.
        /// </summary>
        public override void Initialize()
        {
            SetStartDate(2013, 10, 7);
            SetEndDate(2013, 10, 8);
            SetCash(100000);

            var eurgbp = AddForex("EURGBP", Resolution.Daily);
            _dataPointsPerSymbol.Add(eurgbp.Symbol, 0);
        }

        /// <summary>
        /// OnData event is the primary entry point for your algorithm. Each new data point will be pumped in here.
        /// </summary>
        /// <param name="data">Slice object keyed by symbol containing the stock data</param>
        public override void OnData(Slice data)
        {
            if (!_added)
            {
                var eurusd = AddForex("EURUSD", Resolution.Hour);
                _dataPointsPerSymbol.Add(eurusd.Symbol, 0);

                _added = true;
            }

            foreach (var kvp in data)
            {
                var symbol = kvp.Key;
                _dataPointsPerSymbol[symbol]++;

                Log($"{Time} {symbol.Value} {kvp.Value.Price}");
            }
        }

        /// <summary>
        /// End of algorithm run event handler. This method is called at the end of a backtest or live trading operation. Intended for closing out logs.
        /// </summary>
        public override void OnEndOfAlgorithm()
        {
            // EURUSD has only one day of hourly data, because it was added on the first time step instead of during Initialize
            var expectedDataPointsPerSymbol = new Dictionary<string, int>
            {
                { "EURGBP", 3 },
                { "EURUSD", 24 }
            };

            foreach (var kvp in _dataPointsPerSymbol)
            {
                var symbol = kvp.Key;
                var actualDataPoints = _dataPointsPerSymbol[symbol];
                Log($"Data points for symbol {symbol.Value}: {actualDataPoints}");

                if (actualDataPoints != expectedDataPointsPerSymbol[symbol.Value])
                {
                    throw new Exception($"Data point count mismatch for symbol {symbol.Value}: expected: {expectedDataPointsPerSymbol}, actual: {actualDataPoints}");
                }
            }
        }
    }
}