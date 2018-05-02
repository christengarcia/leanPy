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

using System.Linq;
using Moq;
using NUnit.Framework;
using QuantConnect.Algorithm.Framework.Portfolio;
using QuantConnect.Data.Market;
using QuantConnect.Securities;
using QuantConnect.Tests.Engine;

namespace QuantConnect.Tests.Algorithm.Framework.Portfolio
{
    [TestFixture]
    public class PortfolioTargetTests
    {
        [Test]
        public void PercentInvokesBuyingPowerModelAndAddsInExistingHoldings()
        {
            const decimal bpmQuantity = 100;
            const decimal holdings = 50;
            const decimal targetPercent = 0.5m;

            var algorithm = PerformanceBenchmarkAlgorithms.SingleSecurity_Second;
            algorithm.Initialize();
            algorithm.PostInitialize();
            var security = algorithm.Securities.Single().Value;
            security.SetMarketPrice(new Tick{Value = 1m});
            security.Holdings.SetHoldings(1m, holdings);
            var tpv = algorithm.Portfolio.TotalPortfolioValue;

            var buyingPowerMock = new Mock<IBuyingPowerModel>();
            buyingPowerMock.Setup(bpm => bpm.GetMaximumOrderQuantityForTargetValue(algorithm.Portfolio, security, targetPercent * tpv))
                .Returns(new GetMaximumOrderQuantityForTargetValueResult(bpmQuantity, null, false));
            security.BuyingPowerModel = buyingPowerMock.Object;

            var target = PortfolioTarget.Percent(algorithm, security.Symbol, targetPercent);

            Assert.AreEqual(security.Symbol, target.Symbol);
            Assert.AreEqual(bpmQuantity + holdings, target.Quantity);
        }
    }
}
