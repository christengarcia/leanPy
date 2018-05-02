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
using NUnit.Framework;
using QuantConnect.Data;
using QuantConnect.Data.Auxiliary;
using QuantConnect.Data.Market;

namespace QuantConnect.Tests.Common.Data
{
    [TestFixture]
    public class SubscriptionManagerTests
    {
        [Test]
        [TestCase(SecurityType.Base, Resolution.Minute, typeof(TradeBar), TickType.Trade)]
        [TestCase(SecurityType.Base, Resolution.Tick, typeof(Tick), TickType.Trade)]
        [TestCase(SecurityType.Equity, Resolution.Minute, typeof(TradeBar), TickType.Trade)]
        [TestCase(SecurityType.Equity, Resolution.Tick, typeof(Tick), TickType.Trade)]
        [TestCase(SecurityType.Forex, Resolution.Minute, typeof(QuoteBar), TickType.Quote)]
        [TestCase(SecurityType.Forex, Resolution.Tick, typeof(Tick), TickType.Quote)]
        [TestCase(SecurityType.Cfd, Resolution.Minute, typeof(QuoteBar), TickType.Quote)]
        [TestCase(SecurityType.Cfd, Resolution.Tick, typeof(Tick), TickType.Quote)]
        public void GetsSubscriptionDataTypesSingle(SecurityType securityType, Resolution resolution, Type expectedDataType, TickType expectedTickType)
        {
            var types = GetSubscriptionDataTypes(securityType, resolution);

            Assert.AreEqual(1, types.Count);
            Assert.AreEqual(expectedDataType, types[0].Item1);
            Assert.AreEqual(expectedTickType, types[0].Item2);
        }

        [Test]
        [TestCase(SecurityType.Future, Resolution.Minute, typeof(ZipEntryName), TickType.Quote)]
        [TestCase(SecurityType.Future, Resolution.Tick, typeof(ZipEntryName), TickType.Quote)]
        [TestCase(SecurityType.Option, Resolution.Minute, typeof(ZipEntryName), TickType.Quote)]
        [TestCase(SecurityType.Option, Resolution.Tick, typeof(ZipEntryName), TickType.Quote)]
        public void GetsSubscriptionDataTypesCanonical(SecurityType securityType, Resolution resolution, Type expectedDataType, TickType expectedTickType)
        {
            var types = GetSubscriptionDataTypes(securityType, resolution, true);

            Assert.AreEqual(1, types.Count);
            Assert.AreEqual(expectedDataType, types[0].Item1);
            Assert.AreEqual(expectedTickType, types[0].Item2);
        }

        [Test]
        [TestCase(SecurityType.Future, Resolution.Minute)]
        [TestCase(SecurityType.Option, Resolution.Minute)]
        public void GetsSubscriptionDataTypesFuturesOptionsMinute(SecurityType securityType, Resolution resolution)
        {
            var types = GetSubscriptionDataTypes(securityType, resolution);

            Assert.AreEqual(3, types.Count);
            Assert.AreEqual(typeof(QuoteBar), types[0].Item1);
            Assert.AreEqual(TickType.Quote, types[0].Item2);
            Assert.AreEqual(typeof(TradeBar), types[1].Item1);
            Assert.AreEqual(TickType.Trade, types[1].Item2);
            Assert.AreEqual(typeof(OpenInterest), types[2].Item1);
            Assert.AreEqual(TickType.OpenInterest, types[2].Item2);
        }

        [Test]
        [TestCase(SecurityType.Future, Resolution.Tick)]
        [TestCase(SecurityType.Option, Resolution.Tick)]
        public void GetsSubscriptionDataTypesFuturesOptionsTick(SecurityType securityType, Resolution resolution)
        {
            var types = GetSubscriptionDataTypes(securityType, resolution);

            Assert.AreEqual(3, types.Count);
            Assert.AreEqual(typeof(Tick), types[0].Item1);
            Assert.AreEqual(TickType.Quote, types[0].Item2);
            Assert.AreEqual(typeof(Tick), types[1].Item1);
            Assert.AreEqual(TickType.Trade, types[1].Item2);
            Assert.AreEqual(typeof(Tick), types[2].Item1);
            Assert.AreEqual(TickType.OpenInterest, types[2].Item2);
        }

        [Test]
        [TestCase(Resolution.Minute)]
        [TestCase(Resolution.Tick)]
        public void GetsSubscriptionDataTypesCrypto(Resolution resolution)
        {
            var types = GetSubscriptionDataTypes(SecurityType.Crypto, resolution);

            Assert.AreEqual(2, types.Count);

            if (resolution == Resolution.Tick)
            {
                Assert.AreEqual(typeof(Tick), types[0].Item1);
                Assert.AreEqual(typeof(Tick), types[1].Item1);
            }
            else
            {
                Assert.AreEqual(typeof(TradeBar), types[0].Item1);
                Assert.AreEqual(typeof(QuoteBar), types[1].Item1);
            }

            Assert.AreEqual(TickType.Trade, types[0].Item2);
            Assert.AreEqual(TickType.Quote, types[1].Item2);
        }

        private static List<Tuple<Type, TickType>> GetSubscriptionDataTypes(SecurityType securityType, Resolution resolution, bool isCanonical = false)
        {
            var timeKeeper = new TimeKeeper(DateTime.UtcNow);
            var subscriptionManager = new SubscriptionManager(new AlgorithmSettings(), timeKeeper);
            return subscriptionManager.LookupSubscriptionConfigDataTypes(securityType, resolution, isCanonical);
        }
    }
}
