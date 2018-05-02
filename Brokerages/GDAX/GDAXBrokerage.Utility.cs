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

using RestSharp;
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace QuantConnect.Brokerages.GDAX
{
    /// <summary>
    /// Utility methods for GDAX brokerage
    /// </summary>
    public partial class GDAXBrokerage
    {
        /// <summary>
        /// Sign Header
        /// </summary>
        public const string SignHeader = "CB-ACCESS-SIGN";
        /// <summary>
        /// Key Header
        /// </summary>
        public const string KeyHeader = "CB-ACCESS-KEY";
        /// <summary>
        /// Timestamp Header
        /// </summary>
        public const string TimeHeader = "CB-ACCESS-TIMESTAMP";
        /// <summary>
        /// Passphrase header
        /// </summary>
        public const string PassHeader = "CB-ACCESS-PASSPHRASE";
        private const string Open = "open";
        private const string Pending = "pending";
        private const string Active = "active";
        private const string Done = "done";
        private const string Settled = "settled";

        /// <summary>
        /// Creates an auth token and adds to the request
        /// </summary>
        /// <param name="request">the rest request</param>
        /// <returns>a token representing the request params</returns>
        public AuthenticationToken GetAuthenticationToken(IRestRequest request)
        {
            var body = request.Parameters.SingleOrDefault(b => b.Type == ParameterType.RequestBody);
            var token = GetAuthenticationToken(body == null ? "" : body.Value.ToString(), request.Method.ToString().ToUpper(), request.Resource);

            request.AddHeader(SignHeader, token.Signature);
            request.AddHeader(KeyHeader, ApiKey);
            request.AddHeader(TimeHeader, token.Timestamp);
            request.AddHeader(PassHeader, _passPhrase);

            return token;
        }

        /// <summary>
        /// Creates an auth token to sign a request
        /// </summary>
        /// <param name="body">the request body as json</param>
        /// <param name="method">the http method</param>
        /// <param name="url">the request url</param>
        /// <returns></returns>
        public AuthenticationToken GetAuthenticationToken(string body, string method, string url)
        {
            var token = new AuthenticationToken
            {
                Key = ApiKey,
                Passphrase = _passPhrase,
                //todo: query time server to correct for time skew
                Timestamp = Time.DateTimeToUnixTimeStamp(DateTime.UtcNow).ToString(System.Globalization.CultureInfo.InvariantCulture)
            };

            byte[] data = Convert.FromBase64String(ApiSecret);
            var prehash = token.Timestamp + method + url + body;

            byte[] bytes = Encoding.UTF8.GetBytes(prehash);
            using (var hmac = new HMACSHA256(data))
            {
                byte[] hash = hmac.ComputeHash(bytes);
                token.Signature = Convert.ToBase64String(hash);
            }

            return token;
        }

        private static string ConvertOrderType(Orders.OrderType orderType)
        {
            if (orderType == Orders.OrderType.Limit || orderType == Orders.OrderType.Market)
            {
                return orderType.ToString().ToLower();
            }
            else if (orderType == Orders.OrderType.StopMarket)
            {
                return "stop";
            }

            throw new NotSupportedException("GDAXBrokerage.ConvertOrderType: Unsupported order type:" + orderType.ToString());
        }

        /// <summary>
        /// Converts a product id to a symbol
        /// </summary>
        /// <param name="productId">gdax format product id</param>
        /// <returns>Symbol</returns>
        public static Symbol ConvertProductId(string productId)
        {
            return Symbol.Create(productId.Replace("-", ""), SecurityType.Crypto, Market.GDAX);
        }

        /// <summary>
        /// Converts a symbol to a product id
        /// </summary>
        /// <param name="symbol">Th symbol</param>
        /// <returns>gdax product id</returns>
        protected static string ConvertSymbol(Symbol symbol)
        {
            return symbol.Value.Substring(0, 3).ToUpper() + "-" + symbol.Value.Substring(3, 3).ToUpper();
        }

        private static Orders.OrderStatus ConvertOrderStatus(Messages.Order order)
        {
            if (order.FilledSize != 0 && order.FilledSize != order.Size)
            {
                return Orders.OrderStatus.PartiallyFilled;
            }
            else if (order.Status == Open || order.Status == Pending || order.Status == Active)
            {
                return Orders.OrderStatus.Submitted;
            }
            else if (order.Status == Done || order.Status == Settled)
            {
                return Orders.OrderStatus.Filled;
            }

            return Orders.OrderStatus.None;
        }

        private IRestResponse ExecuteRestRequest(IRestRequest request, GdaxEndpointType endpointType)
        {
            const int maxAttempts = 10;
            var attempts = 0;
            IRestResponse response;

            do
            {
                if (endpointType == GdaxEndpointType.Private)
                {
                    _privateEndpointRateLimiter.WaitToProceed();
                }
                else
                {
                    _publicEndpointRateLimiter.WaitToProceed();
                }

                response = RestClient.Execute(request);
                // 429 status code: Too Many Requests
            } while (++attempts < maxAttempts && (int) response.StatusCode == 429);

            return response;
        }
    }
}
