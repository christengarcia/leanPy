/*
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
using QuantConnect.Data;
using QuantConnect.Data.UniverseSelection;
using QuantConnect.Interfaces;
using QuantConnect.Util;

namespace QuantConnect.Lean.Engine.DataFeeds.Enumerators.Factories
{
    /// <summary>
    /// Provides an implementation of <see cref="ISubscriptionEnumeratorFactory"/> to handle live custom data.
    /// </summary>
    public class LiveCustomDataSubscriptionEnumeratorFactory : ISubscriptionEnumeratorFactory
    {
        private readonly ITimeProvider _timeProvider;

        /// <summary>
        /// Initializes a new instance of the <see cref="LiveCustomDataSubscriptionEnumeratorFactory"/> class
        /// </summary>
        /// <param name="timeProvider">Time provider from data feed</param>
        public LiveCustomDataSubscriptionEnumeratorFactory(ITimeProvider timeProvider)
        {
            _timeProvider = timeProvider;
        }

        /// <summary>
        /// Creates an enumerator to read the specified request.
        /// </summary>
        /// <param name="request">The subscription request to be read</param>
        /// <param name="dataProvider">Provider used to get data when it is not present on disk</param>
        /// <returns>An enumerator reading the subscription request</returns>
        public IEnumerator<BaseData> CreateEnumerator(SubscriptionRequest request, IDataProvider dataProvider)
        {
            var config = request.Configuration;

            // frontier value used to prevent emitting duplicate time stamps between refreshed enumerators
            // also provides some immediate fast-forward to handle spooling through remote files quickly
            var frontier = Ref.Create(request.StartTimeLocal);
            var lastSourceRefreshTime = DateTime.MinValue;
            var sourceFactory = (BaseData) ObjectActivator.GetActivator(config.Type).Invoke(new object[] {config.Type});

            // this is refreshing the enumerator stack for each new source
            var refresher = new RefreshEnumerator<BaseData>(() =>
            {
                // rate limit the refresh of this enumerator stack
                var utcNow = _timeProvider.GetUtcNow();
                var minimumTimeBetweenCalls = GetMinimumTimeBetweenCalls(config.Increment);
                if (utcNow - lastSourceRefreshTime < minimumTimeBetweenCalls)
                {
                    return Enumerable.Empty<BaseData>().GetEnumerator();
                }

                lastSourceRefreshTime = utcNow;
                var localDate = utcNow.ConvertFromUtc(config.ExchangeTimeZone).Date;
                var source = sourceFactory.GetSource(config, localDate, true);

                // fetch the new source and enumerate the data source reader
                var enumerator = EnumerateDataSourceReader(config, dataProvider, frontier, source, localDate);

                if (SourceRequiresFastForward(source))
                {
                    // The FastForwardEnumerator implements these two features:
                    // (1) make sure we never emit past data
                    // (2) data filtering based on a maximum data age
                    // For custom data we don't want feature (2) because we would reject data points emitted later
                    // (e.g. Quandl daily data after a weekend), so we disable it using a huge maximum data age.

                    // apply fast forward logic for file transport mediums
                    var maximumDataAge = GetMaximumDataAge(Time.MaxTimeSpan);
                    enumerator = new FastForwardEnumerator(enumerator, _timeProvider, config.ExchangeTimeZone, maximumDataAge);
                }
                else
                {
                    // rate limit calls to this enumerator stack
                    enumerator = new RateLimitEnumerator<BaseData>(enumerator, _timeProvider, minimumTimeBetweenCalls);
                }

                if (source.Format == FileFormat.Collection)
                {
                    // unroll collections into individual data points after fast forward/rate limiting applied
                    enumerator = enumerator.SelectMany(data =>
                    {
                        var collection = data as BaseDataCollection;
                        return collection?.Data.GetEnumerator() ?? new List<BaseData> {data}.GetEnumerator();
                    });
                }

                return enumerator;
            });

            // prevent calls to the enumerator stack if current is in the future
            var timeZoneOffsetProvider = new TimeZoneOffsetProvider(request.Security.Exchange.TimeZone, request.StartTimeUtc, request.EndTimeUtc);
            return new FrontierAwareEnumerator(refresher, _timeProvider, timeZoneOffsetProvider);
        }

        private IEnumerator<BaseData> EnumerateDataSourceReader(SubscriptionDataConfig config, IDataProvider dataProvider, Ref<DateTime> localFrontier, SubscriptionDataSource source, DateTime localDate)
        {
            using (var dataCacheProvider = new SingleEntryDataCacheProvider(dataProvider))
            {
                var newLocalFrontier = localFrontier.Value;
                var dataSourceReader = GetSubscriptionDataSourceReader(source, dataCacheProvider, config, localDate);
                foreach (var datum in dataSourceReader.Read(source))
                {
                    // always skip past all times emitted on the previous invocation of this enumerator
                    // this allows data at the same time from the same refresh of the source while excluding
                    // data from different refreshes of the source
                    if (datum.EndTime > localFrontier.Value)
                    {
                        yield return datum;
                    }

                    newLocalFrontier = Time.Max(datum.EndTime, newLocalFrontier);
                }

                localFrontier.Value = newLocalFrontier;
            }
        }

        /// <summary>
        /// Gets the <see cref="ISubscriptionDataSourceReader"/> for the specified source
        /// </summary>
        protected virtual ISubscriptionDataSourceReader GetSubscriptionDataSourceReader(SubscriptionDataSource source,
            IDataCacheProvider dataCacheProvider,
            SubscriptionDataConfig config,
            DateTime date
            )
        {
            return SubscriptionDataSourceReader.ForSource(source, dataCacheProvider, config, date, true);
        }

        private bool SourceRequiresFastForward(SubscriptionDataSource source)
        {
            return source.TransportMedium == SubscriptionTransportMedium.LocalFile
                || source.TransportMedium == SubscriptionTransportMedium.RemoteFile;
        }

        private static TimeSpan GetMinimumTimeBetweenCalls(TimeSpan increment)
        {
            return TimeSpan.FromTicks(Math.Min(increment.Ticks, TimeSpan.FromMinutes(30).Ticks));
        }

        private static TimeSpan GetMaximumDataAge(TimeSpan increment)
        {
            return TimeSpan.FromTicks(Math.Max(increment.Ticks, TimeSpan.FromSeconds(5).Ticks));
        }
    }
}