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

using System;
using System.Collections.Generic;
using System.Linq;
using QuantConnect.Interfaces;
using QuantConnect.Securities;
using QuantConnect.Securities.Option;
using QuantConnect.Util;

namespace QuantConnect.Data.UniverseSelection
{
    /// <summary>
    /// Defines a universe for a single option chain
    /// </summary>
    public class OptionChainUniverse : Universe
    {
        private BaseData _underlying;
        private readonly Option _option;
        private readonly UniverseSettings _universeSettings;
        private readonly bool _liveMode;

        private DateTime _cacheDate;

        // used for time-based removals in live mode
        private readonly TimeSpan _minimumTimeInUniverse = TimeSpan.FromMinutes(15);
        private readonly Dictionary<Symbol, DateTime> _addTimesBySymbol = new Dictionary<Symbol, DateTime>();

        /// <summary>
        /// Initializes a new instance of the <see cref="OptionChainUniverse"/> class
        /// </summary>
        /// <param name="option">The canonical option chain security</param>
        /// <param name="universeSettings">The universe settings to be used for new subscriptions</param>
        /// <param name="securityInitializer">The security initializer to use on newly created securities</param>
        /// <param name="liveMode">True if we're running in live mode, false for backtest mode</param>
        public OptionChainUniverse(Option option,
                                   UniverseSettings universeSettings,
                                   ISecurityInitializer securityInitializer,
                                   bool liveMode)
            : base(option.SubscriptionDataConfig, securityInitializer)
        {
            _option = option;
            _universeSettings = universeSettings;
            _liveMode = liveMode;
        }

        /// <summary>
        /// Gets the settings used for subscriptons added for this universe
        /// </summary>
        public override UniverseSettings UniverseSettings
        {
            get { return _universeSettings; }
        }

        /// <summary>
        /// Performs universe selection using the data specified
        /// </summary>
        /// <param name="utcTime">The current utc time</param>
        /// <param name="data">The symbols to remain in the universe</param>
        /// <returns>The data that passes the filter</returns>
        public override IEnumerable<Symbol> SelectSymbols(DateTime utcTime, BaseDataCollection data)
        {
            var optionsUniverseDataCollection = data as OptionChainUniverseDataCollection;
            if (optionsUniverseDataCollection == null)
            {
                throw new ArgumentException(string.Format("Expected data of type '{0}'", typeof (OptionChainUniverseDataCollection).Name));
            }

            _underlying = optionsUniverseDataCollection.Underlying ?? _underlying;
            optionsUniverseDataCollection.Underlying = _underlying;

            if (_underlying == null || data.Data.Count == 0)
            {
                return Unchanged;
            }

            if (_cacheDate == data.Time.Date)
            {
                return Unchanged;
            }

            var availableContracts = optionsUniverseDataCollection.Data.Select(x => x.Symbol);
            var results = _option.ContractFilter.Filter(new OptionFilterUniverse(availableContracts, _underlying));

            // if results are not dynamic, we cache them and won't call filtering till the end of the day
            if (!results.IsDynamic)
            {
                _cacheDate = data.Time.Date;
            }

            var resultingSymbols = results.ToHashSet();

            // we save off the filtered results to the universe data collection for later
            // population into the OptionChain. This is non-ideal and could be remedied by
            // the universe subscription emitting a special type after selection that could
            // be checked for in TimeSlice.Create, but for now this will do
            optionsUniverseDataCollection.FilteredContracts = resultingSymbols;

            return resultingSymbols;
        }

        /// <summary>
        /// Adds the specified security to this universe
        /// </summary>
        /// <param name="utcTime">The current utc date time</param>
        /// <param name="security">The security to be added</param>
        /// <returns>True if the security was successfully added,
        /// false if the security was already in the universe</returns>
        internal override bool AddMember(DateTime utcTime, Security security)
        {
            if (Securities.ContainsKey(security.Symbol))
            {
                return false;
            }

            // method take into account the case, when the option has experienced an adjustment
            // we update member reference in this case
            if (Securities.Any(x => x.Value.Security == security))
            {
                Member member;
                Securities.TryRemove(security.Symbol, out member);
            }

            var added = Securities.TryAdd(security.Symbol, new Member(utcTime, security));

            if (added && _liveMode)
            {
                _addTimesBySymbol[security.Symbol] = utcTime;
            }

            return added;
        }

        /// <summary>
        /// Creates and configures a security for the specified symbol
        /// </summary>
        /// <param name="symbol">The symbol of the security to be created</param>
        /// <param name="algorithm">The algorithm instance</param>
        /// <param name="marketHoursDatabase">The market hours database</param>
        /// <param name="symbolPropertiesDatabase">The symbol properties database</param>
        /// <returns>The newly initialized security object</returns>
        public override Security CreateSecurity(Symbol symbol, IAlgorithm algorithm, MarketHoursDatabase marketHoursDatabase, SymbolPropertiesDatabase symbolPropertiesDatabase)
        {
            // set the underlying security and pricing model from the canonical security
            var option = (Option)base.CreateSecurity(symbol, algorithm, marketHoursDatabase, symbolPropertiesDatabase);
            option.Underlying = _option.Underlying;
            option.PriceModel = _option.PriceModel;
            return option;
        }

        /// <summary>
        /// Determines whether or not the specified security can be removed from
        /// this universe. This is useful to prevent securities from being taken
        /// out of a universe before the algorithm has had enough time to make
        /// decisions on the security
        /// </summary>
        /// <param name="utcTime">The current utc time</param>
        /// <param name="security">The security to check if its ok to remove</param>
        /// <returns>True if we can remove the security, false otherwise</returns>
        public override bool CanRemoveMember(DateTime utcTime, Security security)
        {
            // if we haven't begun receiving data for this security then it's safe to remove
            var lastData = security.Cache.GetData();
            if (lastData == null)
            {
                return true;
            }

            if (_liveMode)
            {
                // Only remove members when they have been in the universe for a minimum period of time.
                // This prevents us from needing to move contracts in and out too fast,
                // as price moves and filtered contracts change throughout the day.
                DateTime timeAdded;

                // get the date/time this symbol was added to the universe
                if (!_addTimesBySymbol.TryGetValue(security.Symbol, out timeAdded))
                {
                    return true;
                }

                if (timeAdded.Add(_minimumTimeInUniverse) > utcTime)
                {
                    // minimum time span not yet elapsed, do not remove
                    return false;
                }

                // ok to remove
                _addTimesBySymbol.Remove(security.Symbol);

                return true;
            }

            // only remove members on day changes, this prevents us from needing to
            // fast forward contracts continuously as price moves and out filtered
            // contracts change throughout the day
            var localTime = utcTime.ConvertFromUtc(security.Exchange.TimeZone);
            return localTime.Date != lastData.Time.Date;
        }
    }
}
