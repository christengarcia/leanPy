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
using System.IO;
using Newtonsoft.Json;
using NUnit.Framework;
using QuantConnect.Orders;

namespace QuantConnect.Tests.Common.Orders
{
    [TestFixture]
    public class OrderJsonConverterTests
    {

        [TestCase(Symbols.SymbolsKey.SPY)]
        [TestCase(Symbols.SymbolsKey.EURUSD)]
        [TestCase(Symbols.SymbolsKey.BTCUSD)]
        public void DeserializesMarketOrder(Symbols.SymbolsKey key)
        {
            var expected = new MarketOrder(Symbols.Lookup(key), 100, new DateTime(2015, 11, 23, 17, 15, 37), "now")
            {
                Id = 12345,
                Price = 209.03m,
                ContingentId = 123456,
                BrokerId = new List<string> {"727", "54970"}
            };

            TestOrderType(expected);
        }

        [TestCase(Symbols.SymbolsKey.SPY)]
        [TestCase(Symbols.SymbolsKey.EURUSD)]
        [TestCase(Symbols.SymbolsKey.BTCUSD)]
        public void DeserializesMarketOnOpenOrder(Symbols.SymbolsKey key)
        {
            var expected = new MarketOnOpenOrder(Symbols.Lookup(key), 100, new DateTime(2015, 11, 23, 17, 15, 37), "now")
            {
                Id = 12345,
                Price = 209.03m,
                ContingentId = 123456,
                BrokerId = new List<string> {"727", "54970"}
            };

            TestOrderType(expected);
        }

        [TestCase(Symbols.SymbolsKey.SPY)]
        [TestCase(Symbols.SymbolsKey.EURUSD)]
        [TestCase(Symbols.SymbolsKey.BTCUSD)]
        public void DeserializesMarketOnCloseOrder(Symbols.SymbolsKey key)
        {
            var expected = new MarketOnCloseOrder(Symbols.Lookup(key), 100, new DateTime(2015, 11, 23, 17, 15, 37), "now")
            {
                Id = 12345,
                Price = 209.03m,
                ContingentId = 123456,
                BrokerId = new List<string> {"727", "54970"}
            };

            TestOrderType(expected);
        }

        [TestCase(Symbols.SymbolsKey.SPY)]
        [TestCase(Symbols.SymbolsKey.EURUSD)]
        [TestCase(Symbols.SymbolsKey.BTCUSD)]
        public void DeserializesLimitOrder(Symbols.SymbolsKey key)
        {
            var expected = new LimitOrder(Symbols.Lookup(key), 100, 210.10m, new DateTime(2015, 11, 23, 17, 15, 37), "now")
            {
                Id = 12345,
                Price = 209.03m,
                ContingentId = 123456,
                BrokerId = new List<string> {"727", "54970"}
            };

            var actual = TestOrderType(expected);

            Assert.AreEqual(expected.LimitPrice, actual.LimitPrice);
        }

        [TestCase(Symbols.SymbolsKey.SPY)]
        [TestCase(Symbols.SymbolsKey.EURUSD)]
        [TestCase(Symbols.SymbolsKey.BTCUSD)]
        public void DeserializesStopMarketOrder(Symbols.SymbolsKey key)
        {
            var expected = new StopMarketOrder(Symbols.Lookup(key), 100, 210.10m, new DateTime(2015, 11, 23, 17, 15, 37), "now")
            {
                Id = 12345,
                Price = 209.03m,
                ContingentId = 123456,
                BrokerId = new List<string> {"727", "54970"}
            };

            var actual = TestOrderType(expected);

            Assert.AreEqual(expected.StopPrice, actual.StopPrice);
        }

        [TestCase(Symbols.SymbolsKey.SPY)]
        [TestCase(Symbols.SymbolsKey.EURUSD)]
        [TestCase(Symbols.SymbolsKey.BTCUSD)]
        public void DeserializesStopLimitOrder(Symbols.SymbolsKey key)
        {
            var expected = new StopLimitOrder(Symbols.Lookup(key), 100, 210.10m, 200.23m, new DateTime(2015, 11, 23, 17, 15, 37), "now")
            {
                Id = 12345,
                Price = 209.03m,
                ContingentId = 123456,
                BrokerId = new List<string> {"727", "54970"}
            };

            var actual = TestOrderType(expected);

            Assert.AreEqual(expected.StopPrice, actual.StopPrice);
            Assert.AreEqual(expected.LimitPrice, actual.LimitPrice);
        }

        [Test]
        public void DeserializesOptionExpireOrder()
        {
            var expected = new OptionExerciseOrder(Symbols.SPY_P_192_Feb19_2016, 100, new DateTime(2015, 11, 23, 17, 15, 37), "now")
            {
                Id = 12345,
                ContingentId = 123456,
                BrokerId = new List<string> { "727", "54970" }
            };

            // Note: Order price equals strike price found in Symbol object
            // Price = Symbol.ID.StrikePrice

            TestOrderType(expected);
        }

