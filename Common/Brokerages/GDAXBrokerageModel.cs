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
using System.Collections.Generic;
using QuantConnect.Orders;
using QuantConnect.Securities;
using QuantConnect.Orders.Fills;
using QuantConnect.Orders.Fees;
using System.Linq;

namespace QuantConnect.Brokerages
{
    /// <summary>
    /// Provides GDAX specific properties
    /// </summary>
    public class GDAXBrokerageModel : DefaultBrokerageModel
    {
        private static BrokerageMessageEvent _message = new BrokerageMessageEvent(BrokerageMessageType.Warning, 0, "Brokerage does not support update. You must cancel and re-create instead.");

        // https://support.gdax.com/customer/portal/articles/2725970-trading-rules
        private static readonly Dictionary<string, decimal> MinimumOrderSizes = new Dictionary<string, decimal>
        {
            { "BTCUSD", 0.001m },
            { "BTCEUR", 0.001m },
            { "BTCGBP", 0.001m },

            { "BCHUSD", 0.01m },
            { "BCHEUR", 0.01m },
            { "BCHBTC", 0.01m },

            { "ETHUSD", 0.01m },
            { "ETHEUR", 0.01m },
            { "ETHGBP", 0.01m },
            { "ETHBTC", 0.01m },

            { "LTCUSD", 0.1m },
            { "LTCEUR", 0.1m },
            { "LTCGBP", 0.1m },
            { "LTCBTC", 0.1m }
        };

        /// <summary>
        /// Initializes a new instance of the <see cref="GDAXBrokerageModel"/> class
        /// </summary>
        /// <param name="accountType">The type of account to be modelled, defaults to
        /// <see cref="AccountType.Cash"/></param>
        public GDAXBrokerageModel(AccountType accountType = AccountType.Cash)
            : base(accountType)
        {
            if (accountType == AccountType.Margin)
            {
                throw new Exception("The GDAX brokerage does not currently support Margin trading.");
            }
        }

        /// <summary>
        /// GDAX global leverage rule
        /// </summary>
        /// <param name="security"></param>
        /// <returns></returns>
        public override decimal GetLeverage(Security security)
        {
            // margin trading is not currently supported by GDAX
            return 1m;
        }

        /// <summary>
        /// Provides GDAX fee model
        /// </summary>
        /// <param name="security"></param>
        /// <returns></returns>
        public override IFeeModel GetFeeModel(Security security)
        {
            return new GDAXFeeModel();
        }

        /// <summary>
        /// Gdax does no support update of orders
        /// </summary>
        /// <param name="security"></param>
        /// <param name="order"></param>
        /// <param name="request"></param>
        /// <param name="message"></param>
        /// <returns></returns>
        public override bool CanUpdateOrder(Security security, Order order, UpdateOrderRequest request, out BrokerageMessageEvent message)
        {
            message = _message;
            return false;
        }

        /// <summary>
        /// Evaluates whether exchange will accept order. Will reject order update
        /// </summary>
        /// <param name="security"></param>
        /// <param name="order"></param>
        /// <param name="message"></param>
        /// <returns></returns>
        public override bool CanSubmitOrder(Security security, Order order, out BrokerageMessageEvent message)
        {
            if (order.BrokerId != null && order.BrokerId.Any())
            {
                message = _message;
                return false;
            }

            decimal minimumOrderSize;
            if (MinimumOrderSizes.TryGetValue(security.Symbol.Value, out minimumOrderSize) &&
                Math.Abs(order.Quantity) < minimumOrderSize)
            {
                message = new BrokerageMessageEvent(BrokerageMessageType.Warning, "NotSupported",
                    $"The minimum order quantity for {security.Symbol.Value} is {minimumOrderSize}"
                );

                return false;
            }

            if (security.Type != SecurityType.Crypto)
            {
                message = new BrokerageMessageEvent(BrokerageMessageType.Warning, "NotSupported",
                    "This model does not support " + security.Type + " security type."
                );

                return false;
            }

            if (order.Type != OrderType.Limit && order.Type != OrderType.Market && order.Type != OrderType.StopMarket)
            {
                message = new BrokerageMessageEvent(BrokerageMessageType.Warning, "NotSupported",
                    "This model does not support " + order.Type + " order type."
                );

                return false;
            }

            return base.CanSubmitOrder(security, order, out message);
        }

        /// <summary>
        /// GDAX fills order using the latest Trade or Quote data
        /// </summary>
        /// <param name="security">The security to get fill model for</param>
        /// <returns>The new fill model for this brokerage</returns>
        public override IFillModel GetFillModel(Security security)
        {
            return new LatestPriceFillModel();
        }

        /// <summary>
        /// Gets a new buying power model for the security, returning the default model with the security's configured leverage.
        /// For cash accounts, leverage = 1 is used.
        /// </summary>
        /// <param name="security">The security to get a buying power model for</param>
        /// <param name="accountType">The account type</param>
        /// <returns>The buying power model for this brokerage/security</returns>
        public override IBuyingPowerModel GetBuyingPowerModel(Security security, AccountType accountType)
        {
            // margin trading is not currently supported by GDAX
            return new CashBuyingPowerModel();
        }
    }
}