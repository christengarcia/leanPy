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

using System;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using NUnit.Framework;
using QuantConnect.Data;
using QuantConnect.Data.Market;
using QuantConnect.Securities.Option;

namespace QuantConnect.Tests.Common
{
    [TestFixture]
    public class SymbolTests
    {
        private JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.All
        };

        [Theory]
        [TestCaseSource("GetSymbolCreateTestCaseData")]
        public void SymbolCreate(string ticker, SecurityType securityType, string market, Symbol expected)
        {
            Assert.AreEqual(Symbol.Create(ticker, securityType, market), expected);
        }

        private TestCaseData[] GetSymbolCreateTestCaseData()
        {
            return new []
            {
                new TestCaseData("SPY", SecurityType.Equity, Market.USA, new Symbol(SecurityIdentifier.GenerateEquity("SPY", Market.USA), "SPY")),
                new TestCaseData("EURUSD", SecurityType.Forex, Market.FXCM, new Symbol(SecurityIdentifier.GenerateForex("EURUSD", Market.FXCM), "EURUSD")),
                new TestCaseData("SPY", SecurityType.Option, Market.USA, new Symbol(SecurityIdentifier.GenerateOption(SecurityIdentifier.DefaultDate, Symbols.SPY.ID, Market.USA, 0, default(OptionRight), default(OptionStyle)), "?SPY"))
            };
        }

        [Test]
        public void SymbolCreateWithOptionSecurityTypeCreatesCanonicalOptionSymbol()
        {
            var symbol = Symbol.Create("SPY", SecurityType.Option, Market.USA);
            var sid = symbol.ID;
            Assert.AreEqual(SecurityIdentifier.DefaultDate, sid.Date);
            Assert.AreEqual(0m, sid.StrikePrice);
            Assert.AreEqual(default(OptionRight), sid.OptionRight);
            Assert.AreEqual(default(OptionStyle), sid.OptionStyle);
        }

        [Test]
        public void CanonicalOptionSymbolAliasHasQuestionMark()
        {
            var symbol = Symbol.Create("SPY", SecurityType.Option, Market.USA);
            Assert.AreEqual("?SPY", symbol.Value);
        }

        [Test]
        public void UsesSidForDictionaryKey()
        {
            var sid = SecurityIdentifier.GenerateEquity("SPY", Market.USA);
            var dictionary = new Dictionary<Symbol, int>
            {
                {new Symbol(sid, "value"), 1}
            };

            var key = new Symbol(sid, "other value");
            Assert.IsTrue(dictionary.ContainsKey(key));
        }

        [Test]
        public void SurvivesRoundtripSerialization()
        {
            var sid = SecurityIdentifier.GenerateEquity("SPY", Market.USA);
            var expected = new Symbol(sid, "value");
            var json = JsonConvert.SerializeObject(expected, Settings);
            var actual = JsonConvert.DeserializeObject<Symbol>(json, Settings);
            Assert.AreEqual(expected, actual);
        }

        [Test]
        public void CreatesOptionWithUnderlying()
        {
            var option = Symbol.CreateOption("XLRE", Market.USA, OptionStyle.American, OptionRight.Call, 21m, new DateTime(2016, 08, 19));

            Assert.AreEqual(option.ID.Date, new DateTime(2016, 08, 19));
            Assert.AreEqual(option.ID.StrikePrice, 21m);
            Assert.AreEqual(option.ID.OptionRight, OptionRight.Call);
            Assert.AreEqual(option.ID.OptionStyle, OptionStyle.American);
            Assert.AreEqual(option.Underlying.ID.Symbol, "XLRE");

        }
        [Test]
        public void SurvivesRoundtripSerializationOption()
        {
            var expected = Symbol.CreateOption("XLRE", Market.USA, OptionStyle.American, OptionRight.Call, 21m, new DateTime(2016, 08, 19));

            var json = JsonConvert.SerializeObject(expected, Settings);
            var actual = JsonConvert.DeserializeObject<Symbol>(json, Settings);
            Assert.AreEqual(expected, actual);

            Assert.AreEqual(expected.ID, actual.ID);
            Assert.AreEqual(expected.Value, actual.Value);
            Assert.AreEqual(expected.ID.Date, actual.ID.Date);
            Assert.AreEqual(expected.ID.StrikePrice, actual.ID.StrikePrice);
            Assert.AreEqual(expected.ID.OptionRight, actual.ID.OptionRight);
            Assert.AreEqual(expected.ID.OptionStyle, actual.ID.OptionStyle);

            Assert.AreEqual(expected.Underlying.ID, actual.Underlying.ID);
            Assert.AreEqual(expected.Underlying.Value, actual.Underlying.Value);
        }

