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
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using QuantConnect.Data;
using QuantConnect.Data.Market;
using QuantConnect.Lean.Engine.DataFeeds.Enumerators;
using QuantConnect.Securities;
using QuantConnect.Securities.Equity;
using QuantConnect.Securities.Forex;
using QuantConnect.Util;

namespace QuantConnect.Tests.Engine.DataFeeds
{
    [TestFixture]
    public class FillForwardEnumeratorTest
    {
        [Test]
        public void FillsForwardMidDay()
        {
            var dataResolution = Time.OneMinute;

            var reference = new DateTime(2015, 6, 25, 9, 30, 0);
            var data = Enumerable.Range(0, 2).Select(x => new TradeBar
            {
                Time = reference.AddMinutes(x*2),
                Value = x,
                Period = dataResolution
            }).ToList();
            var enumerator = data.GetEnumerator();

            var exchange = new EquityExchange();
            var isExtendedMarketHours = false;
            var fillForwardResolution = TimeSpan.FromMinutes(1);
            var fillForwardEnumerator = new FillForwardEnumerator(enumerator, exchange, Ref.Create(fillForwardResolution), isExtendedMarketHours, data.Last().EndTime, dataResolution, exchange.TimeZone);

            // 9:31
            Assert.IsTrue(fillForwardEnumerator.MoveNext());
            Assert.AreEqual(reference.AddMinutes(1), fillForwardEnumerator.Current.EndTime);
            Assert.AreEqual(0, fillForwardEnumerator.Current.Value);
            Assert.AreEqual(dataResolution, fillForwardEnumerator.Current.EndTime - fillForwardEnumerator.Current.Time);

            // 9:32 (ff)
            Assert.IsTrue(fillForwardEnumerator.MoveNext());
            Assert.AreEqual(reference.AddMinutes(2), fillForwardEnumerator.Current.EndTime);
            Assert.AreEqual(0, fillForwardEnumerator.Current.Value);
            Assert.IsTrue(fillForwardEnumerator.Current.IsFillForward);
            Assert.AreEqual(dataResolution, fillForwardEnumerator.Current.EndTime - fillForwardEnumerator.Current.Time);


            // 9:33
            Assert.IsTrue(fillForwardEnumerator.MoveNext());
            Assert.AreEqual(reference.AddMinutes(3), fillForwardEnumerator.Current.EndTime);
            Assert.AreEqual(1, fillForwardEnumerator.Current.Value);
            Assert.AreEqual(dataResolution, fillForwardEnumerator.Current.EndTime - fillForwardEnumerator.Current.Time);

            Assert.IsFalse(fillForwardEnumerator.MoveNext());
        }

        [Test]
        public void FillsForwardFromPreMarket()
        {
            var dataResolution = Time.OneMinute;
            var reference = new DateTime(2015, 6, 25, 9, 28, 0);
            var data = new []
            {
                new TradeBar
                {
                    Time = reference,
                    Value = 0,
                    Period = dataResolution
                },
                new TradeBar
                {
                    Time = reference.AddMinutes(4),
                    Value = 1,
                    Period = dataResolution
                }
            }.ToList();
            var enumerator = data.GetEnumerator();

            var exchange = new EquityExchange();
            var isExtendedMarketHours = false;
            var fillForwardEnumerator = new FillForwardEnumerator(enumerator, exchange, Ref.Create(TimeSpan.FromMinutes(1)), isExtendedMarketHours, data.Last().EndTime, dataResolution, exchange.TimeZone);

            // 9:29
            Assert.IsTrue(fillForwardEnumerator.MoveNext());
            Assert.AreEqual(reference.AddMinutes(1), fillForwardEnumerator.Current.EndTime);
            Assert.AreEqual(0, fillForwardEnumerator.Current.Value);
            Assert.IsFalse(fillForwardEnumerator.Current.IsFillForward);
            Assert.AreEqual(dataResolution, fillForwardEnumerator.Current.EndTime - fillForwardEnumerator.Current.Time);

            // 9:31 (ff)
            Assert.IsTrue(fillForwardEnumerator.MoveNext());
            Assert.AreEqual(reference.AddMinutes(3), fillForwardEnumerator.Current.EndTime);
            Assert.AreEqual(0, fillForwardEnumerator.Current.Value);
            Assert.IsTrue(fillForwardEnumerator.Current.IsFillForward);
            Assert.AreEqual(dataResolution, fillForwardEnumerator.Current.EndTime - fillForwardEnumerator.Current.Time);

            // 9:32 (ff)
            Assert.IsTrue(fillForwardEnumerator.MoveNext());
            Assert.AreEqual(reference.AddMinutes(4), fillForwardEnumerator.Current.EndTime);
            Assert.AreEqual(0, fillForwardEnumerator.Current.Value);
            Assert.IsTrue(fillForwardEnumerator.Current.IsFillForward);
            Assert.AreEqual(dataResolution, fillForwardEnumerator.Current.EndTime - fillForwardEnumerator.Current.Time);

            // 9:33
            Assert.IsTrue(fillForwardEnumerator.MoveNext());
            Assert.AreEqual(reference.AddMinutes(5), fillForwardEnumerator.Current.EndTime);
            Assert.AreEqual(1, fillForwardEnumerator.Current.Value);
            Assert.IsFalse(fillForwardEnumerator.Current.IsFillForward);
            Assert.AreEqual(dataResolution, fillForwardEnumerator.Current.EndTime - fillForwardEnumerator.Current.Time);

            Assert.IsFalse(fillForwardEnumerator.MoveNext());
        }

