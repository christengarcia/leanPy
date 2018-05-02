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

using NUnit.Framework;
using Python.Runtime;
using System;
using System.IO;

namespace QuantConnect.Tests.Python
{
    [TestFixture, Ignore]
    public class MethodOverloadTests
    {
        private dynamic _algorithm;

        /// <summary>
        /// Run before every test
        /// </summary>
        [SetUp]
        public void Setup()
        {
            var pythonPath = new DirectoryInfo("RegressionAlgorithms");
            Environment.SetEnvironmentVariable("PYTHONPATH", pythonPath.FullName);

            using (Py.GIL())
            {
                var module = Py.Import("Test_MethodOverload");
                _algorithm = module.GetAttr("Test_MethodOverload").Invoke();
                _algorithm.Initialize();
            }
        }

        [Test]
        public void CallPlotTests()
        {
            // self.Plot('NUMBER', 0.1)
            Assert.DoesNotThrow(() => _algorithm.call_plot_number_test());

            // self.Plot('STD', self.std), where self.sma = self.SMA('SPY', 20)
            Assert.DoesNotThrow(() => _algorithm.call_plot_sma_test());

            // self.Plot('SMA', self.sma), where self.std = self.STD('SPY', 20)
            Assert.DoesNotThrow(() => _algorithm.call_plot_std_test());

            // self.Plot("ERROR", self.Name), where self.Name is IAlgorithm.Name: string
            Assert.Throws<PythonException>(() => _algorithm.call_plot_throw_test());

            // self.Plot("ERROR", self.Portfolio), where self.Portfolio is IAlgorithm.Portfolio: instance of SecurityPortfolioManager
            Assert.Throws<PythonException>(() => _algorithm.call_plot_throw_managed_test());

            // self.Plot("ERROR", self.a), where self.a is an instance of a python object
            Assert.Throws<PythonException>(() => _algorithm.call_plot_throw_pyobject_test());
        }
    }
}