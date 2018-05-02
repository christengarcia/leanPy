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
using NUnit.Framework;
using QuantConnect.Data;
using QuantConnect.Data.Market;
using QuantConnect.Indicators;
using QuantConnect.Securities;

namespace QuantConnect.Tests.Common.Securities.Equity
{
    [TestFixture]
    public class PriceVariationModelsTests
    {
        [TestCase("SPY", SecurityType.Equity, Market.USA, DataNormalizationMode.Adjusted)]
        [TestCase("SPY", SecurityType.Equity, Market.USA, DataNormalizationMode.SplitAdjusted)]
        [TestCase("EURUSD", SecurityType.Forex, Market.FXCM, DataNormalizationMode.Adjusted)]
        [TestCase("EURUSD", SecurityType.Forex, Market.FXCM, DataNormalizationMode.SplitAdjusted)]
        public void CheckSecurityMinimumPriceVariation(string ticker, SecurityType securityType, string market, DataNormalizationMode mode)
        {
            var symbol = Symbol.Create(ticker, securityType, market);
            var security = GetSecurity(symbol, mode);
            var expected = security.SymbolProperties.MinimumPriceVariation;
            var adjutedEquity = mode == DataNormalizationMode.Adjusted && securityType == SecurityType.Equity;

            security.SetMarketPrice(new IndicatorDataPoint(symbol, DateTime.Now, 10m));
            var actual = security.PriceVariationModel.GetMinimumPriceVariation(security);
            Assert.AreEqual(adjutedEquity ? 0 : expected, actual);

            security.SetMarketPrice(new IndicatorDataPoint(symbol, DateTime.Now, 1m));
            actual = security.PriceVariationModel.GetMinimumPriceVariation(security);
            Assert.AreEqual(adjutedEquity ? 0 : expected, actual);

            // Special case, if stock price less than $1, minimum price variation is $0.0001
            if (securityType == SecurityType.Equity) expected = 0.0001m;

            security.SetMarketPrice(new IndicatorDataPoint(symbol, DateTime.Now, .99m));
            actual = security.PriceVariationModel.GetMinimumPriceVariation(security);
            Assert.AreEqual(adjutedEquity ? 0 : expected, actual);
        }

        private Security GetSecurity(Symbol symbol, DataNormalizationMode mode)
        {
            var symbolProperties = SymbolPropertiesDatabase.FromDataFolder()
                .GetSymbolProperties(symbol.ID.Market, symbol.Value, symbol.ID.SecurityType, CashBook.AccountCurrency);

            Security security;
            if (symbol.ID.SecurityType == SecurityType.Equity)
            {
                security = new QuantConnect.Securities.Equity.Equity(
                    SecurityExchangeHours.AlwaysOpen(TimeZones.NewYork),
                    new SubscriptionDataConfig(typeof(TradeBar), symbol, Resolution.Minute, TimeZones.NewYork, TimeZones.NewYork, true, true, false),
                    new Cash(CashBook.AccountCurrency, 0, 1m),
                    symbolProperties);
            }
            else
            {
                security = new QuantConnect.Securities.Forex.Forex(
                   SecurityExchangeHours.AlwaysOpen(TimeZones.NewYork),
                   new Cash(CashBook.AccountCurrency, 0, 1m),
                   new SubscriptionDataConfig(typeof(TradeBar), symbol, Resolution.Minute, TimeZones.NewYork, TimeZones.NewYork, true, true, false),
                   symbolProperties);
            }

            var TimeKeeper = new TimeKeeper(DateTime.Now.ConvertToUtc(TimeZones.NewYork), new[] { TimeZones.NewYork });
            security.SetLocalTimeKeeper(TimeKeeper.GetLocalTimeKeeper(TimeZones.NewYork));
            security.SetDataNormalizationMode(mode);

            return security;
        }
    }
}