        [Test]
        public void FillsForwardFromPreMarketMinuteToSecond()
        {
            var dataResolution = Time.OneMinute;
            var reference = new DateTime(2011, 4, 26, 8, 39, 0);
            var data = new[]
            {
                new TradeBar
                {
                    Time = reference,
                    Value = 1,
                    Period = dataResolution
                },
                new TradeBar
                {
                    Time = reference.Date.Add(new TimeSpan(9, 30, 0)),
                    Value = 2,
                    Period = dataResolution
                }
            }.ToList();
            var enumerator = data.GetEnumerator();

            var exchange = new EquityExchange();
            const bool isExtendedMarketHours = false;
            var fillForwardEnumerator = new FillForwardEnumerator(enumerator, exchange, Ref.Create(TimeSpan.FromSeconds(1)), isExtendedMarketHours, data.Last().EndTime, dataResolution, exchange.TimeZone);

            // 8:40:00
            Assert.IsTrue(fillForwardEnumerator.MoveNext());
            Assert.AreEqual(reference.AddMinutes(1), fillForwardEnumerator.Current.EndTime);
            Assert.AreEqual(1, fillForwardEnumerator.Current.Value);
            Assert.IsFalse(fillForwardEnumerator.Current.IsFillForward);
            Assert.AreEqual(dataResolution, fillForwardEnumerator.Current.EndTime - fillForwardEnumerator.Current.Time);

            // 9:30:01 to 9:30:59 (ff)
            for (var i = 1; i < 60; i++)
            {
                Assert.IsTrue(fillForwardEnumerator.MoveNext());
                Assert.AreEqual(reference.Date.Add(new TimeSpan(9, 30, i)), fillForwardEnumerator.Current.EndTime);
                Assert.AreEqual(1, fillForwardEnumerator.Current.Value);
                Assert.IsTrue(fillForwardEnumerator.Current.IsFillForward);
                Assert.AreEqual(dataResolution, fillForwardEnumerator.Current.EndTime - fillForwardEnumerator.Current.Time);
            }

            // 9:31:00
            Assert.IsTrue(fillForwardEnumerator.MoveNext());
            Assert.AreEqual(reference.Date.Add(new TimeSpan(9, 31, 0)), fillForwardEnumerator.Current.EndTime);
            Assert.AreEqual(2, fillForwardEnumerator.Current.Value);
            Assert.IsFalse(fillForwardEnumerator.Current.IsFillForward);
            Assert.AreEqual(dataResolution, fillForwardEnumerator.Current.EndTime - fillForwardEnumerator.Current.Time);

            Assert.IsFalse(fillForwardEnumerator.MoveNext());
        }

        [Test]
        public void FillsForwardRestOfDay()
        {
            var dataResolution = Time.OneMinute;
            var reference = new DateTime(2015, 6, 25, 15, 57, 0);
            var data = Enumerable.Range(0, 1).Select(x => new TradeBar
            {
                Time = reference.AddMinutes(x*2),
                Value = x,
                Period = dataResolution
            }).ToList();
            var enumerator = data.GetEnumerator();

            var exchange = new EquityExchange();
            var isExtendedMarketHours = false;
            var fillForwardEnumerator = new FillForwardEnumerator(enumerator, exchange, Ref.Create(TimeSpan.FromMinutes(1)), isExtendedMarketHours, reference.AddMinutes(3), dataResolution, exchange.TimeZone);

            // 3:58
            Assert.IsTrue(fillForwardEnumerator.MoveNext());
            Assert.AreEqual(reference.AddMinutes(1), fillForwardEnumerator.Current.EndTime);
            Assert.AreEqual(0, fillForwardEnumerator.Current.Value);
            Assert.IsFalse(fillForwardEnumerator.Current.IsFillForward);
            Assert.AreEqual(dataResolution, fillForwardEnumerator.Current.EndTime - fillForwardEnumerator.Current.Time);

            // 3:59 (ff)
            Assert.IsTrue(fillForwardEnumerator.MoveNext());
            Assert.AreEqual(reference.AddMinutes(2), fillForwardEnumerator.Current.EndTime);
            Assert.AreEqual(0, fillForwardEnumerator.Current.Value);
            Assert.IsTrue(fillForwardEnumerator.Current.IsFillForward);
            Assert.AreEqual(dataResolution, fillForwardEnumerator.Current.EndTime - fillForwardEnumerator.Current.Time);

            // 4:00 (ff)
            Assert.IsTrue(fillForwardEnumerator.MoveNext());
            Assert.AreEqual(reference.AddMinutes(3), fillForwardEnumerator.Current.EndTime);
            Assert.AreEqual(0, fillForwardEnumerator.Current.Value);
            Assert.IsTrue(fillForwardEnumerator.Current.IsFillForward);
            Assert.AreEqual(dataResolution, fillForwardEnumerator.Current.EndTime - fillForwardEnumerator.Current.Time);

            Assert.IsFalse(fillForwardEnumerator.MoveNext());
        }

