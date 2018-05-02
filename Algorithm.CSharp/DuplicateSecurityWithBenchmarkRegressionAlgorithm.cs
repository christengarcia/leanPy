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
using System.Linq;
using QuantConnect.Data;
using QuantConnect.Indicators;
using QuantConnect.Securities.Equity;

namespace QuantConnect.Algorithm.CSharp
{
    /// <summary>
    /// This algorithm is a regression test case using consolidators with SetBenchmark and duplicate securities.
    /// </summary>
    public class DuplicateSecurityWithBenchmarkRegressionAlgorithm : QCAlgorithm
    {
        private SimpleMovingAverage _spyMovingAverage;
        private Equity _spy1;
        private Equity _spy2;

        /// <summary>
        /// Initialise the data and resolution required, as well as the cash and start-end dates for your algorithm. All algorithms must initialized.
        /// </summary>
        public override void Initialize()
        {
            SetStartDate(2013, 10, 07);
            SetEndDate(2013, 10, 11);
            SetCash(100000);

            _spy1 = AddEquity("SPY", Resolution.Daily);

            // SetBenchmark call prevents SMA update
            SetBenchmark("SPY");

            _spy2 = AddEquity("SPY", Resolution.Daily);
            _spyMovingAverage = SMA("SPY", 3, Resolution.Daily);
        }

        /// <summary>
        /// OnData event is the primary entry point for your algorithm. Each new data point will be pumped in here.
        /// </summary>
        /// <param name="data">Slice object keyed by symbol containing the stock data</param>
        public override void OnData(Slice data)
        {
            Log($"{Time} - {Securities["SPY"].Price}, {_spyMovingAverage}");
        }

        /// <summary>
        /// End of algorithm run event handler. This method is called at the end of a backtest or live trading operation. Intended for closing out logs.
        /// </summary>
        public override void OnEndOfAlgorithm()
        {
            Log($"_spy1.Subscriptions.Count(): {_spy1.Subscriptions.Count()}");
            Log($"_spy2.Subscriptions.Count(): {_spy2.Subscriptions.Count()}");
            Log($"_spy1.Subscriptions.First().Consolidators.Count: {_spy1.Subscriptions.First().Consolidators.Count}");
            Log($"_spy2.Subscriptions.First().Consolidators.Count: {_spy2.Subscriptions.First().Consolidators.Count}");

            if (_spyMovingAverage == 0)
            {
                throw new Exception("SMA was not updated.");
            }
        }
    }
}