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
using QuantConnect.Algorithm.Framework.Alphas;
using QuantConnect.Algorithm.Framework.Alphas.Analysis;
using QuantConnect.Interfaces;
using QuantConnect.Logging;

namespace QuantConnect.Lean.Engine.Alphas
{
    /// <summary>
    /// Manages alpha charting responsibilities.
    /// </summary>
    public class ChartingInsightManagerExtension : IInsightManagerExtension
    {
        private readonly bool _liveMode;
        private readonly StatisticsInsightManagerExtension _statisticsManager;

        private const int BacktestChartSamples = 1000;
        private DateTime _lastInsightCountSampleDateUtc;
        private DateTime _nextChartSampleAlgorithmTimeUtc;

        private readonly Chart _totalInsightCountPerSymbolChart = new Chart("Alpha Assets");          // pie chart
        private readonly Chart _dailyInsightCountPerSymbolChart = new Chart("Alpha Asset Breakdown"); // stacked area
        private readonly Series _totalInsightCountSeries = new Series("Count", SeriesType.Bar, "#");

        private readonly Dictionary<Symbol, int> _insightCountPerSymbol = new Dictionary<Symbol, int>();
        private readonly Dictionary<Symbol, int> _dailyInsightCountPerSymbol = new Dictionary<Symbol, int>();
        private readonly Dictionary<InsightScoreType, Series> _insightScoreSeriesByScoreType = new Dictionary<InsightScoreType, Series>();

        /// <summary>
        /// Gets or sets the interval at which alpha charts are updated. This is in realtion to algorithm time.
        /// </summary>
        protected TimeSpan SampleInterval { get; set; } = TimeSpan.FromMinutes(1);

        /// <summary>
        /// Initializes a new instance of the <see cref="ChartingInsightManagerExtension"/> class
        /// </summary>
        /// <param name="algorithm">The algorithm instance. This is only used for adding the charts
        /// to the algorithm. We purposefully do not save a reference to avoid potentially inconsistent reads</param>
        /// <param name="statisticsManager">Statistics manager used to access mean population scores for charting</param>
        public ChartingInsightManagerExtension(IAlgorithm algorithm, StatisticsInsightManagerExtension statisticsManager)
        {
            _statisticsManager = statisticsManager;
            _liveMode = algorithm.LiveMode;

            // chart for average scores over sample period
            var scoreChart = new Chart("Alpha");
            foreach (var scoreType in InsightManager.ScoreTypes)
            {
                var series = new Series($"{scoreType} Score", SeriesType.Line, "%");
                scoreChart.AddSeries(series);
                _insightScoreSeriesByScoreType[scoreType] = series;
            }

            // chart for insight count over sample period
            var insightCount = new Chart("Insight Count");
            insightCount.AddSeries(_totalInsightCountSeries);

            algorithm.AddChart(scoreChart);
            algorithm.AddChart(insightCount);
            algorithm.AddChart(_totalInsightCountPerSymbolChart);
            // removing this for now, not sure best way to display this data
            //Algorithm.AddChart(_dailyInsightCountPerSymbolChart);
        }

        /// <summary>
        /// Invokes the manager at the end of the time step.
        /// Samples and plots insight counts and population score.
        /// </summary>
        /// <param name="frontierTimeUtc">The current frontier time utc</param>
        public void Step(DateTime frontierTimeUtc)
        {
            // sample insight/symbol counts each utc day change
            if (frontierTimeUtc.Date > _lastInsightCountSampleDateUtc)
            {
                _lastInsightCountSampleDateUtc = frontierTimeUtc.Date;

                // populate charts with the daily insight counts per symbol, resetting our storage
                var sumInsights = PopulateChartWithSeriesPerSymbol(_dailyInsightCountPerSymbol, _dailyInsightCountPerSymbolChart, SeriesType.StackedArea, frontierTimeUtc);
                _dailyInsightCountPerSymbol.Clear();

                // add sum of daily insight counts to the total insight count series
                _totalInsightCountSeries.AddPoint(frontierTimeUtc.Date, sumInsights, _liveMode);

                // populate charts with the total insight counts per symbol, no need to reset
                PopulateChartWithSeriesPerSymbol(_insightCountPerSymbol, _totalInsightCountPerSymbolChart, SeriesType.Pie, frontierTimeUtc);
            }

            // sample average population scores
            if (frontierTimeUtc >= _nextChartSampleAlgorithmTimeUtc)
            {
                try
                {
                    // verify these scores have been computed before taking the first sample
                    if (_statisticsManager.RollingAverageIsReady)
                    {
                        // sample the rolling averaged population scores
                        foreach (var scoreType in InsightManager.ScoreTypes)
                        {
                            var score = 100 * _statisticsManager.Statistics.RollingAveragedPopulationScore.GetScore(scoreType);
                            _insightScoreSeriesByScoreType[scoreType].AddPoint(frontierTimeUtc, (decimal)score, _liveMode);
                        }
                        _nextChartSampleAlgorithmTimeUtc = frontierTimeUtc + SampleInterval;
                    }
                }
                catch (Exception err)
                {
                    Log.Error(err);
                }
            }
        }

