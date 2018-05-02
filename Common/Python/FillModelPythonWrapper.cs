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
using QuantConnect.Orders;
using QuantConnect.Orders.Fills;
using QuantConnect.Securities;

namespace QuantConnect.Python
{
    /// <summary>
    /// Wraps a <see cref="PyObject"/> object that represents a model that simulates order fill events
    /// </summary>
    public class FillModelPythonWrapper : IFillModel
    {
        private readonly dynamic _model;

        /// <summary>
        /// Constructor for initialising the <see cref="FillModelPythonWrapper"/> class with wrapped <see cref="PyObject"/> object
        /// </summary>
        /// <param name="model">Represents a model that simulates order fill events</param>
        public FillModelPythonWrapper(PyObject model)
        {
            _model = model;
        }

        /// <summary>
        /// Limit Fill Model. Return an order event with the fill details.
        /// </summary>
        /// <param name="asset">Stock Object to use to help model limit fill</param>
        /// <param name="order">Order to fill. Alter the values directly if filled.</param>
        /// <returns>Order fill information detailing the average price and quantity filled.</returns>
        public OrderEvent LimitFill(Security asset, LimitOrder order)
        {
            using (Py.GIL())
            {
                return _model.LimitFill(asset, order);
            }
        }

        /// <summary>
        /// Model the slippage on a market order: fixed percentage of order price
        /// </summary>
        /// <param name="asset">Asset we're trading this order</param>
        /// <param name="order">Order to update</param>
        /// <returns>Order fill information detailing the average price and quantity filled.</returns>
        public OrderEvent MarketFill(Security asset, MarketOrder order)
        {
            using (Py.GIL())
            {
                return _model.MarketFill(asset, order);
            }
        }

        /// <summary>
        /// Market on Close Fill Model. Return an order event with the fill details
        /// </summary>
        /// <param name="asset">Asset we're trading with this order</param>
        /// <param name="order">Order to be filled</param>
        /// <returns>Order fill information detailing the average price and quantity filled.</returns>
        public OrderEvent MarketOnCloseFill(Security asset, MarketOnCloseOrder order)
        {
            using (Py.GIL())
            {
                return _model.MarketOnCloseFill(asset, order);
            }
        }

        /// <summary>
        /// Market on Open Fill Model. Return an order event with the fill details
        /// </summary>
        /// <param name="asset">Asset we're trading with this order</param>
        /// <param name="order">Order to be filled</param>
        /// <returns>Order fill information detailing the average price and quantity filled.</returns>
        public OrderEvent MarketOnOpenFill(Security asset, MarketOnOpenOrder order)
        {
            using (Py.GIL())
            {
                return _model.MarketOnOpenFill(asset, order);
            }
        }

        /// <summary>
        /// Stop Limit Fill Model. Return an order event with the fill details.
        /// </summary>
        /// <param name="asset">Asset we're trading this order</param>
        /// <param name="order">Stop Limit Order to Check, return filled if true</param>
        /// <returns>Order fill information detailing the average price and quantity filled.</returns>
        public OrderEvent StopLimitFill(Security asset, StopLimitOrder order)
        {
            using (Py.GIL())
            {
                return _model.StopLimitFill(asset, order);
            }
        }

        /// <summary>
        /// Stop Market Fill Model. Return an order event with the fill details.
        /// </summary>
        /// <param name="asset">Asset we're trading this order</param>
        /// <param name="order">Stop Order to Check, return filled if true</param>
        /// <returns>Order fill information detailing the average price and quantity filled.</returns>
        public OrderEvent StopMarketFill(Security asset, StopMarketOrder order)
        {
            using (Py.GIL())
            {
                return _model.StopMarketFill(asset, order);
            }
        }
    }
}