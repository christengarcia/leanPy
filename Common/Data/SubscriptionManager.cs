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
using System.Linq;
using NodaTime;
using QuantConnect.Data.Auxiliary;
using QuantConnect.Data.Consolidators;
using QuantConnect.Data.Market;
using QuantConnect.Logging;
using QuantConnect.Util;

namespace QuantConnect.Data
{
    /// <summary>
    /// Enumerable Subscription Management Class
    /// </summary>
    public class SubscriptionManager
    {
        private readonly AlgorithmSettings _algorithmSettings;
        private readonly TimeKeeper _timeKeeper;

        /// Generic Market Data Requested and Object[] Arguements to Get it:
        public HashSet<SubscriptionDataConfig> Subscriptions;

        /// <summary>
        /// Flags the existence of custom data in the subscriptions
        /// </summary>
        public bool HasCustomData { get; set; }

        /// <summary>
        ///
        /// </summary>
        public Dictionary<SecurityType, List<TickType>> AvailableDataTypes
        {
            get;
            private set;
        }

        /// <summary>
        /// Initialise the Generic Data Manager Class
        /// </summary>
        /// <param name="algorithmSettings">The algorithm settings instance</param>
        /// <param name="timeKeeper">The algorithm's time keeper</param>
        public SubscriptionManager(AlgorithmSettings algorithmSettings, TimeKeeper timeKeeper)
        {
            _algorithmSettings = algorithmSettings;
            _timeKeeper = timeKeeper;
            //Generic Type Data Holder:
            Subscriptions = new HashSet<SubscriptionDataConfig>();

            // Initialize the default data feeds for each security type
            AvailableDataTypes = DefaultDataTypes();
        }

        /// <summary>
        /// Get the count of assets:
        /// </summary>
        public int Count
        {
            get
            {
                return Subscriptions.Count;
            }
        }

        /// <summary>
        /// Add Market Data Required (Overloaded method for backwards compatibility).
        /// </summary>
        /// <param name="symbol">Symbol of the asset we're like</param>
        /// <param name="resolution">Resolution of Asset Required</param>
        /// <param name="timeZone">The time zone the subscription's data is time stamped in</param>
        /// <param name="exchangeTimeZone">Specifies the time zone of the exchange for the security this subscription is for. This
        /// is this output time zone, that is, the time zone that will be used on BaseData instances</param>
        /// <param name="isCustomData">True if this is custom user supplied data, false for normal QC data</param>
        /// <param name="fillDataForward">when there is no data pass the last tradebar forward</param>
        /// <param name="extendedMarketHours">Request premarket data as well when true </param>
        /// <returns>The newly created <see cref="SubscriptionDataConfig"/></returns>
        public SubscriptionDataConfig Add(Symbol symbol, Resolution resolution, DateTimeZone timeZone, DateTimeZone exchangeTimeZone, bool isCustomData = false, bool fillDataForward = true, bool extendedMarketHours = false)
        {
            //Set the type: market data only comes in two forms -- ticks(trade by trade) or tradebar(time summaries)
            var dataType = typeof(TradeBar);
            if (resolution == Resolution.Tick)
            {
                dataType = typeof(Tick);
            }
            var tickType = LeanData.GetCommonTickTypeForCommonDataTypes(dataType, symbol.SecurityType);
            return Add(dataType, tickType, symbol, resolution, timeZone, exchangeTimeZone, isCustomData, fillDataForward, extendedMarketHours);
        }

        /// <summary>
        /// Add Market Data Required - generic data typing support as long as Type implements BaseData.
        /// </summary>
        /// <param name="dataType">Set the type of the data we're subscribing to.</param>
        /// <param name="tickType">Tick type for the subscription.</param>
        /// <param name="symbol">Symbol of the asset we're like</param>
        /// <param name="resolution">Resolution of Asset Required</param>
        /// <param name="dataTimeZone">The time zone the subscription's data is time stamped in</param>
        /// <param name="exchangeTimeZone">Specifies the time zone of the exchange for the security this subscription is for. This
        /// is this output time zone, that is, the time zone that will be used on BaseData instances</param>
        /// <param name="isCustomData">True if this is custom user supplied data, false for normal QC data</param>
        /// <param name="fillDataForward">when there is no data pass the last tradebar forward</param>
        /// <param name="extendedMarketHours">Request premarket data as well when true </param>
        /// <param name="isInternalFeed">Set to true to prevent data from this subscription from being sent into the algorithm's OnData events</param>
        /// <param name="isFilteredSubscription">True if this subscription should have filters applied to it (market hours/user filters from security), false otherwise</param>
        /// <returns>The newly created <see cref="SubscriptionDataConfig"/></returns>
        public SubscriptionDataConfig Add(Type dataType, TickType tickType, Symbol symbol, Resolution resolution, DateTimeZone dataTimeZone, DateTimeZone exchangeTimeZone, bool isCustomData, bool fillDataForward = true, bool extendedMarketHours = false, bool isInternalFeed = false, bool isFilteredSubscription = true)
        {
            if (dataTimeZone == null)
            {
                throw new ArgumentNullException("dataTimeZone", "DataTimeZone is a required parameter for new subscriptions.  Set to the time zone the raw data is time stamped in.");
            }
            if (exchangeTimeZone == null)
            {
                throw new ArgumentNullException("exchangeTimeZone", "ExchangeTimeZone is a required parameter for new subscriptions.  Set to the time zone the security exchange resides in.");
            }

            //Create:
            var newConfig = new SubscriptionDataConfig(dataType, symbol, resolution, dataTimeZone, exchangeTimeZone, fillDataForward, extendedMarketHours, isInternalFeed, isCustomData, isFilteredSubscription: isFilteredSubscription, tickType: tickType);

            //Add to subscription list: make sure we don't have this symbol:
            if (Subscriptions.Contains(newConfig))
            {
                Log.Trace("SubscriptionManager.Add(): subscription already added: " + newConfig);
                return newConfig;
            }

            Subscriptions.Add(newConfig);

            // count data subscriptions by symbol, ignoring multiple data types
            var uniqueCount = Subscriptions
                .Where(x => !x.Symbol.IsCanonical())
                .DistinctBy(x => x.Symbol.Value)
                .Count();
            if (uniqueCount > _algorithmSettings.DataSubscriptionLimit)
            {
                throw new Exception(
                    string.Format(
                        "The maximum number of concurrent market data subscriptions was exceeded ({0}). Please reduce the number of symbols requested or increase the limit using Settings.DataSubscriptionLimit.",
                        _algorithmSettings.DataSubscriptionLimit));
            }

            // add the time zone to our time keeper
            _timeKeeper.AddTimeZone(exchangeTimeZone);

            // if is custom data, sets HasCustomData to true
            HasCustomData = HasCustomData || isCustomData;

            return newConfig;
        }