        [Test]
        public void SurvivesRoundtripSerializationCanonicalOption()
        {
            var expected = Symbol.Create("SPY", SecurityType.Option, Market.USA);

            var json = JsonConvert.SerializeObject(expected, Settings);
            var actual = JsonConvert.DeserializeObject<Symbol>(json, Settings);
            Assert.AreEqual(expected, actual);

            Assert.AreEqual(SecurityIdentifier.DefaultDate, actual.ID.Date);
            Assert.AreEqual(0m, actual.ID.StrikePrice);
            Assert.AreEqual(default(OptionRight), actual.ID.OptionRight);
            Assert.AreEqual(default(OptionStyle), actual.ID.OptionStyle);
            Assert.AreNotEqual(default(Symbol), actual.Underlying);
        }

        [Test]
        public void SurvivesRoundtripSerializationWithTypeNameHandling()
        {
            var sid = SecurityIdentifier.GenerateEquity("SPY", Market.USA);
            var expected = new Symbol(sid, "value");
            var json = JsonConvert.SerializeObject(expected, Settings);
            var actual = JsonConvert.DeserializeObject<Symbol>(json);
            Assert.AreEqual(expected, actual);
        }

        [Test]
        public void HandlesListTicks()
        {
            const string json = @"{'$type':'System.Collections.Generic.List`1[[QuantConnect.Data.BaseData, QuantConnect.Common]], mscorlib',
'$values':[{'$type':'QuantConnect.Data.Market.Tick, QuantConnect.Common',
'TickType':0,'Quantity':1,'Exchange':'',
'SaleCondition':'',
'Suspicious':false,'BidPrice':0.72722,'AskPrice':0.7278,'BidSize':0,'AskSize':0,'LastPrice':0.72722,'DataType':2,'IsFillForward':false,'Time':'2015-09-18T16:52:37.379',
'EndTime':'2015-09-18T16:52:37.379',
'Symbol':{'$type':'QuantConnect.Symbol, QuantConnect.Common',
'Value':'EURGBP',
'ID':'EURGBP 5O'},'Value':0.72722,'Price':0.72722}]}";