        [Test]
        public void FillsForwardEndOfSubscription()
        {
            var dataResolution = Time.OneMinute;
            var reference = new DateTime(2015, 6, 25, 15, 57, 0);
            var data = new[]
            {
                new TradeBar
                {
                    Time = reference,
                    Value = 0,
                    Period = dataResolution
                }
            }.ToList();

            var enumerator = data.GetEnumerator();

            var exchange = new EquityExchange();
            var isExtendedMarketHours = false;
            var fillForwardEnumerator = new FillForwardEnumerator(enumerator, exchange, Ref.Create(TimeSpan.FromMinutes(1)), isExtendedMarketHours, reference.AddMinutes(3), dataResolution, exchange.TimeZone);

            // 3:58
            Assert.IsTrue(fillForwardEnumerator.MoveNext());
            Assert.AreEqual(reference.AddMinutes(1), fillForwardEnumerator.Current.EndTime);
            Assert.AreEqual(0, fillForwardEnumerator.Current.Value);
            Assert.IsFalse(fillForwardEnumerator.Current.IsFillForward);
            Assert.AreEqual(dataResolution, fillForwardEnumerator.Current.EndTime - fillForwardEnumerator.Current.Time);

            // 3:59 (ff)
            Assert.IsTrue(fillForwardEnumerator.MoveNext());
            Assert.AreEqual(reference.AddMinutes(2), fillForwardEnumerator.Current.EndTime);
            Assert.AreEqual(0, fillForwardEnumerator.Current.Value);
            Assert.IsTrue(fillForwardEnumerator.Current.IsFillForward);
            Assert.AreEqual(dataResolution, fillForwardEnumerator.Current.EndTime - fillForwardEnumerator.Current.Time);

            // 4:00 (ff)
            Assert.IsTrue(fillForwardEnumerator.MoveNext());
            Assert.AreEqual(reference.AddMinutes(3), fillForwardEnumerator.Current.EndTime);
            Assert.AreEqual(0, fillForwardEnumerator.Current.Value);
            Assert.IsTrue(fillForwardEnumerator.Current.IsFillForward);
            Assert.AreEqual(dataResolution, fillForwardEnumerator.Current.EndTime - fillForwardEnumerator.Current.Time);

            Assert.IsFalse(fillForwardEnumerator.MoveNext());
        }

        [Test]
        public void FillsForwardGapBeforeEndOfSubscription()
        {
            var dataResolution = Time.OneMinute;
            var reference = new DateTime(2015, 6, 25, 15, 57, 0);
            var data = new[]
            {
                new TradeBar
                {
                    Time = reference,
                    Value = 0,
                    Period = dataResolution
                }
            }.ToList();
            var enumerator = data.GetEnumerator();

            var exchange = new EquityExchange();
            var isExtendedMarketHours = false;
            var fillForwardEnumerator = new FillForwardEnumerator(enumerator, exchange, Ref.Create(TimeSpan.FromMinutes(1)), isExtendedMarketHours, reference.Date.AddHours(16), dataResolution, exchange.TimeZone);

            // 3:58
            Assert.IsTrue(fillForwardEnumerator.MoveNext());
            Assert.AreEqual(reference.AddMinutes(1), fillForwardEnumerator.Current.EndTime);
            Assert.AreEqual(0, fillForwardEnumerator.Current.Value);
            Assert.IsFalse(fillForwardEnumerator.Current.IsFillForward);
            Assert.AreEqual(dataResolution, fillForwardEnumerator.Current.EndTime - fillForwardEnumerator.Current.Time);

            // 3:39 (ff)
            Assert.IsTrue(fillForwardEnumerator.MoveNext());
            Assert.AreEqual(reference.AddMinutes(2), fillForwardEnumerator.Current.EndTime);
            Assert.AreEqual(0, fillForwardEnumerator.Current.Value);
            Assert.IsTrue(fillForwardEnumerator.Current.IsFillForward);
            Assert.AreEqual(dataResolution, fillForwardEnumerator.Current.EndTime - fillForwardEnumerator.Current.Time);

            // 4:00 (ff)
            Assert.IsTrue(fillForwardEnumerator.MoveNext());
            Assert.AreEqual(reference.AddMinutes(3), fillForwardEnumerator.Current.EndTime);
            Assert.AreEqual(0, fillForwardEnumerator.Current.Value);
            Assert.IsTrue(fillForwardEnumerator.Current.IsFillForward);
            Assert.AreEqual(dataResolution, fillForwardEnumerator.Current.EndTime - fillForwardEnumerator.Current.Time);

            Assert.IsFalse(fillForwardEnumerator.MoveNext());
        }

