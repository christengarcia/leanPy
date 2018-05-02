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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using QuantConnect.Data;
using QuantConnect.ToolBox.IEX;

namespace QuantConnect.Tests.Engine.DataFeeds
{
    [TestFixture, Ignore("Tests are dependent on network and are long")]
    public class IEXDataQueueHandlerTests
    {
        private void ProcessFeed(IEXDataQueueHandler iex, Action<BaseData> callback = null)
        {
            Task.Run(() =>
            {
                foreach (var tick in iex.GetNextTicks())
                {
                    try
                    {
                        if (callback != null)
                        {
                            callback.Invoke(tick);
                        }
                    }
                    catch (AssertionException)
                    {
                        throw;
                    }
                    catch (Exception err)
                    {
                        Console.WriteLine(err.Message);
                    }
                }
            });
        }

        [Test]
        public void IEXCouldConnect()
        {
            var iex = new IEXDataQueueHandler();
            Thread.Sleep(5000);
            Assert.IsTrue(iex.IsConnected);
            iex = null;
            GC.Collect(2, GCCollectionMode.Forced, true);
            Thread.Sleep(1000);
            // finalizer should print disconnected message
        }

        /// <summary>
        /// Firehose is a special symbol that subscribes to all IEX symbols
        /// </summary>
        [Test]
        public void IEXCouldSubscribeToAll()
        {
            var iex = new IEXDataQueueHandler();

            ProcessFeed(iex, tick => Console.WriteLine(tick.ToString()));

            iex.Subscribe(null, new[]
            {
                Symbol.Create("firehose", SecurityType.Equity, Market.USA)
            });

            Thread.Sleep(30000);
            iex.Dispose();
        }

        /// <summary>
        /// Subscribe to multiple symbols in a single call
        /// </summary>
        [Test]
        public void IEXCouldSubscribe()
        {
            var iex = new IEXDataQueueHandler();

            ProcessFeed(iex, tick => Console.WriteLine(tick.ToString()));

            iex.Subscribe(null, new[]
            {
                Symbol.Create("FB", SecurityType.Equity, Market.USA),
                Symbol.Create("AAPL", SecurityType.Equity, Market.USA),
                Symbol.Create("XIV", SecurityType.Equity, Market.USA),
                Symbol.Create("PTN", SecurityType.Equity, Market.USA),
                Symbol.Create("USO", SecurityType.Equity, Market.USA),
            });

            Thread.Sleep(10000);
            iex.Dispose();
        }

        /// <summary>
        /// Subscribe to multiple symbols in a series of calls
        /// </summary>
        [Test]
        public void IEXCouldSubscribeManyTimes()
        {
            var iex = new IEXDataQueueHandler();

            ProcessFeed(iex, tick => Console.WriteLine(tick.ToString()));

            iex.Subscribe(null, new[]
            {
                Symbol.Create("MBLY", SecurityType.Equity, Market.USA),
            });

            iex.Subscribe(null, new[]
            {
                Symbol.Create("FB", SecurityType.Equity, Market.USA),
            });

            iex.Subscribe(null, new[]
            {
                Symbol.Create("AAPL", SecurityType.Equity, Market.USA),
            });

            iex.Subscribe(null, new[]
            {
                Symbol.Create("USO", SecurityType.Equity, Market.USA),
            });

            Thread.Sleep(10000);

            Console.WriteLine("Unsubscribing from all except MBLY");

            iex.Unsubscribe(null, new[]
            {
                Symbol.Create("FB", SecurityType.Equity, Market.USA),
            });

            iex.Unsubscribe(null, new[]
            {
                Symbol.Create("AAPL", SecurityType.Equity, Market.USA),
            });

            iex.Unsubscribe(null, new[]
            {
                Symbol.Create("USO", SecurityType.Equity, Market.USA),
            });

            Thread.Sleep(10000);

            iex.Dispose();
        }

        [Test]
        public void IEXCouldSubscribeAndUnsubscribe()
        {
            // MBLY is the most liquid IEX instrument
            var iex = new IEXDataQueueHandler();
            var unsubscribed = false;
            ProcessFeed(iex, tick =>
            {
                Console.WriteLine(tick.ToString());
                if (unsubscribed && tick.Symbol.Value == "MBLY")
                {
                    Assert.Fail("Should not receive data for unsubscribed symbol");
                }
            });

            iex.Subscribe(null, new[] {
                Symbol.Create("MBLY", SecurityType.Equity, Market.USA),
                Symbol.Create("USO", SecurityType.Equity, Market.USA)
            });

            Thread.Sleep(20000);

            iex.Unsubscribe(null, new[]
            {
                Symbol.Create("MBLY", SecurityType.Equity, Market.USA)
            });
            Console.WriteLine("Unsubscribing");
            Thread.Sleep(2000);
            // some messages could be inflight, but after a pause all MBLY messages must have beed consumed by ProcessFeed
            unsubscribed = true;

            Thread.Sleep(20000);
            iex.Dispose();
        }

        [Test]
        public void IEXCouldReconnect()
        {
            var iex = new IEXDataQueueHandler();
            var realEndpoint = iex.Endpoint;
            Thread.Sleep(1000);
            iex.Dispose();
            iex.Endpoint = "https://badd.address";
            iex.Reconnect();
            Thread.Sleep(1000);
            iex.Dispose();
            iex.Endpoint = realEndpoint;
            iex.Reconnect();
            Thread.Sleep(1000);
            Assert.IsTrue(iex.IsConnected);
            iex.Dispose();
        }
    }
}