        /// <summary>
        /// Add a consolidator for the symbol
        /// </summary>
        /// <param name="symbol">Symbol of the asset to consolidate</param>
        /// <param name="consolidator">The consolidator</param>
        public void AddConsolidator(Symbol symbol, IDataConsolidator consolidator)
        {
            // Find the right subscription and add the consolidator to it
            var subscriptions = Subscriptions.Where(x => x.Symbol == symbol).ToList();

            if (subscriptions.Count == 0)
            {
                // If we made it here it is because we never found the symbol in the subscription list
                throw new ArgumentException("Please subscribe to this symbol before adding a consolidator for it. Symbol: " + symbol.Value);
            }

            foreach (var subscription in subscriptions)
            {
                // we need to be able to pipe data directly from the data feed into the consolidator
                if (consolidator.InputType.IsAssignableFrom(subscription.Type))
                {
                    subscription.Consolidators.Add(consolidator);
                    return;
                }
            }

            throw new ArgumentException(string.Format("Type mismatch found between consolidator and symbol. " +
                "Symbol: {0} does not support input type: {1}. Supported types: {2}.",
                symbol.Value,
                consolidator.InputType.Name,
                string.Join(",", subscriptions.Select(x => x.Type.Name))));
        }

        /// <summary>
        /// Removes the specified consolidator for the symbol
        /// </summary>
        /// <param name="symbol">The symbol the consolidator is receiving data from</param>
        /// <param name="consolidator">The consolidator instance to be removed</param>
        public void RemoveConsolidator(Symbol symbol, IDataConsolidator consolidator)
        {
            // remove consolidator from each subscription
            foreach (var subscription in Subscriptions.Where(x => x.Symbol == symbol))
            {
                subscription.Consolidators.Remove(consolidator);
            }

            // dispose of the consolidator to remove any remaining event handlers
            consolidator.DisposeSafely();
        }

        /// <summary>
        /// Hard code the set of default available data feeds
        /// </summary>
        public Dictionary<SecurityType, List<TickType>> DefaultDataTypes()
        {
            return new Dictionary<SecurityType, List<TickType>>()
            {
                {SecurityType.Base, new List<TickType>() { TickType.Trade } },
                {SecurityType.Forex, new List<TickType>() { TickType.Quote } },
                {SecurityType.Equity, new List<TickType>() { TickType.Trade } },
                {SecurityType.Option, new List<TickType>() { TickType.Quote, TickType.Trade, TickType.OpenInterest } },
                {SecurityType.Cfd, new List<TickType>() { TickType.Quote } },
                {SecurityType.Future, new List<TickType>() { TickType.Quote, TickType.Trade, TickType.OpenInterest } },
                {SecurityType.Commodity, new List<TickType>() { TickType.Trade } },
                {SecurityType.Crypto, new List<TickType>() { TickType.Trade, TickType.Quote } },
            };
        }

        /// <summary>
        /// Get the available data types for a security
        /// </summary>
        public IReadOnlyList<TickType> GetDataTypesForSecurity(SecurityType securityType)
        {
            return AvailableDataTypes[securityType];
        }

        /// <summary>
        /// Get the data feed types for a given <see cref="SecurityType"/> <see cref="Resolution"/>
        /// </summary>
        /// <param name="symbolSecurityType">The <see cref="SecurityType"/> used to determine the types</param>
        /// <param name="resolution">The resolution of the data requested</param>
        /// <param name="isCanonical">Indicates whether the security is Canonical (future and options)</param>
        /// <returns>Types that should be added to the <see cref="SubscriptionDataConfig"/></returns>
        public List<Tuple<Type, TickType>> LookupSubscriptionConfigDataTypes(SecurityType symbolSecurityType, Resolution resolution, bool isCanonical)
        {
            if (isCanonical)
            {
                return new List<Tuple<Type, TickType>> { new Tuple<Type, TickType>(typeof(ZipEntryName), TickType.Quote) };
            }

            return AvailableDataTypes[symbolSecurityType].Select(tickType => new Tuple<Type, TickType>(LeanData.GetDataType(resolution, tickType), tickType)).ToList();
        }

    } // End Algorithm MetaData Manager Class

} // End QC Namespace