        [Test]
        public void FillsForwardToNextDay()
        {
            var dataResolution = Time.OneHour;
            var reference = new DateTime(2015, 6, 25, 14, 0, 0);
            var end = reference.Date.AddDays(1).AddHours(10);
            var data = new []
            {
                new TradeBar
                {
                    Time = reference,
                    Value = 0,
                    Period = dataResolution
                },
                new TradeBar
                {
                    Time = end - dataResolution,
                    Value = 1,
                    Period = dataResolution
                }
            }.ToList();
            var enumerator = data.GetEnumerator();

            var exchange = new EquityExchange();
            bool isExtendedMarketHours = false;
            var fillForwardEnumerator = new FillForwardEnumerator(enumerator, exchange, Ref.Create(TimeSpan.FromHours(1)), isExtendedMarketHours, end, dataResolution, exchange.TimeZone);

            // 3:00
            Assert.IsTrue(fillForwardEnumerator.MoveNext());
            Assert.AreEqual(reference.AddHours(1), fillForwardEnumerator.Current.EndTime);
            Assert.AreEqual(0, fillForwardEnumerator.Current.Value);
            Assert.IsFalse(fillForwardEnumerator.Current.IsFillForward);
            Assert.AreEqual(dataResolution, fillForwardEnumerator.Current.EndTime - fillForwardEnumerator.Current.Time);

            // 4:00 (ff)
            Assert.IsTrue(fillForwardEnumerator.MoveNext());
            Assert.AreEqual(reference.AddHours(2), fillForwardEnumerator.Current.EndTime);
            Assert.AreEqual(0, fillForwardEnumerator.Current.Value);
            Assert.IsTrue(fillForwardEnumerator.Current.IsFillForward);
            Assert.AreEqual(dataResolution, fillForwardEnumerator.Current.EndTime - fillForwardEnumerator.Current.Time);

            // 10:00
            Assert.IsTrue(fillForwardEnumerator.MoveNext());
            Assert.AreEqual(end, fillForwardEnumerator.Current.EndTime);
            Assert.AreEqual(1, fillForwardEnumerator.Current.Value);
            Assert.IsFalse(fillForwardEnumerator.Current.IsFillForward);
            Assert.AreEqual(dataResolution, fillForwardEnumerator.Current.EndTime - fillForwardEnumerator.Current.Time);

            Assert.IsFalse(fillForwardEnumerator.MoveNext());
        }

        [Test]
        public void SkipsAfterMarketData()
        {
            var dataResolution = Time.OneHour;
            var reference = new DateTime(2015, 6, 25, 14, 0, 0);
            var end = reference.Date.AddDays(1).AddHours(10);
            var data = new BaseData[]
            {
                new TradeBar
                {
                    Time = reference,
                    Value = 0,
                    Period = dataResolution
                },
                new TradeBar
                {
                    Time = reference.AddHours(3),
                    Value = 1,
                    Period = dataResolution
                },
                new TradeBar
                {
                    Time = reference.Date.AddDays(1).AddHours(10) - dataResolution,
                    Value = 2,
                    Period = dataResolution
                }
            }.ToList();
            var enumerator = data.GetEnumerator();

            var exchange = new EquityExchange();
            bool isExtendedMarketHours = false;
            var fillForwardEnumerator = new FillForwardEnumerator(enumerator, exchange, Ref.Create(TimeSpan.FromHours(1)), isExtendedMarketHours, end, dataResolution, exchange.TimeZone);

            // 3:00
            Assert.IsTrue(fillForwardEnumerator.MoveNext());
            Assert.AreEqual(reference.AddHours(1), fillForwardEnumerator.Current.EndTime);
            Assert.AreEqual(0, fillForwardEnumerator.Current.Value);
            Assert.IsFalse(fillForwardEnumerator.Current.IsFillForward);
            Assert.AreEqual(dataResolution, fillForwardEnumerator.Current.EndTime - fillForwardEnumerator.Current.Time);

            // 4:00 (ff)
            Assert.IsTrue(fillForwardEnumerator.MoveNext());
            Assert.AreEqual(reference.AddHours(2), fillForwardEnumerator.Current.EndTime);
            Assert.AreEqual(0, fillForwardEnumerator.Current.Value);
            Assert.IsTrue(fillForwardEnumerator.Current.IsFillForward);
            Assert.AreEqual(dataResolution, fillForwardEnumerator.Current.EndTime - fillForwardEnumerator.Current.Time);

            // 6:00 - this is raw data, the FF enumerator doesn't try to perform filtering per se, just filtering on when to FF
            Assert.IsTrue(fillForwardEnumerator.MoveNext());
            Assert.AreEqual(reference.AddHours(4), fillForwardEnumerator.Current.EndTime);
            Assert.AreEqual(1, fillForwardEnumerator.Current.Value);
            Assert.IsFalse(fillForwardEnumerator.Current.IsFillForward);
            Assert.AreEqual(dataResolution, fillForwardEnumerator.Current.EndTime - fillForwardEnumerator.Current.Time);

            // 10:00
            Assert.IsTrue(fillForwardEnumerator.MoveNext());
            Assert.AreEqual(end, fillForwardEnumerator.Current.EndTime);
            Assert.AreEqual(2, fillForwardEnumerator.Current.Value);
            Assert.IsFalse(fillForwardEnumerator.Current.IsFillForward);
            Assert.AreEqual(dataResolution, fillForwardEnumerator.Current.EndTime - fillForwardEnumerator.Current.Time);

            Assert.IsFalse(fillForwardEnumerator.MoveNext());
        }