        /// <summary>
        /// Invoked after <see cref="IAlgorithm.Initialize"/> has been called.
        /// Determines chart sample interval and initial sample times
        /// </summary>
        /// <remarks>
        /// While the algorithm instance is provided, it's highly recommended to not maintain
        /// a direct reference to it as there is no way to guarantee consistence reads.
        /// </remarks>
        /// <param name="algorithmStartDate">The start date of the algorithm</param>
        /// <param name="algorithmEndDate">The end date of the algorithm</param>
        /// <param name="algorithmUtcTime">The algorithm's current utc time</param>
        public void InitializeForRange(DateTime algorithmStartDate, DateTime algorithmEndDate, DateTime algorithmUtcTime)
        {
            if (_liveMode)
            {
                // live mode we'll sample each minute
                SampleInterval = Time.OneMinute;
            }
            else
            {
                // space out backtesting samples evenly
                var backtestPeriod = algorithmEndDate - algorithmStartDate;
                SampleInterval = TimeSpan.FromTicks(backtestPeriod.Ticks / BacktestChartSamples);
            }

            _nextChartSampleAlgorithmTimeUtc = algorithmUtcTime + SampleInterval;
            _lastInsightCountSampleDateUtc = algorithmUtcTime.RoundDown(Time.OneDay);
        }

        /// <summary>
        /// Handles the <see cref="IAlgorithm.InsightsGenerated"/> event.
        /// Keep daily and total count of insights by symbol
        /// </summary>
        /// <param name="context">The newly generated insight analysis context</param>
        public void OnInsightGenerated(InsightAnalysisContext context)
        {
            if (!_dailyInsightCountPerSymbol.ContainsKey(context.Symbol))
            {
                _insightCountPerSymbol[context.Symbol] = 1;
                _dailyInsightCountPerSymbol[context.Symbol] = 1;
            }
            else
            {
                // track total assets for life of backtest
                _insightCountPerSymbol[context.Symbol] += 1;

                // track daily assets
                _dailyInsightCountPerSymbol[context.Symbol] += 1;
            }
        }

        /// <summary>
        /// NOP - Charting is more concerned with population vs individual insights
        /// </summary>
        /// <param name="context">Context whose insight has just completed analysis</param>
        public void OnInsightClosed(InsightAnalysisContext context)
        {
        }

        /// <summary>
        /// NOP - Charting is more concerned with population vs individual insights
        /// </summary>
        /// <param name="context">Context whose insight has just completed analysis</param>
        public void OnInsightAnalysisCompleted(InsightAnalysisContext context)
        {
        }

        /// <summary>
        /// Creates series for each symbol and adds a value corresponding to the specified data
        /// </summary>
        private int PopulateChartWithSeriesPerSymbol(Dictionary<Symbol, int> data, Chart chart, SeriesType seriesType, DateTime frontierTimeUtc)
        {
            var sum = 0;
            foreach (var kvp in data)
            {
                var symbol = kvp.Key;
                var count = kvp.Value;

                Series series;
                if (!chart.Series.TryGetValue(symbol.Value, out series))
                {
                    series = new Series(symbol.Value, seriesType, "#");
                    chart.Series.Add(series.Name, series);
                }

                sum += count;
                series.AddPoint(frontierTimeUtc, count, _liveMode);
            }
            return sum;
        }
    }
}