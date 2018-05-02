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

using NUnit.Framework;
using QuantConnect.Indicators;
using System;

namespace QuantConnect.Tests.Indicators
{
    /// <summary>
    /// Result tested vs. Python available at: http://tinyurl.com/o7redso
    /// </summary>
    [TestFixture]
    public class RegressionChannelTest
    {
        [Test]
        public void RegressionChannelComputesCorrectly()
        {
            var period = 20;
            var indicator = new RegressionChannel(period, 2);
            var stdDev = new StandardDeviation(period);
            var time = DateTime.Now;

            var prices = LeastSquaresMovingAverageTest.prices;
            var expected = LeastSquaresMovingAverageTest.expected;

            var actual = new decimal[prices.Length];

            for (int i = 0; i < prices.Length; i++)
            {
                indicator.Update(time, prices[i]);
                stdDev.Update(time, prices[i]);
                actual[i] = Math.Round(indicator.Current.Value, 4);
                time = time.AddMinutes(1);
            }
            Assert.AreEqual(expected, actual);

            var expectedUpper = indicator.Current + stdDev.Current * 2;
            Assert.AreEqual(expectedUpper, indicator.UpperChannel);
            var expectedLower = indicator.Current - stdDev.Current * 2;
            Assert.AreEqual(expectedLower, indicator.LowerChannel);
        }

        [Test]
        public void ResetsProperly()
        {
            var period = 10;
            var time = DateTime.Now;

            var indicator = new RegressionChannel(period, 2);

            for (int i = 0; i < period + 1; i++)
            {
                indicator.Update(time, 1m);
                time.AddMinutes(1);
            }
            Assert.IsTrue(indicator.IsReady, "Regression Channel ready");
            indicator.Reset();
            TestHelper.AssertIndicatorIsInDefaultState(indicator);
        }
    }
}