        [Test]
        public void FillsForwardDailyOnHoursInMarketHours()
        {
            var dataResolution = Time.OneDay;
            var reference = new DateTime(2015, 6, 25);
            var data = new BaseData[]
            {
                // thurs 6/25
                new TradeBar{Value = 0, Time = reference, Period = Time.OneDay},
                // fri 6/26
                new TradeBar{Value = 1, Time = reference.AddDays(1), Period = Time.OneDay},
            }.ToList();
            var enumerator = data.GetEnumerator();

            var exchange = new EquityExchange();
            bool isExtendedMarketHours = false;
            var fillForwardEnumerator = new FillForwardEnumerator(enumerator, exchange, Ref.Create(TimeSpan.FromHours(1)), isExtendedMarketHours, data.Last().EndTime, dataResolution, exchange.TimeZone);

            // 12:00am
            Assert.IsTrue(fillForwardEnumerator.MoveNext());
            Assert.AreEqual(reference.AddDays(1), fillForwardEnumerator.Current.EndTime);
            Assert.AreEqual(0, fillForwardEnumerator.Current.Value);
            Assert.IsFalse(fillForwardEnumerator.Current.IsFillForward);
            Assert.AreEqual(dataResolution, fillForwardEnumerator.Current.EndTime - fillForwardEnumerator.Current.Time);

            // 10:00 (ff)
            Assert.IsTrue(fillForwardEnumerator.MoveNext());
            Assert.AreEqual(reference.AddDays(1).AddHours(10), fillForwardEnumerator.Current.EndTime);
            Assert.AreEqual(0, fillForwardEnumerator.Current.Value);
            Assert.IsTrue(fillForwardEnumerator.Current.IsFillForward);
            Assert.AreEqual(dataResolution, fillForwardEnumerator.Current.EndTime - fillForwardEnumerator.Current.Time);

            // 11:00 (ff)
            Assert.IsTrue(fillForwardEnumerator.MoveNext());
            Assert.AreEqual(reference.AddDays(1).AddHours(11), fillForwardEnumerator.Current.EndTime);
            Assert.AreEqual(0, fillForwardEnumerator.Current.Value);
            Assert.IsTrue(fillForwardEnumerator.Current.IsFillForward);
            Assert.AreEqual(dataResolution, fillForwardEnumerator.Current.EndTime - fillForwardEnumerator.Current.Time);

            // 12:00pm (ff)
            Assert.IsTrue(fillForwardEnumerator.MoveNext());
            Assert.AreEqual(reference.AddDays(1).AddHours(12), fillForwardEnumerator.Current.EndTime);
            Assert.AreEqual(0, fillForwardEnumerator.Current.Value);
            Assert.IsTrue(fillForwardEnumerator.Current.IsFillForward);
            Assert.AreEqual(dataResolution, fillForwardEnumerator.Current.EndTime - fillForwardEnumerator.Current.Time);

            // 1:00 (ff)
            Assert.IsTrue(fillForwardEnumerator.MoveNext());
            Assert.AreEqual(reference.AddDays(1).AddHours(13), fillForwardEnumerator.Current.EndTime);
            Assert.AreEqual(0, fillForwardEnumerator.Current.Value);
            Assert.IsTrue(fillForwardEnumerator.Current.IsFillForward);
            Assert.AreEqual(dataResolution, fillForwardEnumerator.Current.EndTime - fillForwardEnumerator.Current.Time);

            // 2:00 (ff)
            Assert.IsTrue(fillForwardEnumerator.MoveNext());
            Assert.AreEqual(reference.AddDays(1).AddHours(14), fillForwardEnumerator.Current.EndTime);
            Assert.AreEqual(0, fillForwardEnumerator.Current.Value);
            Assert.IsTrue(fillForwardEnumerator.Current.IsFillForward);
            Assert.AreEqual(dataResolution, fillForwardEnumerator.Current.EndTime - fillForwardEnumerator.Current.Time);

            // 3:00 (ff)
            Assert.IsTrue(fillForwardEnumerator.MoveNext());
            Assert.AreEqual(reference.AddDays(1).AddHours(15), fillForwardEnumerator.Current.EndTime);
            Assert.AreEqual(0, fillForwardEnumerator.Current.Value);
            Assert.IsTrue(fillForwardEnumerator.Current.IsFillForward);
            Assert.AreEqual(dataResolution, fillForwardEnumerator.Current.EndTime - fillForwardEnumerator.Current.Time);

            // 4:00 (ff)
            Assert.IsTrue(fillForwardEnumerator.MoveNext());
            Assert.AreEqual(reference.AddDays(1).AddHours(16), fillForwardEnumerator.Current.EndTime);
            Assert.AreEqual(0, fillForwardEnumerator.Current.Value);
            Assert.IsTrue(fillForwardEnumerator.Current.IsFillForward);
            Assert.AreEqual(dataResolution, fillForwardEnumerator.Current.EndTime - fillForwardEnumerator.Current.Time);

            // 12:00am
            Assert.IsTrue(fillForwardEnumerator.MoveNext());
            Assert.AreEqual(reference.AddDays(2), fillForwardEnumerator.Current.EndTime);
            Assert.AreEqual(1, fillForwardEnumerator.Current.Value);
            Assert.IsFalse(fillForwardEnumerator.Current.IsFillForward);
            Assert.AreEqual(dataResolution, fillForwardEnumerator.Current.EndTime - fillForwardEnumerator.Current.Time);

            Assert.IsFalse(fillForwardEnumerator.MoveNext());
        }