        [Test]
        public void DeserializesOldSymbol()
        {
            const string json = @"{'Type':0,
'Value':99986.827413672,
'Id':1,
'ContingentId':0,
'BrokerId':[1],
'Symbol':{'Value':'SPY',
'Permtick':'SPY'},
'Price':100.086914328,
'Time':'2010-03-04T14:31:00Z',
'Quantity':999,
'Status':3,
'TimeInForce':0,
'Tag':'',
'SecurityType':1,
'Direction':0,
'AbsoluteQuantity':999}";


            var order = DeserializeOrder<MarketOrder>(json);
            var actual = order.Symbol;

            Assert.AreEqual(Symbols.SPY, actual);
            Assert.AreEqual(Market.USA, actual.ID.Market);
        }

        [Test]
        public void WorksWithJsonConvert()
        {
            JsonConvert.DefaultSettings = () => new JsonSerializerSettings
            {
                Converters = {new OrderJsonConverter()}
            };

            const string json = @"{'Type':0,
'Value':99986.827413672,
'Id':1,
'ContingentId':0,
'BrokerId':[1],
'Symbol':{'Value':'SPY',
'Permtick':'SPY'},
'Price':100.086914328,
'Time':'2010-03-04T14:31:00Z',
'Quantity':999,
'Status':3,
'TimeInForce':1,
'Tag':'',
'SecurityType':1,
'Direction':0,
'AbsoluteQuantity':999}";

            var order = JsonConvert.DeserializeObject<Order>(json);
            Assert.IsInstanceOf<MarketOrder>(order);
            Assert.AreEqual(Market.USA, order.Symbol.ID.Market);
            Assert.AreEqual(1, (int)order.TimeInForce);
        }

        [Test]
        public void DeserializesOldDurationProperty()
        {
            JsonConvert.DefaultSettings = () => new JsonSerializerSettings
            {
                Converters = { new OrderJsonConverter() }
            };

            // The Duration property has been renamed to TimeInForce,
            // we still want to deserialize old JSON files containing Duration.
            const string json = @"{'Type':0,
'Value':99986.827413672,
'Id':1,
'ContingentId':0,
'BrokerId':[1],
'Symbol':{'Value':'SPY',
'Permtick':'SPY'},
'Price':100.086914328,
'Time':'2010-03-04T14:31:00Z',
'Quantity':999,
'Status':3,
'Duration':1,
'Tag':'',
'SecurityType':1,
'Direction':0,
'AbsoluteQuantity':999}";

            var order = JsonConvert.DeserializeObject<Order>(json);
            Assert.IsInstanceOf<MarketOrder>(order);
            Assert.AreEqual(Market.USA, order.Symbol.ID.Market);
            Assert.AreEqual(1, (int)order.TimeInForce);
        }

        [Test]
        public void DeserializesDecimalizedQuantity()
        {
            var expected = new MarketOrder(Symbols.BTCUSD, 0.123m, DateTime.Today);
            TestOrderType(expected);
        }

        private static T TestOrderType<T>(T expected)
            where T : Order
        {
            var json = JsonConvert.SerializeObject(expected);

            var actual = DeserializeOrder<T>(json);

            Assert.IsInstanceOf<T>(actual);
            Assert.AreEqual(expected.AbsoluteQuantity, actual.AbsoluteQuantity);
            CollectionAssert.AreEqual(expected.BrokerId, actual.BrokerId);
            Assert.AreEqual(expected.ContingentId, actual.ContingentId);
            Assert.AreEqual(expected.Direction, actual.Direction);
            Assert.AreEqual(expected.TimeInForce, actual.TimeInForce);
            Assert.AreEqual(expected.DurationValue, actual.DurationValue);
            Assert.AreEqual(expected.Id, actual.Id);
            Assert.AreEqual(expected.Price, actual.Price);
            Assert.AreEqual(expected.SecurityType, actual.SecurityType);
            Assert.AreEqual(expected.Status, actual.Status);
            Assert.AreEqual(expected.Symbol, actual.Symbol);
            Assert.AreEqual(expected.Tag, actual.Tag);
            Assert.AreEqual(expected.Time, actual.Time);
            Assert.AreEqual(expected.Type, actual.Type);
            Assert.AreEqual(expected.Value, actual.Value);
            Assert.AreEqual(expected.Quantity, actual.Quantity);
            Assert.AreEqual(expected.Symbol.ID.Market, actual.Symbol.ID.Market);

            return (T) actual;
        }

        private static Order DeserializeOrder<T>(string json) where T : Order
        {
            var converter = new OrderJsonConverter();
            var reader = new JsonTextReader(new StringReader(json));
            var jsonSerializer = new JsonSerializer();
            jsonSerializer.Converters.Add(converter);
            var actual = jsonSerializer.Deserialize<Order>(reader);
            return actual;
        }
    }
}
