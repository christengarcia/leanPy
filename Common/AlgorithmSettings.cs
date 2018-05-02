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

using QuantConnect.Interfaces;

namespace QuantConnect
{
    /// <summary>
    /// This class includes user settings for the algorithm which can be changed in the <see cref="IAlgorithm.Initialize"/> method
    /// </summary>
    public class AlgorithmSettings
    {
        /// <summary>
        /// Gets/sets the maximum number of concurrent market data subscriptions available
        /// </summary>
        /// <remarks>
        /// All securities added with <see cref="IAlgorithm.AddSecurity"/> are counted as one,
        /// with the exception of options and futures where every single contract in a chain counts as one.
        /// </remarks>
        public int DataSubscriptionLimit { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="AlgorithmSettings"/> class
        /// </summary>
        public AlgorithmSettings()
        {
            // default is unlimited
            DataSubscriptionLimit = int.MaxValue;
        }
    }
}