        [Test]
        public void FillsForwardDailyMissingDays()
        {
            var dataResolution = Time.OneDay;
            var reference = new DateTime(2015, 6, 25);
            var data = new BaseData[]
            {
                // thurs 6/25
                new TradeBar{Value = 0, Time = reference, Period = Time.OneDay},
                // fri 6/26
                new TradeBar{Value = 1, Time = reference.AddDays(5), Period = Time.OneDay},
            }.ToList();
            var enumerator = data.GetEnumerator();

            var exchange = new EquityExchange();
            bool isExtendedMarketHours = false;
            var fillForwardEnumerator = new FillForwardEnumerator(enumerator, exchange, Ref.Create(TimeSpan.FromDays(1)), isExtendedMarketHours, data.Last().EndTime.AddDays(1), dataResolution, exchange.TimeZone);

            // 6/25
            Assert.IsTrue(fillForwardEnumerator.MoveNext());
            Assert.AreEqual(reference.AddDays(1), fillForwardEnumerator.Current.EndTime);
            Assert.AreEqual(0, fillForwardEnumerator.Current.Value);
            Assert.IsFalse(fillForwardEnumerator.Current.IsFillForward);
            Assert.IsTrue(((TradeBar)fillForwardEnumerator.Current).Period == dataResolution);

            // 6/26
            Assert.IsTrue(fillForwardEnumerator.MoveNext());
            Assert.AreEqual(reference.AddDays(2), fillForwardEnumerator.Current.EndTime);
            Assert.AreEqual(0, fillForwardEnumerator.Current.Value);
            Assert.IsTrue(fillForwardEnumerator.Current.IsFillForward);
            Assert.IsTrue(((TradeBar)fillForwardEnumerator.Current).Period == dataResolution);

            // 6/29
            Assert.IsTrue(fillForwardEnumerator.MoveNext());
            Assert.AreEqual(reference.AddDays(5), fillForwardEnumerator.Current.EndTime);
            Assert.AreEqual(0, fillForwardEnumerator.Current.Value);
            Assert.IsTrue(fillForwardEnumerator.Current.IsFillForward);
            Assert.IsTrue(((TradeBar)fillForwardEnumerator.Current).Period == dataResolution);

            // 6/30
            Assert.IsTrue(fillForwardEnumerator.MoveNext());
            Assert.AreEqual(reference.AddDays(6), fillForwardEnumerator.Current.EndTime);
            Assert.AreEqual(1, fillForwardEnumerator.Current.Value);
            Assert.IsFalse(fillForwardEnumerator.Current.IsFillForward);
            Assert.IsTrue(((TradeBar)fillForwardEnumerator.Current).Period == dataResolution);

            // 7/1
            Assert.IsTrue(fillForwardEnumerator.MoveNext());
            Assert.AreEqual(reference.AddDays(7), fillForwardEnumerator.Current.EndTime);
            Assert.AreEqual(1, fillForwardEnumerator.Current.Value);
            Assert.IsTrue(fillForwardEnumerator.Current.IsFillForward);
            Assert.IsTrue(((TradeBar)fillForwardEnumerator.Current).Period == dataResolution);

            Assert.IsFalse(fillForwardEnumerator.MoveNext());
        }