            var expected = new Symbol(SecurityIdentifier.GenerateForex("EURGBP", Market.FXCM),  "EURGBP");
            var settings = Settings;
            var actual = JsonConvert.DeserializeObject<List<BaseData>>(json, settings);
            Assert.AreEqual(expected, actual[0].Symbol);
        }

        [Test]
        public void HandlesListTicksWithDifferentSymbols()
        {
            // the first serialized Tick object has a Symbol of EURGBP and the second has EURUSD, but the output
            const string json =
                "{'$type':'System.Collections.Generic.List`1[[QuantConnect.Data.BaseData, QuantConnect.Common]], mscorlib','$values':[" +

                    "{'$type':'QuantConnect.Data.Market.Tick, QuantConnect.Common'," +
                    "'TickType':0,'Quantity':1,'Exchange':'','SaleCondition':'','Suspicious':false," +
                    "'BidPrice':1.11895,'AskPrice':1.11898,'LastPrice':1.11895,'DataType':2,'IsFillForward':false," +
                    "'Time':'2015-09-22T01:26:44.676','EndTime':'2015-09-22T01:26:44.676'," +
                    "'Symbol':{'$type':'QuantConnect.Symbol, QuantConnect.Common','Value':'EURUSD', 'ID': 'EURUSD 5O'}," +
                    "'Value':1.11895,'Price':1.11895}," +

                    "{'$type':'QuantConnect.Data.Market.Tick, QuantConnect.Common'," +
                    "'TickType':0,'Quantity':1,'Exchange':'','SaleCondition':'','Suspicious':false," +
                    "'BidPrice':0.72157,'AskPrice':0.72162,'LastPrice':0.72157,'DataType':2,'IsFillForward':false," +
                    "'Time':'2015-09-22T01:26:44.675','EndTime':'2015-09-22T01:26:44.675'," +
                    "'Symbol':{'$type':'QuantConnect.Symbol, QuantConnect.Common','Value':'EURGBP', 'ID': 'EURGBP 5O'}," +
                    "'Value':0.72157,'Price':0.72157}," +

                    "]}";

            var actual = JsonConvert.DeserializeObject<List<BaseData>>(json, Settings);
            Assert.IsFalse(actual.All(x => x.Symbol == new Symbol(SecurityIdentifier.GenerateForex("EURUSD", Market.FXCM), "EURUSD")));
        }

        [Test]
        public void SymbolTypeNameHandling()
        {
            const string json = @"{'$type':'QuantConnect.Symbol, QuantConnect.Common', 'Value':'EURGBP', 'ID': 'EURGBP 5O'}";
            var expected = new Symbol(SecurityIdentifier.GenerateForex("EURGBP", Market.FXCM), "EURGBP");
            var actual = JsonConvert.DeserializeObject<Symbol>(json, Settings);
            Assert.AreEqual(expected, actual);
        }

        [Test]
        public void TickRoundTrip()
        {
            var tick = new Tick
            {
                Symbol = Symbols.EURGBP,
                AskPrice = 1,
                Time = DateTime.Now,
                Exchange = "",
                Value = 2,
                EndTime = DateTime.Now,
                Quantity = 1,
                BidPrice = 2,
                SaleCondition = ""
            };
            var json = JsonConvert.SerializeObject(tick, Settings);
            var actual = JsonConvert.DeserializeObject<Tick>(json, Settings);
            Assert.AreEqual(tick.Symbol, actual.Symbol);

            json = JsonConvert.SerializeObject(tick, Settings);
            actual = JsonConvert.DeserializeObject<Tick>(json);
            Assert.AreEqual(tick.Symbol, actual.Symbol);
        }

        [Test]
        public void CompareToItselfReturnsZero()
        {
            var sym = new Symbol(SecurityIdentifier.GenerateForex("sym", Market.FXCM), "sym");
            Assert.AreEqual(0, sym.CompareTo(sym));
        }

        [Test]
        public void ComparesTheSameAsStringCompare()
        {
            var a = new Symbol(SecurityIdentifier.GenerateForex("a", Market.FXCM), "a");
            var z = new Symbol(SecurityIdentifier.GenerateForex("z", Market.FXCM), "z");

            Assert.AreEqual(string.Compare("a", "z", StringComparison.Ordinal), a.CompareTo(z));
            Assert.AreEqual(string.Compare("z", "a", StringComparison.Ordinal), z.CompareTo(a));
        }

        [Test]
        public void ComparesTheSameAsStringCompareAndIgnoresCase()
        {
            var a = new Symbol(SecurityIdentifier.GenerateForex("a", Market.FXCM), "a");
            var z = new Symbol(SecurityIdentifier.GenerateForex("z", Market.FXCM), "z");

            Assert.AreEqual(string.Compare("a", "Z", StringComparison.OrdinalIgnoreCase), a.CompareTo(z));
            Assert.AreEqual(string.Compare("z", "A", StringComparison.OrdinalIgnoreCase), z.CompareTo(a));
        }

        [Test]
        public void ComparesAgainstStringWithoutException()
        {
            var a = new Symbol(SecurityIdentifier.GenerateForex("a", Market.FXCM), "a");
            Assert.AreEqual(0, a.CompareTo("a"));
        }

        [Test]
        public void ComparesAgainstStringIgnoringCase()
        {
            var a = new Symbol(SecurityIdentifier.GenerateForex("a", Market.FXCM), "a");
            Assert.AreEqual(0, a.CompareTo("A"));
        }

        [Test]
        public void ComparesAgainstNullOrEmpty()
        {
            var validSymbol = Symbols.SPY;
            var emptySymbol = Symbol.Empty;
            Symbol nullSymbol = null;

            Assert.IsTrue(nullSymbol == emptySymbol);
            Assert.IsFalse(nullSymbol != emptySymbol);

            Assert.IsTrue(emptySymbol == nullSymbol);
            Assert.IsFalse(emptySymbol != nullSymbol);

            Assert.IsTrue(validSymbol != null);
            Assert.IsTrue(emptySymbol == null);
            Assert.IsTrue(nullSymbol == null);

            Assert.IsFalse(validSymbol == null);
            Assert.IsFalse(emptySymbol != null);
            Assert.IsFalse(nullSymbol != null);

            Assert.IsTrue(validSymbol != Symbol.Empty);
            Assert.IsTrue(emptySymbol == Symbol.Empty);
            Assert.IsTrue(nullSymbol == Symbol.Empty);

            Assert.IsFalse(validSymbol == Symbol.Empty);
            Assert.IsFalse(emptySymbol != Symbol.Empty);
            Assert.IsFalse(nullSymbol != Symbol.Empty);

            Assert.IsTrue(null != validSymbol);
            Assert.IsTrue(null == emptySymbol);
            Assert.IsTrue(null == nullSymbol);

            Assert.IsFalse(null == validSymbol);
            Assert.IsFalse(null != emptySymbol);
            Assert.IsFalse(null != nullSymbol);

            Assert.IsTrue(Symbol.Empty != validSymbol);
            Assert.IsTrue(Symbol.Empty == emptySymbol);
            Assert.IsTrue(Symbol.Empty == nullSymbol);

            Assert.IsFalse(Symbol.Empty == validSymbol);
            Assert.IsFalse(Symbol.Empty != emptySymbol);
            Assert.IsFalse(Symbol.Empty != nullSymbol);
        }

        [Test]
        public void BackwardsCompatibleJson()
        {
            var symbol = new Symbol(SecurityIdentifier.GenerateForex("a", Market.FXCM), "a");
            var json = JsonConvert.SerializeObject(symbol, new JsonSerializerSettings{Formatting = Formatting.Indented, TypeNameHandling = TypeNameHandling.All});
            var oldSymbol = JsonConvert.DeserializeObject<OldSymbol>(json);
            Assert.AreEqual("A", oldSymbol.Value);
            Assert.AreEqual("A", oldSymbol.Permtick);
        }

        [Test]
        public void ImplicitOperatorsAreInverseFunctions()
        {
#pragma warning disable 0618 // This test requires implicit operators
            var eurusd = new Symbol(SecurityIdentifier.GenerateForex("EURUSD", Market.FXCM), "EURUSD");
            string stringEurusd = eurusd;
            Symbol symbolEurusd = stringEurusd;
            Assert.AreEqual(eurusd, symbolEurusd);
#pragma warning restore 0618
        }

        [Test]
        public void ImplicitOperatorsReturnSIDOnFailure()
        {
#pragma warning disable 0618 // This test requires implicit operators
            // this doesn't exist in the symbol cache
            var eurusd = new Symbol(SecurityIdentifier.GenerateForex("NOT-A-SECURITY", Market.FXCM), "EURUSD");
            string stringEurusd = eurusd;
            Assert.AreEqual(eurusd.ID.ToString(), stringEurusd);

            Symbol notASymbol = "this will not resolve to a proper Symbol instance";
            Assert.AreEqual(Symbol.Empty, notASymbol);
#pragma warning restore 0618
        }

        [Test]
        public void ImplicitFromStringChecksSymbolCache()
        {
#pragma warning disable 0618 // This test requires implicit operators
            SymbolCache.Set("EURUSD", Symbol.Create("EURUSD", SecurityType.Forex, Market.FXCM));
            string ticker = "EURUSD";
            Symbol actual = ticker;
            var expected = SymbolCache.GetSymbol(ticker);
            Assert.AreEqual(expected, actual);
            SymbolCache.Clear();
#pragma warning restore 0618
        }

        [Test]
        public void ImplicitFromStringParsesSid()
        {
#pragma warning disable 0618 // This test requires implicit operators
            SymbolCache.Set("EURUSD", Symbol.Create("EURUSD", SecurityType.Forex, Market.FXCM));
            var expected = SymbolCache.GetSymbol("EURUSD");
            string sid = expected.ID.ToString();
            Symbol actual = sid;
            Assert.AreEqual(expected, actual);
            SymbolCache.Clear();
#pragma warning restore 0618
        }

        [Test]
        public void ImplicitFromWithinStringLiftsSecondArgument()
        {
#pragma warning disable 0618 // This test requires implicit operators
            SymbolCache.Clear();
            SymbolCache.Set("EURUSD", Symbols.EURUSD);
            var expected = SymbolCache.GetSymbol("EURUSD");
            string stringValue = expected;
            string notFound = "EURGBP 5O";
            var expectedNotFoundSymbol = Symbols.EURGBP;
            string sid = expected.ID.ToString();
            Symbol actual = sid;
            if (!(expected == stringValue))
            {
                Assert.Fail("Failed expected == string");
            }
            else if (!(stringValue == expected))
            {
                Assert.Fail("Failed string == expected");
            }
            else if (expected != stringValue)
            {
                Assert.Fail("Failed expected != string");
            }
            else if (stringValue != expected)
            {
                Assert.Fail("Failed string != expected");
            }

            Symbol notFoundSymbol = notFound;
            Assert.AreEqual(expectedNotFoundSymbol, notFoundSymbol);
            SymbolCache.Clear();
#pragma warning restore 0618
        }


        [Test]
        public void TestIfWeDetectCorrectlyWeekliesAndStandardOptionsBeforeFeb2015()
        {
            var symbol = Symbol.CreateOption("SPY", Market.USA, OptionStyle.American, OptionRight.Call, 200, new DateTime(2012, 09, 22));
            var weeklySymbol = Symbol.CreateOption("SPY", Market.USA, OptionStyle.American, OptionRight.Call, 200, new DateTime(2012, 09, 07));

            Assert.True(OptionSymbol.IsStandardContract(symbol));
            Assert.False(OptionSymbol.IsStandardContract(weeklySymbol));

            Assert.AreEqual(new DateTime(2012, 09, 21)/*Friday*/, OptionSymbol.GetLastDayOfTrading(symbol));
            Assert.AreEqual(new DateTime(2012, 09, 07)/*Friday*/, OptionSymbol.GetLastDayOfTrading(weeklySymbol));
        }

        [Test]
        public void TestIfWeDetectCorrectlyWeekliesAndStandardOptionsAfterFeb2015()
        {
            var symbol = Symbol.CreateOption("SPY", Market.USA, OptionStyle.American, OptionRight.Call, 200, new DateTime(2016, 02, 19));
            var weeklySymbol = Symbol.CreateOption("SPY", Market.USA, OptionStyle.American, OptionRight.Call, 200, new DateTime(2016, 02, 05));

            Assert.True(OptionSymbol.IsStandardContract(symbol));
            Assert.False(OptionSymbol.IsStandardContract(weeklySymbol));

            Assert.AreEqual(new DateTime(2016, 02, 19)/*Friday*/, OptionSymbol.GetLastDayOfTrading(symbol));
            Assert.AreEqual(new DateTime(2016, 02, 05)/*Friday*/, OptionSymbol.GetLastDayOfTrading(weeklySymbol));
        }

        [Test]
        public void HasUnderlyingSymbolReturnsTrueWhenSpecifyingCorrectUnderlying()
        {
            Assert.IsTrue(Symbols.SPY_C_192_Feb19_2016.HasUnderlyingSymbol(Symbols.SPY));
        }

        [Test]
        public void HasUnderlyingSymbolReturnsFalsWhenSpecifyingIncorrectUnderlying()
        {
            Assert.IsFalse(Symbols.SPY_C_192_Feb19_2016.HasUnderlyingSymbol(Symbols.AAPL));
        }

        class OldSymbol
        {
            public string Value { get; set; }
            public string Permtick { get; set; }
        }
    }
}
