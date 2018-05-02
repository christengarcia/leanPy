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
using System.Linq;
using QuantConnect.Brokerages;
using QuantConnect.Data;
using QuantConnect.Orders;

namespace QuantConnect.Algorithm.CSharp
{
    /// <summary>
    /// This algorithm is a regression test case for CancelOpenOrders and rejected orders
    /// </summary>
    public class CancelOpenOrdersRegressionAlgorithm : QCAlgorithm
    {
        /// <summary>
        /// Initialise the data and resolution required, as well as the cash and start-end dates for your algorithm. All algorithms must initialized.
        /// </summary>
        public override void Initialize()
        {
            SetStartDate(2017, 9, 3);  //Set Start Date
            SetEndDate(2017, 9, 3);    //Set End Date
            SetCash(1000);             //Set Strategy Cash

            SetBrokerageModel(BrokerageName.GDAX, AccountType.Cash);

            AddCrypto("BTCUSD");
            AddCrypto("ETHUSD");
        }

        /// <summary>
        /// OnData event is the primary entry point for your algorithm. Each new data point will be pumped in here.
        /// </summary>
        /// <param name="data">Slice object keyed by symbol containing the stock data</param>
        public override void OnData(Slice data)
        {
            if (UtcTime.Hour != 6) return;

            if (UtcTime.Minute == 0)
            {
                // this order will be rejected for insufficient funds
                LimitOrder("BTCUSD", 100m, 4734.64m);
                LimitOrder("ETHUSD", 1.35505027m, 368.8m);
            }
            else if (UtcTime.Minute == 6)
            {
                Transactions.CancelOpenOrders("BTCUSD");
                LimitOrder("BTCUSD", 0.10576312m, 4727.61m);
            }
            else if (UtcTime.Minute == 12)
            {
                Transactions.CancelOpenOrders("BTCUSD");
                LimitOrder("BTCUSD", 0.10576267m, 4727.63m);
            }
            else if (UtcTime.Minute == 18)
            {
                Transactions.CancelOpenOrders("BTCUSD");
                LimitOrder("BTCUSD", 0.10547724m, 4740.42m);
            }
            else if (UtcTime.Minute == 24)
            {
                Transactions.CancelOpenOrders("BTCUSD");
            }
        }

        /// <summary>
        /// Order fill event handler. On an order fill update the resulting information is passed to this method.
        /// </summary>
        /// <param name="orderEvent">Order event details containing details of the events</param>
        public override void OnOrderEvent(OrderEvent orderEvent)
        {
            Debug(orderEvent.ToString());
        }

        /// <summary>
        /// End of algorithm run event handler. This method is called at the end of a backtest or live trading operation.
        /// </summary>
        public override void OnEndOfAlgorithm()
        {
            const int expectedOrders = 5;
            var expectedStatus = new[] { OrderStatus.Invalid, OrderStatus.Filled, OrderStatus.Canceled, OrderStatus.Canceled, OrderStatus.Filled };

            var orders = Transactions.GetOrders(x => true).ToList();

            if (orders.Count != expectedOrders)
            {
                throw new Exception($"Expected orders: {expectedOrders}, actual orders: {orders.Count}");
            }

            for (var i = 0; i < expectedOrders; i++)
            {
                var order = orders[i];
                if (order.Status != expectedStatus[i])
                {
                    throw new Exception($"Invalid status for order {order.Id}, Expected: {expectedStatus[i]}, actual: {order.Status}");
                }
            }
        }
    }
}