        [Test]
        public void FillForwardHoursAtEndOfDayByHalfHour()
        {
            var dataResolution = Time.OneHour;
            var reference = new DateTime(2015, 6, 25, 14, 0, 0);
            var data = new BaseData[]
            {
                // thurs 6/25
                new TradeBar{Value = 0, Time = reference, Period = dataResolution},
                // fri 6/26
                new TradeBar{Value = 1, Time = reference.Date.AddDays(1), Period = dataResolution},
            }.ToList();
            var enumerator = data.GetEnumerator();

            var exchange = new EquityExchange();
            bool isExtendedMarketHours = false;
            var ffResolution = TimeSpan.FromMinutes(30);
            var fillForwardEnumerator = new FillForwardEnumerator(enumerator, exchange, Ref.Create(ffResolution), isExtendedMarketHours, data.Last().EndTime, dataResolution, exchange.TimeZone);

            // 3:00
            Assert.IsTrue(fillForwardEnumerator.MoveNext());
            Assert.AreEqual(reference.AddHours(1), fillForwardEnumerator.Current.EndTime);
            Assert.AreEqual(0, fillForwardEnumerator.Current.Value);
            Assert.IsFalse(fillForwardEnumerator.Current.IsFillForward);

            // 3:30
            Assert.IsTrue(fillForwardEnumerator.MoveNext());
            Assert.AreEqual(reference.AddHours(1.5), fillForwardEnumerator.Current.EndTime);
            Assert.AreEqual(0, fillForwardEnumerator.Current.Value);
            Assert.IsTrue(fillForwardEnumerator.Current.IsFillForward);

            // 4:00
            Assert.IsTrue(fillForwardEnumerator.MoveNext());
            Assert.AreEqual(reference.AddHours(2), fillForwardEnumerator.Current.EndTime);
            Assert.AreEqual(0, fillForwardEnumerator.Current.Value);
            Assert.IsTrue(fillForwardEnumerator.Current.IsFillForward);

            // 12:00am
            Assert.IsTrue(fillForwardEnumerator.MoveNext());
            Assert.AreEqual(data.Last().EndTime, fillForwardEnumerator.Current.EndTime);
            Assert.AreEqual(1, fillForwardEnumerator.Current.Value);
            Assert.IsFalse(fillForwardEnumerator.Current.IsFillForward);

            Assert.IsFalse(fillForwardEnumerator.MoveNext());
        }

        [Test]
        public void FillsForwardHourlyOnMinutesBeginningOfDay()
        {
            var dataResolution = Time.OneHour;
            var reference = new DateTime(2015, 6, 25);
            var data = new BaseData[]
            {
                // thurs 6/25
                new TradeBar{Value = 0, Time = reference, Period = dataResolution},
                // fri 6/26
                new TradeBar{Value = 1, Time = reference.Date.AddHours(9), Period = dataResolution},
            }.ToList();
            var enumerator = data.GetEnumerator();

            var exchange = new EquityExchange();
            bool isExtendedMarketHours = false;
            var ffResolution = TimeSpan.FromMinutes(15);
            var fillForwardEnumerator = new FillForwardEnumerator(enumerator, exchange, Ref.Create(ffResolution), isExtendedMarketHours, data.Last().EndTime, dataResolution, exchange.TimeZone);

            // 12:00
            Assert.IsTrue(fillForwardEnumerator.MoveNext());
            Assert.AreEqual(reference.AddHours(1), fillForwardEnumerator.Current.EndTime);
            Assert.AreEqual(0, fillForwardEnumerator.Current.Value);
            Assert.IsFalse(fillForwardEnumerator.Current.IsFillForward);

            // 9:45 (ff)
            Assert.IsTrue(fillForwardEnumerator.MoveNext());
            Assert.AreEqual(reference.AddHours(9.75), fillForwardEnumerator.Current.EndTime);
            Assert.AreEqual(0, fillForwardEnumerator.Current.Value);
            Assert.IsTrue(fillForwardEnumerator.Current.IsFillForward);

            // 10:00
            Assert.IsTrue(fillForwardEnumerator.MoveNext());
            Assert.AreEqual(reference.AddHours(10), fillForwardEnumerator.Current.EndTime);
            Assert.AreEqual(1, fillForwardEnumerator.Current.Value);
            Assert.IsFalse(fillForwardEnumerator.Current.IsFillForward);

            Assert.IsFalse(fillForwardEnumerator.MoveNext());
        }

