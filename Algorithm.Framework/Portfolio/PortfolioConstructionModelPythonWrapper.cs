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

using Python.Runtime;
using QuantConnect.Algorithm.Framework.Alphas;
using QuantConnect.Data.UniverseSelection;
using System;
using System.Collections.Generic;

namespace QuantConnect.Algorithm.Framework.Portfolio
{
    /// <summary>
    /// Provides an implementation of <see cref="IPortfolioConstructionModel"/> that wraps a <see cref="PyObject"/> object
    /// </summary>
    public class PortfolioConstructionModelPythonWrapper : IPortfolioConstructionModel
    {
        private readonly dynamic _model;

        /// <summary>
        /// Constructor for initialising the <see cref="IPortfolioConstructionModel"/> class with wrapped <see cref="PyObject"/> object
        /// </summary>
        /// <param name="model">Model defining how to build a portoflio from alphas</param>
        public PortfolioConstructionModelPythonWrapper(PyObject model)
        {
            using (Py.GIL())
            {
                foreach (var attributeName in new[] { "CreateTargets", "OnSecuritiesChanged" })
                {
                    if (!model.HasAttr(attributeName))
                    {
                        throw new NotImplementedException($"IPortfolioConstructionModel.{attributeName} must be implemented. Please implement this missing method on {model.GetPythonType()}");
                    }
                }
            }
            _model = model;
        }

        /// <summary>
        /// Create portfolio targets from the specified insights
        /// </summary>
        /// <param name="algorithm">The algorithm instance</param>
        /// <param name="insights">The insights to create portoflio targets from</param>
        /// <returns>An enumerable of portfolio targets to be sent to the execution model</returns>
        public IEnumerable<IPortfolioTarget> CreateTargets(QCAlgorithmFramework algorithm, Insight[] insights)
        {
            using (Py.GIL())
            {
                var targets = _model.CreateTargets(algorithm, insights) as PyObject;
                foreach (PyObject target in targets)
                {
                    yield return target.AsManagedObject(typeof(IPortfolioTarget)) as IPortfolioTarget;
                }
                targets.Destroy();
            }
        }

        /// <summary>
        /// Event fired each time the we add/remove securities from the data feed
        /// </summary>
        /// <param name="algorithm">The algorithm instance that experienced the change in securities</param>
        /// <param name="changes">The security additions and removals from the algorithm</param>
        public void OnSecuritiesChanged(QCAlgorithmFramework algorithm, SecurityChanges changes)
        {
            using (Py.GIL())
            {
                _model.OnSecuritiesChanged(algorithm, changes);
            }
        }
    }
}