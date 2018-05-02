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
using Newtonsoft.Json;
using NUnit.Framework;
using QuantConnect.Algorithm.Framework.Alphas;

namespace QuantConnect.Tests.Algorithm.Framework
{
    [TestFixture]
    public class InsightTests
    {
        [Test]
        public void HasReferenceTypeEqualitySemantics()
        {
            var one = Insight.Price(Symbols.SPY, Time.OneSecond, InsightDirection.Up);
            var two = Insight.Price(Symbols.SPY, Time.OneSecond, InsightDirection.Up);
            Assert.AreNotEqual(one, two);
            Assert.AreEqual(one, one);
            Assert.AreEqual(two, two);
        }

        [Test]
        public void SurvivesRoundTripSerializationUsingJsonConvert()
        {
            var time = new DateTime(2000, 01, 02, 03, 04, 05, 06);
            var insight = new Insight(time, Symbols.SPY, Time.OneMinute, InsightType.Volatility, InsightDirection.Up, 1, 2, "source-model");
            Insight.Group(insight);
            var serialized = JsonConvert.SerializeObject(insight);
            var deserialized = JsonConvert.DeserializeObject<Insight>(serialized);

            Assert.AreEqual(insight.CloseTimeUtc, deserialized.CloseTimeUtc);
            Assert.AreEqual(insight.Confidence, deserialized.Confidence);
            Assert.AreEqual(insight.Direction, deserialized.Direction);
            Assert.AreEqual(insight.EstimatedValue, deserialized.EstimatedValue);
            Assert.AreEqual(insight.GeneratedTimeUtc, deserialized.GeneratedTimeUtc);
            Assert.AreEqual(insight.GroupId, deserialized.GroupId);
            Assert.AreEqual(insight.Id, deserialized.Id);
            Assert.AreEqual(insight.Magnitude, deserialized.Magnitude);
            Assert.AreEqual(insight.Period, deserialized.Period);
            Assert.AreEqual(insight.SourceModel, deserialized.SourceModel);
            Assert.AreEqual(insight.Score.Direction, deserialized.Score.Direction);
            Assert.AreEqual(insight.Score.Magnitude, deserialized.Score.Magnitude);
            Assert.AreEqual(insight.Score.UpdatedTimeUtc, deserialized.Score.UpdatedTimeUtc);
            Assert.AreEqual(insight.Score.IsFinalScore, deserialized.Score.IsFinalScore);
            Assert.AreEqual(insight.Symbol, deserialized.Symbol);
            Assert.AreEqual(insight.Type, deserialized.Type);
        }

        [Test]
        public void SurvivesRoundTripCopy()
        {
            var time = new DateTime(2000, 01, 02, 03, 04, 05, 06);
            var original = new Insight(time, Symbols.SPY, Time.OneMinute, InsightType.Volatility, InsightDirection.Up, 1, 2, "source-model");
            Insight.Group(original);

            var copy = original.Clone();

            Assert.AreEqual(original.CloseTimeUtc, copy.CloseTimeUtc);
            Assert.AreEqual(original.Confidence, copy.Confidence);
            Assert.AreEqual(original.Direction, copy.Direction);
            Assert.AreEqual(original.EstimatedValue, copy.EstimatedValue);
            Assert.AreEqual(original.GeneratedTimeUtc, copy.GeneratedTimeUtc);
            Assert.AreEqual(original.GroupId, copy.GroupId);
            Assert.AreEqual(original.Id, copy.Id);
            Assert.AreEqual(original.Magnitude, copy.Magnitude);
            Assert.AreEqual(original.Period, copy.Period);
            Assert.AreEqual(original.SourceModel, copy.SourceModel);
            Assert.AreEqual(original.Score.Direction, copy.Score.Direction);
            Assert.AreEqual(original.Score.Magnitude, copy.Score.Magnitude);
            Assert.AreEqual(original.Score.UpdatedTimeUtc, copy.Score.UpdatedTimeUtc);
            Assert.AreEqual(original.Score.IsFinalScore, copy.Score.IsFinalScore);
            Assert.AreEqual(original.Symbol, copy.Symbol);
            Assert.AreEqual(original.Type, copy.Type);
        }

        [Test]
        public void GroupAssignsGroupId()
        {
            var insight1 = Insight.Price(Symbols.SPY, Time.OneMinute, InsightDirection.Up);
            var insight2 = Insight.Price(Symbols.SPY, Time.OneMinute, InsightDirection.Up);
            var groupId = Insight.Group(insight1, insight2);
            Assert.AreEqual(groupId, insight1.GroupId);
            Assert.AreEqual(groupId, insight2.GroupId);
        }

        [Test]
        public void GroupThrowsExceptionIfInsightAlreadyHasGroupId()
        {
            var insight1 = Insight.Price(Symbols.SPY, Time.OneMinute, InsightDirection.Up);
            Insight.Group(insight1);
            Assert.That(() => Insight.Group(insight1), Throws.InvalidOperationException);
        }
    }
}
