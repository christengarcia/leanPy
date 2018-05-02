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
using QuantConnect.Algorithm.Framework;
using QuantConnect.Algorithm.Framework.Alphas;
using QuantConnect.Algorithm.Framework.Portfolio;
using QuantConnect.Algorithm.Framework.Selection;
using QuantConnect.Data.UniverseSelection;
using QuantConnect.Orders;
using QuantConnect.Scheduling;

namespace QuantConnect.Algorithm.CSharp
{
    /// <summary>
    /// Regression algortihm for testing <see cref="ScheduledUniverseSelectionModel"/> scheduling functions
    /// </summary>
    public class ScheduledUniverseSelectionModelRegressionAlgorithm : QCAlgorithmFramework
    {
        public override void Initialize()
        {
            UniverseSettings.Resolution = Resolution.Hour;

            SetStartDate(2017, 01, 01);
            SetEndDate(2017, 02, 01);

            // selection will run on mon/tues/thurs at 00:00/06:00/12:00/18:00
            SetUniverseSelection(new ScheduledUniverseSelectionModel(
                DateRules.Every(DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Thursday),
                TimeRules.Every(TimeSpan.FromHours(12)),
                SelectSymbols
            ));

            SetAlpha(new ConstantAlphaModel(InsightType.Price, InsightDirection.Up, TimeSpan.FromDays(1)));
            SetPortfolioConstruction(new EqualWeightingPortfolioConstructionModel());
        }

        private IEnumerable<Symbol> SelectSymbols(DateTime dateTime)
        {
            if (dateTime.DayOfWeek == DayOfWeek.Monday || dateTime.DayOfWeek == DayOfWeek.Tuesday)
            {
                yield return QuantConnect.Symbol.Create("SPY", SecurityType.Equity, Market.USA);
            }
            else if (dateTime.DayOfWeek == DayOfWeek.Wednesday)
            {
                // given the date/time rules specified in Initialize, this symbol will never be selected (not invoked on wednesdays)
                yield return QuantConnect.Symbol.Create("AAPL", SecurityType.Equity, Market.USA);
            }
            else
            {
                yield return QuantConnect.Symbol.Create("IBM", SecurityType.Equity, Market.USA);
            }

            if (dateTime.DayOfWeek == DayOfWeek.Tuesday || dateTime.DayOfWeek == DayOfWeek.Thursday)
            {
                yield return QuantConnect.Symbol.Create("EURUSD", SecurityType.Forex, Market.FXCM);
            }
            else if (dateTime.DayOfWeek == DayOfWeek.Friday)
            {
                // given the date/time rules specified in Initialize, this symbol will never be selected (every 6 hours never lands on hour==1)
                yield return QuantConnect.Symbol.Create("EURGBP", SecurityType.Forex, Market.FXCM);
            }
            else
            {
                yield return QuantConnect.Symbol.Create("NZDUSD", SecurityType.Forex, Market.FXCM);
            }
        }

        // some days of the week have different behavior the first time -- less securities to remove
        private readonly HashSet<DayOfWeek> _seenDays = new HashSet<DayOfWeek>();
        public override void OnSecuritiesChanged(SecurityChanges changes)
        {
            Console.WriteLine($"{Time}: {changes}");

            switch (Time.DayOfWeek)
            {
                case DayOfWeek.Monday:
                    ExpectAdditions(changes, "SPY", "NZDUSD");
                    if (_seenDays.Add(DayOfWeek.Monday))
                    {
                        ExpectRemovals(changes, null);
                    }
                    else
                    {
                        ExpectRemovals(changes, "EURUSD", "IBM");
                    }
                    break;

                case DayOfWeek.Tuesday:
                    ExpectAdditions(changes, "EURUSD");
                    if (_seenDays.Add(DayOfWeek.Tuesday))
                    {
                        ExpectRemovals(changes, "NZDUSD");
                    }
                    else
                    {
                        ExpectRemovals(changes, "NZDUSD");
                    }
                    break;

                case DayOfWeek.Wednesday:
                    // selection function not invoked on wednesdays
                    ExpectAdditions(changes, null);
                    ExpectRemovals(changes, null);
                    break;

                case DayOfWeek.Thursday:
                    ExpectAdditions(changes, "IBM");
                    ExpectRemovals(changes, "SPY");
                    break;

                case DayOfWeek.Friday:
                    // selection function not invoked on fridays
                    ExpectAdditions(changes, null);
                    ExpectRemovals(changes, null);
                    break;
            }
        }

        public override void OnOrderEvent(OrderEvent orderEvent)
        {
            Console.WriteLine($"{Time}: {orderEvent}");
        }

        private void ExpectAdditions(SecurityChanges changes, params string[] tickers)
        {
            if (tickers == null && changes.AddedSecurities.Count > 0)
            {
                throw new Exception($"{Time}: Expected no additions: {Time.DayOfWeek}");
            }
            if (tickers == null)
            {
                return;
            }

            foreach (var ticker in tickers)
            {
                if (changes.AddedSecurities.All(s => s.Symbol.Value != ticker))
                {
                    throw new Exception($"{Time}: Expected {ticker} to be added: {Time.DayOfWeek}");
                }
            }
        }

        private void ExpectRemovals(SecurityChanges changes, params string[] tickers)
        {
            if (tickers == null && changes.RemovedSecurities.Count > 0)
            {
                throw new Exception($"{Time}: Expected no removals: {Time.DayOfWeek}");
            }

            if (tickers == null)
            {
                return;
            }

            foreach (var ticker in tickers)
            {
                if (changes.RemovedSecurities.All(s => s.Symbol.Value != ticker))
                {
                    throw new Exception($"{Time}: Expected {ticker} to be removed: {Time.DayOfWeek}");
                }
            }
        }
    }
}