        [Test]
        public void FillsForwardMissingDaysOnFillForwardResolutionOfAnHour()
        {
            var dataResolution = Time.OneDay;
            var reference = new DateTime(2015, 6, 23);
            var data = new BaseData[]
            {
                // tues 6/23
                new TradeBar{Value = 0, Time = reference, Period = dataResolution},
                // wed 7/1
                new TradeBar{Value = 1, Time = reference.AddDays(8), Period = dataResolution},
            }.ToList();
            var enumerator = data.GetEnumerator();

            var exchange = new EquityExchange();
            bool isExtendedMarketHours = false;
            var ffResolution = TimeSpan.FromHours(1);
            var fillForwardEnumerator = new FillForwardEnumerator(enumerator, exchange, Ref.Create(ffResolution), isExtendedMarketHours, data.Last().EndTime, dataResolution, exchange.TimeZone);

            int dailyBars = 0;
            int hourlyBars = 0;
            while (fillForwardEnumerator.MoveNext())
            {
                Console.WriteLine(fillForwardEnumerator.Current.EndTime);
                if (fillForwardEnumerator.Current.Time.TimeOfDay == TimeSpan.Zero)
                {
                    dailyBars++;
                }
                else
                {
                    hourlyBars++;
                }
            }

            // we expect 7 daily bars here, beginning tues, wed, thurs, fri, mon, tues, wed
            Assert.AreEqual(7, dailyBars);

            // we expect 6 days worth of ff hourly bars at 7 bars a day
            Assert.AreEqual(42, hourlyBars);
        }

        [Test]
        public void OandaFillsForwardDailyForexOnWeekends()
        {
            var dailyBarsEmitted = 0;
            var fillForwardBars = new List<BaseData>();

            // 3 QuoteBars as they would be read from the EURUSD oanda daily file by QuoteBar.Reader()
            // The conversion from dataTimeZone to exchangeTimeZone has been done by hand
            // dataTimeZone == UTC
            /*
                20120719 00:00,1.22769,1.2324,1.22286,1.22759,0,1.22781,1.23253,1.22298,1.22771,0
                20120720 00:00,1.22757,1.22823,1.21435,1.21542,0,1.22769,1.22835,1.21449,1.21592,0
                20120722 00:00,1.21542,1.21542,1.21037,1.21271,0,1.21592,1.21592,1.21087,1.21283,0
                20120723 00:00,1.21273,1.21444,1.20669,1.21238,0,1.21285,1.21454,1.20685,1.21249,0
             */
            var data = new BaseData[]
            {
                // fri 7/20
                new QuoteBar{Value = 0, Time =  new DateTime(2012, 7, 19, 20, 0, 0), Period = Time.OneDay},
                // sunday 7/22
                new QuoteBar{Value = 1, Time = new DateTime(2012, 7, 21, 20, 0, 0), Period = Time.OneDay},
                // monday 7/23
                new QuoteBar{Value = 2, Time = new DateTime(2012, 7, 22, 20, 0, 0), Period = Time.OneDay},
            }.ToList();
            var enumerator = data.GetEnumerator();

            var market = Market.Oanda;
            var symbol = Symbol.Create("EURUSD", SecurityType.Forex, market);

            var marketHours = MarketHoursDatabase.FromDataFolder();
            var exchange = new ForexExchange(marketHours.GetExchangeHours(market, symbol, SecurityType.Forex));

            var fillForwardEnumerator = new FillForwardEnumerator(enumerator, exchange, Ref.Create(TimeSpan.FromDays(1)), false, data.Last().EndTime, Time.OneDay, TimeZones.Utc);

            while (fillForwardEnumerator.MoveNext())
            {
                fillForwardBars.Add(fillForwardEnumerator.Current);
                Console.WriteLine(fillForwardEnumerator.Current.Time.DayOfWeek + " " + fillForwardEnumerator.Current.Time + " - " + fillForwardEnumerator.Current.EndTime.DayOfWeek + " " + fillForwardEnumerator.Current.EndTime);
                dailyBarsEmitted++;
            }

            Assert.AreEqual(3, dailyBarsEmitted);
            Assert.AreEqual(new DateTime(2012, 7, 19, 20, 0, 0), fillForwardBars[0].Time);
            Assert.AreEqual(new DateTime(2012, 7, 21, 20, 0, 0), fillForwardBars[1].Time);
            Assert.AreEqual(new DateTime(2012, 7, 22, 20, 0, 0), fillForwardBars[2].Time);
        }
    }
}
