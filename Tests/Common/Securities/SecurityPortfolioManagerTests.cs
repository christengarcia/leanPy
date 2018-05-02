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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using NUnit.Framework;
using QuantConnect.Brokerages;
using QuantConnect.Data;
using QuantConnect.Data.Market;
using QuantConnect.Indicators;
using QuantConnect.Orders;
using QuantConnect.Securities;
using QuantConnect.Util;
using QuantConnect.Securities.Option;
using QuantConnect.Lean.Engine.TransactionHandlers;
using QuantConnect.Brokerages.Backtesting;
using QuantConnect.Tests.Engine;
using QuantConnect.Algorithm;
using QuantConnect.Data.UniverseSelection;

namespace QuantConnect.Tests.Common.Securities
{
    [TestFixture]
    public class SecurityPortfolioManagerTests
    {
        private static readonly AlgorithmSettings AlgorithmSettings = new AlgorithmSettings();
        private static readonly SecurityExchangeHours SecurityExchangeHours = SecurityExchangeHours.AlwaysOpen(TimeZones.NewYork);
        private static readonly Symbol CASH = new Symbol(SecurityIdentifier.GenerateBase("CASH", Market.USA), "CASH");
        private static readonly Symbol MCHJWB = new Symbol(SecurityIdentifier.GenerateForex("MCHJWB", Market.FXCM), "MCHJWB");
        private static readonly Symbol MCHUSD = new Symbol(SecurityIdentifier.GenerateForex("MCHUSD", Market.FXCM), "MCHUSD");
        private static readonly Symbol USDJWB = new Symbol(SecurityIdentifier.GenerateForex("USDJWB", Market.FXCM), "USDJWB");
        private static readonly Symbol JWBUSD = new Symbol(SecurityIdentifier.GenerateForex("JWBUSD", Market.FXCM), "JWBUSD");

        private static readonly Dictionary<string, Symbol> SymbolMap = new Dictionary<string, Symbol>
        {
            {"CASH", CASH},
            {"MCHJWB", MCHJWB},
            {"MCHUSD", MCHUSD},
            {"USDJWB", USDJWB},
            {"JWBUSD", JWBUSD},
        };

        [Test]
        public void TestCashFills()
        {
            // this test asserts the portfolio behaves according to the Test_Cash algo, see TestData\CashTestingStrategy.csv
            // also "https://www.dropbox.com/s/oiliumoyqqj1ovl/2013-cash.csv?dl=1"

            const string fillsFile = "TestData\\test_cash_fills.xml";
            const string equityFile = "TestData\\test_cash_equity.xml";

            var fills = XDocument.Load(fillsFile).Descendants("OrderEvent").Select(x => new OrderEvent(
                x.Get<int>("OrderId"),
                SymbolMap[x.Get<string>("Symbol")],
                DateTime.MinValue,
                x.Get<OrderStatus>("Status"),
                x.Get<int>("FillQuantity") < 0 ? OrderDirection.Sell
              : x.Get<int>("FillQuantity") > 0 ? OrderDirection.Buy
                                               : OrderDirection.Hold,
                x.Get<decimal>("FillPrice"),
                x.Get<int>("FillQuantity"),
                0m)
                ).ToList();

            var equity = XDocument.Load(equityFile).Descendants("decimal")
                .Select(x => decimal.Parse(x.Value, CultureInfo.InvariantCulture))
                .ToList();

            Assert.AreEqual(fills.Count + 1, equity.Count);

            // we're going to process fills and very our equity after each fill
            var subscriptions = new SubscriptionManager(AlgorithmSettings, TimeKeeper);
            var securities = new SecurityManager(TimeKeeper);
            var security = new Security(SecurityExchangeHours, subscriptions.Add(CASH, Resolution.Daily, TimeZones.NewYork, TimeZones.NewYork), new Cash(CashBook.AccountCurrency, 0, 1m), SymbolProperties.GetDefault(CashBook.AccountCurrency));
            security.SetLeverage(10m);
            securities.Add(CASH, security);
            var transactions = new SecurityTransactionManager(null, securities);
            var portfolio = new SecurityPortfolioManager(securities, transactions);
            portfolio.SetCash(equity[0]);

            for (int i = 0; i < fills.Count; i++)
            {
                // before processing the fill we must deduct the cost
                var fill = fills[i];
                var time = DateTime.Today.AddDays(i);
                TimeKeeper.SetUtcDateTime(time.ConvertToUtc(TimeZones.NewYork));
                // the value of 'CASH' increments for each fill, the original test algo did this monthly
                // the time doesn't really matter though
                security.SetMarketPrice(new IndicatorDataPoint(CASH, time, i + 1));

                portfolio.ProcessFill(fill);
                Assert.AreEqual(equity[i + 1], portfolio.TotalPortfolioValue, "Failed on " + i);
            }
        }

        [Test]
        public void ForexCashFills()
        {
            // this test asserts the portfolio behaves according to the Test_Cash algo, but for a Forex security,
            // see TestData\CashTestingStrategy.csv; also "https://www.dropbox.com/s/oiliumoyqqj1ovl/2013-cash.csv?dl=1"

            const string fillsFile = "TestData\\test_forex_fills.xml";
            const string equityFile = "TestData\\test_forex_equity.xml";
            const string mchQuantityFile = "TestData\\test_forex_fills_mch_quantity.xml";
            const string jwbQuantityFile = "TestData\\test_forex_fills_jwb_quantity.xml";

            var fills = XDocument.Load(fillsFile).Descendants("OrderEvent").Select(x => new OrderEvent(
                x.Get<int>("OrderId"),
                SymbolMap[x.Get<string>("Symbol")],
                DateTime.MinValue,
                x.Get<OrderStatus>("Status"),
                x.Get<int>("FillQuantity") < 0 ? OrderDirection.Sell
              : x.Get<int>("FillQuantity") > 0 ? OrderDirection.Buy
                                               : OrderDirection.Hold,
                x.Get<decimal>("FillPrice"),
                x.Get<int>("FillQuantity"),
                0)
                ).ToList();

            var equity = XDocument.Load(equityFile).Descendants("decimal")
                .Select(x => decimal.Parse(x.Value, CultureInfo.InvariantCulture))
                .ToList();

            var mchQuantity = XDocument.Load(mchQuantityFile).Descendants("decimal")
                .Select(x => decimal.Parse(x.Value, CultureInfo.InvariantCulture))
                .ToList();

            var jwbQuantity = XDocument.Load(jwbQuantityFile).Descendants("decimal")
                .Select(x => decimal.Parse(x.Value, CultureInfo.InvariantCulture))
                .ToList();

            Assert.AreEqual(fills.Count + 1, equity.Count);

            // we're going to process fills and very our equity after each fill
            var subscriptions = new SubscriptionManager(AlgorithmSettings, TimeKeeper);
            var securities = new SecurityManager(TimeKeeper);
            var transactions = new SecurityTransactionManager(null, securities);
            var portfolio = new SecurityPortfolioManager(securities, transactions);
            portfolio.SetCash(equity[0]);
            portfolio.CashBook.Add("MCH", mchQuantity[0], 0);
            portfolio.CashBook.Add("JWB", jwbQuantity[0], 0);

            var jwbCash = portfolio.CashBook["JWB"];
            var mchCash = portfolio.CashBook["MCH"];
            var usdCash = portfolio.CashBook["USD"];

            var mchJwbSecurity = new QuantConnect.Securities.Forex.Forex(SecurityExchangeHours, jwbCash, subscriptions.Add(MCHJWB, Resolution.Minute, TimeZones.NewYork, TimeZones.NewYork), SymbolProperties.GetDefault(jwbCash.Symbol));
            mchJwbSecurity.SetLeverage(10m);
            var mchUsdSecurity = new QuantConnect.Securities.Forex.Forex(SecurityExchangeHours, usdCash, subscriptions.Add(MCHUSD, Resolution.Minute, TimeZones.NewYork, TimeZones.NewYork), SymbolProperties.GetDefault(usdCash.Symbol));
            mchUsdSecurity.SetLeverage(10m);
            var usdJwbSecurity = new QuantConnect.Securities.Forex.Forex(SecurityExchangeHours, mchCash, subscriptions.Add(USDJWB, Resolution.Minute, TimeZones.NewYork, TimeZones.NewYork), SymbolProperties.GetDefault(mchCash.Symbol));
            usdJwbSecurity.SetLeverage(10m);

            // no fee model
            mchJwbSecurity.TransactionModel = new SecurityTransactionModel();
            mchUsdSecurity.TransactionModel = new SecurityTransactionModel();
            usdJwbSecurity.TransactionModel = new SecurityTransactionModel();

            securities.Add(mchJwbSecurity);
            securities.Add(usdJwbSecurity);
            securities.Add(mchUsdSecurity);

            portfolio.CashBook.EnsureCurrencyDataFeeds(securities, subscriptions, MarketHoursDatabase.FromDataFolder(), SymbolPropertiesDatabase.FromDataFolder(), DefaultBrokerageModel.DefaultMarketMap, SecurityChanges.None);

            for (int i = 0; i < fills.Count; i++)
            {
                // before processing the fill we must deduct the cost
                var fill = fills[i];
                var time = DateTime.Today.AddDays(i);

                // the value of 'MCJWB' increments for each fill, the original test algo did this monthly
                // the time doesn't really matter though
                decimal mchJwb = i + 1;
                decimal mchUsd = (i + 1)/(i + 2m);
                decimal usdJwb = i + 2;
                Assert.AreEqual((double)mchJwb, (double)(mchUsd*usdJwb), 1e-10);
                //Console.WriteLine("Step: " + i + " -- MCHJWB: " + mchJwb);


                jwbCash.Update(new IndicatorDataPoint(MCHJWB, time, mchJwb));
                usdCash.Update(new IndicatorDataPoint(MCHUSD, time, mchUsd));
                mchCash.Update(new IndicatorDataPoint(JWBUSD, time, usdJwb));

                var updateData = new Dictionary<Security, BaseData>
                {
                    {mchJwbSecurity, new IndicatorDataPoint(MCHJWB, time, mchJwb)},
                    {mchUsdSecurity, new IndicatorDataPoint(MCHUSD, time, mchUsd)},
                    {usdJwbSecurity, new IndicatorDataPoint(JWBUSD, time, usdJwb)}
                };

                foreach (var kvp in updateData)
                {
                    kvp.Key.SetMarketPrice(kvp.Value);
                }

                portfolio.ProcessFill(fill);
                //Console.WriteLine("-----------------------");
                //Console.WriteLine(fill);

                //Console.WriteLine("Post step: " + i);
                //foreach (var cash in portfolio.CashBook)
                //{
                //    Console.WriteLine(cash.Value);
                //}
                //Console.WriteLine("CashValue: " + portfolio.CashBook.TotalValueInAccountCurrency);

                Console.WriteLine(i + 1 + "   " + portfolio.TotalPortfolioValue.ToString("C"));
                //Assert.AreEqual((double) equity[i + 1], (double)portfolio.TotalPortfolioValue, 2e-2);
                Assert.AreEqual((double) mchQuantity[i + 1], (double)portfolio.CashBook["MCH"].Amount);
                Assert.AreEqual((double) jwbQuantity[i + 1], (double)portfolio.CashBook["JWB"].Amount);

                //Console.WriteLine();
                //Console.WriteLine();
            }
        }

        [Test]
        public void ComputeMarginProperlyAsSecurityPriceFluctuates()
        {
            const decimal leverage = 1m;
            const int quantity = (int) (1000*leverage);
            var securities = new SecurityManager(TimeKeeper);
            var transactions = new SecurityTransactionManager(null, securities);
            var orderProcessor = new OrderProcessor();
            transactions.SetOrderProcessor(orderProcessor);
            var portfolio = new SecurityPortfolioManager(securities, transactions);
            portfolio.CashBook["USD"].SetAmount(quantity);

            var config = CreateTradeBarDataConfig(SecurityType.Equity, Symbols.AAPL);
            securities.Add(new Security(SecurityExchangeHours, config, new Cash(CashBook.AccountCurrency, 0, 1m), SymbolProperties.GetDefault(CashBook.AccountCurrency)));
            var security = securities[Symbols.AAPL];
            security.SetLeverage(leverage);

            var time = DateTime.Now;
            const decimal buyPrice = 1m;
            security.SetMarketPrice(new TradeBar(time, Symbols.AAPL, buyPrice, buyPrice, buyPrice, buyPrice, 1));

            var order = new MarketOrder(Symbols.AAPL, quantity, time) {Price = buyPrice};
            var fill = new OrderEvent(order, DateTime.UtcNow, 0) { FillPrice = buyPrice, FillQuantity = quantity };
            orderProcessor.AddOrder(order);
            var request = new SubmitOrderRequest(OrderType.Market, security.Type, security.Symbol, order.Quantity, 0, 0, order.Time, null);
            request.SetOrderId(0);
            orderProcessor.AddTicket(new OrderTicket(null, request));
            Assert.AreEqual(portfolio.CashBook["USD"].Amount, fill.FillPrice*fill.FillQuantity);

            portfolio.ProcessFill(fill);

            Assert.AreEqual(0, portfolio.MarginRemaining);
            Assert.AreEqual(quantity, portfolio.TotalMarginUsed);
            Assert.AreEqual(quantity, portfolio.TotalPortfolioValue);

            // we shouldn't be able to place a trader
            var newOrder = new MarketOrder(Symbols.AAPL, 1, time.AddSeconds(1)) {Price = buyPrice};
            var hasSufficientBuyingPower = security.BuyingPowerModel.HasSufficientBuyingPowerForOrder(portfolio, security, newOrder).IsSufficient;
            Assert.IsFalse(hasSufficientBuyingPower);

            // now the stock doubles, so we should have margin remaining

            time = time.AddDays(1);
            const decimal highPrice = buyPrice * 2;
            security.SetMarketPrice(new TradeBar(time, Symbols.AAPL, highPrice, highPrice, highPrice, highPrice, 1));

            Assert.AreEqual(quantity, portfolio.MarginRemaining);
            Assert.AreEqual(quantity, portfolio.TotalMarginUsed);
            Assert.AreEqual(quantity * 2, portfolio.TotalPortfolioValue);

            // we shouldn't be able to place a trader
            var anotherOrder = new MarketOrder(Symbols.AAPL, 1, time.AddSeconds(1)) { Price = highPrice };
            hasSufficientBuyingPower = security.BuyingPowerModel.HasSufficientBuyingPowerForOrder(portfolio, security, anotherOrder).IsSufficient;
            Assert.IsTrue(hasSufficientBuyingPower);

            // now the stock plummets, so we should have negative margin remaining

            time = time.AddDays(1);
            const decimal lowPrice = buyPrice/2;
            security.SetMarketPrice(new TradeBar(time, Symbols.AAPL, lowPrice, lowPrice, lowPrice, lowPrice, 1));

            Assert.AreEqual(-quantity/2m, portfolio.MarginRemaining);
            Assert.AreEqual(quantity, portfolio.TotalMarginUsed);
            Assert.AreEqual(quantity/2m, portfolio.TotalPortfolioValue);


            // this would not cause a margin call due to leverage = 1
            bool issueMarginCallWarning;
            var marginCallOrders = portfolio.MarginCallModel.GetMarginCallOrders(out issueMarginCallWarning);
            Assert.IsFalse(issueMarginCallWarning);
            Assert.AreEqual(0, marginCallOrders.Count);

            // now change the leverage to test margin call warning and margin call logic
            security.SetLeverage(leverage * 2);

            // Stock price increase by minimum variation
            const decimal newPrice = lowPrice + 0.01m;
            security.SetMarketPrice(new TradeBar(time, Symbols.AAPL, newPrice, newPrice, newPrice, newPrice, 1));

            // this would not cause a margin call, only a margin call warning
            marginCallOrders = portfolio.MarginCallModel.GetMarginCallOrders(out issueMarginCallWarning);
            Assert.IsTrue(issueMarginCallWarning);
            Assert.AreEqual(0, marginCallOrders.Count);

            // Price drops again to previous low, margin call orders will be issued
            security.SetMarketPrice(new TradeBar(time, Symbols.AAPL, lowPrice, lowPrice, lowPrice, lowPrice, 1));

            order = new MarketOrder(Symbols.AAPL, quantity, time) { Price = buyPrice };
            fill = new OrderEvent(order, DateTime.UtcNow, 0) { FillPrice = buyPrice, FillQuantity = quantity };

            portfolio.ProcessFill(fill);

            Assert.AreEqual(0, portfolio.TotalPortfolioValue);

            marginCallOrders = portfolio.MarginCallModel.GetMarginCallOrders(out issueMarginCallWarning);
            Assert.IsTrue(issueMarginCallWarning);
            Assert.AreNotEqual(0, marginCallOrders.Count);
            Assert.AreEqual(-security.Holdings.Quantity, marginCallOrders[0].Quantity); // we bought twice
            Assert.GreaterOrEqual(-portfolio.MarginRemaining, security.Price * marginCallOrders[0].Quantity);
        }

        [Test]
        public void MarginComputesProperlyWithMultipleSecurities()
        {
            var securities = new SecurityManager(TimeKeeper);
            var transactions = new SecurityTransactionManager(null, securities);
            var orderProcessor = new OrderProcessor();
            transactions.SetOrderProcessor(orderProcessor);
            var portfolio = new SecurityPortfolioManager(securities, transactions);
            portfolio.CashBook["USD"].SetAmount(1000);
            portfolio.CashBook.Add("EUR",  1000, 1.1m);
            portfolio.CashBook.Add("GBP", -1000, 2.0m);

            var eurCash = portfolio.CashBook["EUR"];
            var gbpCash = portfolio.CashBook["GBP"];
            var usdCash = portfolio.CashBook["USD"];

            var time = DateTime.Now;
            var config1 = CreateTradeBarDataConfig(SecurityType.Equity, Symbols.AAPL);
            securities.Add(new Security(SecurityExchangeHours, config1, new Cash(CashBook.AccountCurrency, 0, 1m), SymbolProperties.GetDefault(CashBook.AccountCurrency)));
            securities[Symbols.AAPL].SetLeverage(2m);
            securities[Symbols.AAPL].Holdings.SetHoldings(100, 100);
            securities[Symbols.AAPL].SetMarketPrice(new TradeBar{Time = time, Value = 100});

            var config2 = CreateTradeBarDataConfig(SecurityType.Forex, Symbols.EURUSD);
            securities.Add(new QuantConnect.Securities.Forex.Forex(SecurityExchangeHours, usdCash, config2, SymbolProperties.GetDefault(CashBook.AccountCurrency)));
            securities[Symbols.EURUSD].SetLeverage(100m);
            securities[Symbols.EURUSD].Holdings.SetHoldings(1.1m, 1000);
            securities[Symbols.EURUSD].SetMarketPrice(new TradeBar { Time = time, Value = 1.1m });

            var config3 = CreateTradeBarDataConfig(SecurityType.Forex, Symbols.EURGBP);
            securities.Add(new QuantConnect.Securities.Forex.Forex(SecurityExchangeHours, gbpCash, config3, SymbolProperties.GetDefault(gbpCash.Symbol)));
            securities[Symbols.EURGBP].SetLeverage(100m);
            securities[Symbols.EURGBP].Holdings.SetHoldings(1m, 1000);
            securities[Symbols.EURGBP].SetMarketPrice(new TradeBar { Time = time, Value = 1m });

            var acceptedOrder = new MarketOrder(Symbols.AAPL, 101, DateTime.Now) { Price = 100 };
            orderProcessor.AddOrder(acceptedOrder);
            var request = new SubmitOrderRequest(OrderType.Market, acceptedOrder.SecurityType, acceptedOrder.Symbol, acceptedOrder.Quantity, 0, 0, acceptedOrder.Time, null);
            request.SetOrderId(0);
            orderProcessor.AddTicket(new OrderTicket(null, request));
            var security = securities[Symbols.AAPL];
            var hasSufficientBuyingPower = security.BuyingPowerModel.HasSufficientBuyingPowerForOrder(portfolio, security, acceptedOrder).IsSufficient;
            Assert.IsTrue(hasSufficientBuyingPower);

            var rejectedOrder = new MarketOrder(Symbols.AAPL, 102, DateTime.Now) { Price = 100 };
            hasSufficientBuyingPower = security.BuyingPowerModel.HasSufficientBuyingPowerForOrder(portfolio, security, rejectedOrder).IsSufficient;
            Assert.IsFalse(hasSufficientBuyingPower);
        }

        [Test]
        public void BuyingSellingFuturesDoesntAddToCash()
        {
            var securities = new SecurityManager(TimeKeeper);
            var transactions = new SecurityTransactionManager(null, securities);
            var portfolio = new SecurityPortfolioManager(securities, transactions);
            portfolio.SetCash(0);

            securities.Add(Symbols.Fut_SPY_Feb19_2016, new Security(SecurityExchangeHours, CreateTradeBarDataConfig(SecurityType.Future, Symbols.Fut_SPY_Feb19_2016), new Cash(CashBook.AccountCurrency, 0, 1m), SymbolProperties.GetDefault(CashBook.AccountCurrency)));

            var fillBuy = new OrderEvent(1, Symbols.Fut_SPY_Feb19_2016, DateTime.MinValue, OrderStatus.Filled, OrderDirection.Buy, 100, 100, 0);
            portfolio.ProcessFill(fillBuy);

            Assert.AreEqual(0, portfolio.Cash);
            Assert.AreEqual(100, securities[Symbols.Fut_SPY_Feb19_2016].Holdings.Quantity);

            var fillSell = new OrderEvent(2, Symbols.Fut_SPY_Feb19_2016, DateTime.MinValue, OrderStatus.Filled, OrderDirection.Sell, 100, -100, 0);
            portfolio.ProcessFill(fillSell);

            Assert.AreEqual(0, portfolio.Cash);
            Assert.AreEqual(0, securities[Symbols.Fut_SPY_Feb19_2016].Holdings.Quantity);
        }

        [Test]
        public void BuyingSellingFuturesAddsToCashOnClose()
        {
            var securities = new SecurityManager(TimeKeeper);
            var transactions = new SecurityTransactionManager(null, securities);
            var portfolio = new SecurityPortfolioManager(securities, transactions);
            portfolio.SetCash(0);

            securities.Add(Symbols.Fut_SPY_Feb19_2016, new Security(SecurityExchangeHours, CreateTradeBarDataConfig(SecurityType.Future, Symbols.Fut_SPY_Feb19_2016), new Cash(CashBook.AccountCurrency, 0, 1m), new SymbolProperties("", CashBook.AccountCurrency.ToUpper(), 50, 0.01m, 1)));

            var fillBuy = new OrderEvent(1, Symbols.Fut_SPY_Feb19_2016, DateTime.MinValue, OrderStatus.Filled, OrderDirection.Buy, 100, 100, 0);
            portfolio.ProcessFill(fillBuy);

            Assert.AreEqual(0, portfolio.Cash);
            Assert.AreEqual(100, securities[Symbols.Fut_SPY_Feb19_2016].Holdings.Quantity);

            var fillSell = new OrderEvent(2, Symbols.Fut_SPY_Feb19_2016, DateTime.MinValue, OrderStatus.Filled, OrderDirection.Sell, 99, -100, 0);
            portfolio.ProcessFill(fillSell);

            Assert.AreEqual(-100 * 50, portfolio.Cash);
            Assert.AreEqual(0, securities[Symbols.Fut_SPY_Feb19_2016].Holdings.Quantity);
        }

        [Test]
        public void BuyingSellingFuturesAddsCorrectSales()
        {
            var securities = new SecurityManager(TimeKeeper);
            var transactions = new SecurityTransactionManager(null, securities);
            var portfolio = new SecurityPortfolioManager(securities, transactions);
            portfolio.SetCash(0);

            securities.Add(Symbols.Fut_SPY_Feb19_2016, new Security(SecurityExchangeHours, CreateTradeBarDataConfig(SecurityType.Future, Symbols.Fut_SPY_Feb19_2016), new Cash(CashBook.AccountCurrency, 0, 1m), new SymbolProperties("", CashBook.AccountCurrency.ToUpper(), 50, 0.01m, 1)));

            var fillBuy = new OrderEvent(1, Symbols.Fut_SPY_Feb19_2016, DateTime.MinValue, OrderStatus.Filled, OrderDirection.Buy, 100, 100, 0);
            portfolio.ProcessFill(fillBuy);

            Assert.AreEqual(100 * 100, securities[Symbols.Fut_SPY_Feb19_2016].Holdings.TotalSaleVolume);

            var fillSell = new OrderEvent(2, Symbols.Fut_SPY_Feb19_2016, DateTime.MinValue, OrderStatus.Filled, OrderDirection.Sell, 100, -100, 0);
            portfolio.ProcessFill(fillSell);

            Assert.AreEqual(2 * 100 * 100, securities[Symbols.Fut_SPY_Feb19_2016].Holdings.TotalSaleVolume);
        }

        [Test]
        public void SellingShortFromZeroAddsToCash()
        {
            var securities = new SecurityManager(TimeKeeper);
            var transactions = new SecurityTransactionManager(null, securities);
            var portfolio = new SecurityPortfolioManager(securities, transactions);
            portfolio.SetCash(0);

            securities.Add(Symbols.AAPL, new Security(SecurityExchangeHours, CreateTradeBarDataConfig(SecurityType.Equity, Symbols.AAPL), new Cash(CashBook.AccountCurrency, 0, 1m), SymbolProperties.GetDefault(CashBook.AccountCurrency)));

            var fill = new OrderEvent(1, Symbols.AAPL, DateTime.MinValue, OrderStatus.Filled, OrderDirection.Sell,  100, -100, 0);
            portfolio.ProcessFill(fill);

            Assert.AreEqual(100 * 100, portfolio.Cash);
            Assert.AreEqual(-100, securities[Symbols.AAPL].Holdings.Quantity);
        }

        [Test]
        public void SellingShortFromLongAddsToCash()
        {
            var securities = new SecurityManager(TimeKeeper);
            var transactions = new SecurityTransactionManager(null, securities);
            var portfolio = new SecurityPortfolioManager(securities, transactions);
            portfolio.SetCash(0);

            securities.Add(Symbols.AAPL, new Security(SecurityExchangeHours, CreateTradeBarDataConfig(SecurityType.Equity, Symbols.AAPL), new Cash(CashBook.AccountCurrency, 0, 1m), SymbolProperties.GetDefault(CashBook.AccountCurrency)));
            securities[Symbols.AAPL].Holdings.SetHoldings(100, 100);

            var fill = new OrderEvent(1, Symbols.AAPL, DateTime.MinValue, OrderStatus.Filled, OrderDirection.Sell,  100, -100, 0);
            portfolio.ProcessFill(fill);

            Assert.AreEqual(100 * 100, portfolio.Cash);
            Assert.AreEqual(0, securities[Symbols.AAPL].Holdings.Quantity);
        }

        [Test]
        public void SellingShortFromShortAddsToCash()
        {
            var securities = new SecurityManager(TimeKeeper);
            var transactions = new SecurityTransactionManager(null, securities);
            var portfolio = new SecurityPortfolioManager(securities, transactions);
            portfolio.SetCash(0);

            securities.Add(Symbols.AAPL, new Security(SecurityExchangeHours, CreateTradeBarDataConfig(SecurityType.Equity, Symbols.AAPL), new Cash(CashBook.AccountCurrency, 0, 1m), SymbolProperties.GetDefault(CashBook.AccountCurrency)));
            securities[Symbols.AAPL].Holdings.SetHoldings(100, -100);

            var fill = new OrderEvent(1, Symbols.AAPL, DateTime.MinValue,  OrderStatus.Filled, OrderDirection.Sell,  100, -100, 0);
            Assert.AreEqual(-100, securities[Symbols.AAPL].Holdings.Quantity);
            portfolio.ProcessFill(fill);

            Assert.AreEqual(100 * 100, portfolio.Cash);
            Assert.AreEqual(-200, securities[Symbols.AAPL].Holdings.Quantity);
        }

        [Test]
        public void ForexFillUpdatesCashCorrectly()
        {
            var securities = new SecurityManager(TimeKeeper);
            var transactions = new SecurityTransactionManager(null, securities);
            var portfolio = new SecurityPortfolioManager(securities, transactions);
            portfolio.SetCash(1000);
            portfolio.CashBook.Add("EUR", 0, 1.1000m);

            securities.Add(Symbols.EURUSD, new QuantConnect.Securities.Forex.Forex(SecurityExchangeHours, portfolio.CashBook["USD"], CreateTradeBarDataConfig(SecurityType.Forex, Symbols.EURUSD), SymbolProperties.GetDefault(CashBook.AccountCurrency)));
            var security = securities[Symbols.EURUSD];
            Assert.AreEqual(0, security.Holdings.Quantity);
            Assert.AreEqual(1000, portfolio.Cash);

            var orderFee = security.FeeModel.GetOrderFee(security, new MarketOrder(Symbols.EURUSD, 100, DateTime.MinValue));
            var fill = new OrderEvent(1, Symbols.EURUSD, DateTime.MinValue, OrderStatus.Filled, OrderDirection.Buy, 1.1000m, 100, orderFee);
            portfolio.ProcessFill(fill);
            Assert.AreEqual(100, security.Holdings.Quantity);
            Assert.AreEqual(998, portfolio.Cash);
            Assert.AreEqual(100, portfolio.CashBook["EUR"].Amount);
            Assert.AreEqual(888, portfolio.CashBook["USD"].Amount);
        }

        [Test]
        public void CryptoFillUpdatesCashCorrectly()
        {
            var securities = new SecurityManager(TimeKeeper);
            var transactions = new SecurityTransactionManager(null, securities);
            var portfolio = new SecurityPortfolioManager(securities, transactions);
            portfolio.SetCash(10000);
            portfolio.CashBook.Add("BTC", 0, 4000.01m);

            securities.Add(Symbols.BTCUSD, new QuantConnect.Securities.Crypto.Crypto(SecurityExchangeHours, portfolio.CashBook["USD"], CreateTradeBarDataConfig(SecurityType.Crypto,
                Symbols.BTCUSD), SymbolProperties.GetDefault(CashBook.AccountCurrency)));
            var security = securities[Symbols.BTCUSD];
            Assert.AreEqual(0, security.Holdings.Quantity);
            Assert.AreEqual(10000, portfolio.Cash);

            var orderFee = security.FeeModel.GetOrderFee(security, new MarketOrder(Symbols.BTCUSD, 2, DateTime.MinValue));
            var fill = new OrderEvent(1, Symbols.BTCUSD, DateTime.MinValue, OrderStatus.Filled, OrderDirection.Buy, 4000.01m, 2, orderFee);
            portfolio.ProcessFill(fill);
            Assert.AreEqual(2, security.Holdings.Quantity);
            Assert.AreEqual(10000, portfolio.Cash);
            Assert.AreEqual(2, portfolio.CashBook["BTC"].Amount);
            Assert.AreEqual(1999.98, portfolio.CashBook["USD"].Amount);
        }

        [Test]
        public void EquitySellAppliesSettlementCorrectly()
        {
            var securityExchangeHours = SecurityExchangeHoursTests.CreateUsEquitySecurityExchangeHours();
            var securities = new SecurityManager(TimeKeeper);
            var transactions = new SecurityTransactionManager(null, securities);
            var portfolio = new SecurityPortfolioManager(securities, transactions);
            portfolio.SetCash(1000);
            securities.Add(Symbols.AAPL, new QuantConnect.Securities.Equity.Equity(securityExchangeHours, CreateTradeBarDataConfig(SecurityType.Equity, Symbols.AAPL), new Cash(CashBook.AccountCurrency, 0, 1m), SymbolProperties.GetDefault(CashBook.AccountCurrency)));
            var security = securities[Symbols.AAPL];
            security.SettlementModel = new DelayedSettlementModel(3, TimeSpan.FromHours(8));
            Assert.AreEqual(0, security.Holdings.Quantity);
            Assert.AreEqual(1000, portfolio.Cash);
            Assert.AreEqual(0, portfolio.UnsettledCash);

            // Buy on Monday
            var timeUtc = new DateTime(2015, 10, 26, 15, 30, 0);
            var orderFee = security.FeeModel.GetOrderFee(security,new MarketOrder(Symbols.AAPL, 10, timeUtc));
            var fill = new OrderEvent(1, Symbols.AAPL, timeUtc, OrderStatus.Filled, OrderDirection.Buy, 100, 10, orderFee);
            portfolio.ProcessFill(fill);
            Assert.AreEqual(10, security.Holdings.Quantity);
            Assert.AreEqual(-1, portfolio.Cash);
            Assert.AreEqual(0, portfolio.UnsettledCash);

            // Sell on Tuesday, cash unsettled
            timeUtc = timeUtc.AddDays(1);
            orderFee = security.FeeModel.GetOrderFee(security, new MarketOrder(Symbols.AAPL, 10, timeUtc));
            fill = new OrderEvent(2, Symbols.AAPL, timeUtc, OrderStatus.Filled, OrderDirection.Sell, 100, -10, orderFee);
            portfolio.ProcessFill(fill);
            Assert.AreEqual(0, security.Holdings.Quantity);
            Assert.AreEqual(-2, portfolio.Cash);
            Assert.AreEqual(1000, portfolio.UnsettledCash);

            // Thursday, still cash unsettled
            timeUtc = timeUtc.AddDays(2);
            portfolio.ScanForCashSettlement(timeUtc);
            Assert.AreEqual(-2, portfolio.Cash);
            Assert.AreEqual(1000, portfolio.UnsettledCash);

            // Friday at open, cash settled
            var marketOpen = securityExchangeHours.MarketHours[timeUtc.DayOfWeek].GetMarketOpen(TimeSpan.Zero, false);
            Assert.IsTrue(marketOpen.HasValue);
            timeUtc = timeUtc.AddDays(1).Date.Add(marketOpen.Value).ConvertToUtc(securityExchangeHours.TimeZone);
            portfolio.ScanForCashSettlement(timeUtc);
            Assert.AreEqual(998, portfolio.Cash);
            Assert.AreEqual(0, portfolio.UnsettledCash);
        }

        [Test]
        public void ComputeMarginProperlyLongSellZeroShort()
        {
            const decimal leverage = 2m;
            const int amount = 1000;
            const int quantity = (int)(amount * leverage);
            var securities = new SecurityManager(TimeKeeper);
            var transactions = new SecurityTransactionManager(null, securities);
            var orderProcessor = new OrderProcessor();
            transactions.SetOrderProcessor(orderProcessor);
            var portfolio = new SecurityPortfolioManager(securities, transactions);
            portfolio.CashBook["USD"].SetAmount(amount);

            var config = CreateTradeBarDataConfig(SecurityType.Equity, Symbols.AAPL);
            securities.Add(new Security(SecurityExchangeHours, config, new Cash(CashBook.AccountCurrency, 0, 1m), SymbolProperties.GetDefault(CashBook.AccountCurrency)));
            var security = securities[Symbols.AAPL];
            security.SetLeverage(leverage);

            var time = DateTime.Now;
            const decimal buyPrice = 1m;
            security.SetMarketPrice(new TradeBar(time, Symbols.AAPL, buyPrice, buyPrice, buyPrice, buyPrice, 1));

            var order = new MarketOrder(Symbols.AAPL, quantity, time) { Price = buyPrice };
            var fill = new OrderEvent(order, DateTime.UtcNow, 0) { FillPrice = buyPrice, FillQuantity = quantity };
            orderProcessor.AddOrder(order);
            var request = new SubmitOrderRequest(OrderType.Market, security.Type, security.Symbol, order.Quantity, 0, 0, order.Time, null);
            request.SetOrderId(0);
            orderProcessor.AddTicket(new OrderTicket(null, request));

            portfolio.ProcessFill(fill);

            // we shouldn't be able to place a new buy order
            var newOrder = new MarketOrder(Symbols.AAPL, 1, time.AddSeconds(1)) { Price = buyPrice };
            var hasSufficientBuyingPower = security.BuyingPowerModel.HasSufficientBuyingPowerForOrder(portfolio, security, newOrder).IsSufficient;
            Assert.IsFalse(hasSufficientBuyingPower);

            // we should be able to place sell to zero
            newOrder = new MarketOrder(Symbols.AAPL, -quantity, time.AddSeconds(1)) { Price = buyPrice };
            hasSufficientBuyingPower = security.BuyingPowerModel.HasSufficientBuyingPowerForOrder(portfolio, security, newOrder).IsSufficient;
            Assert.IsTrue(hasSufficientBuyingPower);

            // now the stock plummets, so we should have negative margin remaining
            time = time.AddDays(1);
            const decimal lowPrice = buyPrice / 2;
            security.SetMarketPrice(new TradeBar(time, Symbols.AAPL, lowPrice, lowPrice, lowPrice, lowPrice, 1));

            // we still should be able to place sell to zero
            newOrder = new MarketOrder(Symbols.AAPL, -quantity, time.AddSeconds(1)) { Price = lowPrice };
            hasSufficientBuyingPower = security.BuyingPowerModel.HasSufficientBuyingPowerForOrder(portfolio, security, newOrder).IsSufficient;
            Assert.IsTrue(hasSufficientBuyingPower);

            // we shouldn't be able to place sell to short
            newOrder = new MarketOrder(Symbols.AAPL, -quantity - 1, time.AddSeconds(1)) { Price = lowPrice };
            hasSufficientBuyingPower = security.BuyingPowerModel.HasSufficientBuyingPowerForOrder(portfolio, security, newOrder).IsSufficient;
            Assert.IsFalse(hasSufficientBuyingPower);
        }

        [Test]
        public void ComputeMarginProperlyShortCoverZeroLong()
        {
            const decimal leverage = 2m;
            const int amount = 1000;
            const int quantity = (int)(amount * leverage);
            var securities = new SecurityManager(TimeKeeper);
            var transactions = new SecurityTransactionManager(null, securities);
            var orderProcessor = new OrderProcessor();
            transactions.SetOrderProcessor(orderProcessor);
            var portfolio = new SecurityPortfolioManager(securities, transactions);
            portfolio.CashBook["USD"].SetAmount(amount);

            var config = CreateTradeBarDataConfig(SecurityType.Equity, Symbols.AAPL);
            securities.Add(new Security(SecurityExchangeHours, config, new Cash(CashBook.AccountCurrency, 0, 1m), SymbolProperties.GetDefault(CashBook.AccountCurrency)));
            var security = securities[Symbols.AAPL];
            security.SetLeverage(leverage);

            var time = DateTime.Now;
            const decimal sellPrice = 1m;
            security.SetMarketPrice(new TradeBar(time, Symbols.AAPL, sellPrice, sellPrice, sellPrice, sellPrice, 1));

            var order = new MarketOrder(Symbols.AAPL, -quantity, time) { Price = sellPrice };
            var fill = new OrderEvent(order, DateTime.UtcNow, 0) { FillPrice = sellPrice, FillQuantity = -quantity };
            orderProcessor.AddOrder(order);
            var request = new SubmitOrderRequest(OrderType.Market, security.Type, security.Symbol, order.Quantity, 0, 0, order.Time, null);
            request.SetOrderId(0);
            orderProcessor.AddTicket(new OrderTicket(null, request));

            portfolio.ProcessFill(fill);

            // we shouldn't be able to place a new short order
            var newOrder = new MarketOrder(Symbols.AAPL, -1, time.AddSeconds(1)) { Price = sellPrice };
            var hasSufficientBuyingPower = security.BuyingPowerModel.HasSufficientBuyingPowerForOrder(portfolio, security, newOrder).IsSufficient;
            Assert.IsFalse(hasSufficientBuyingPower);

            // we should be able to place cover to zero
            newOrder = new MarketOrder(Symbols.AAPL, quantity, time.AddSeconds(1)) { Price = sellPrice };
            hasSufficientBuyingPower = security.BuyingPowerModel.HasSufficientBuyingPowerForOrder(portfolio, security, newOrder).IsSufficient;
            Assert.IsTrue(hasSufficientBuyingPower);

            // now the stock doubles, so we should have negative margin remaining
            time = time.AddDays(1);
            const decimal highPrice = sellPrice * 2;
            security.SetMarketPrice(new TradeBar(time, Symbols.AAPL, highPrice, highPrice, highPrice, highPrice, 1));

            // we still shouldn be able to place cover to zero
            newOrder = new MarketOrder(Symbols.AAPL, quantity, time.AddSeconds(1)) { Price = highPrice };
            hasSufficientBuyingPower = security.BuyingPowerModel.HasSufficientBuyingPowerForOrder(portfolio, security, newOrder).IsSufficient;
            Assert.IsTrue(hasSufficientBuyingPower);

            // we shouldn't be able to place cover to long
            newOrder = new MarketOrder(Symbols.AAPL, quantity + 1, time.AddSeconds(1)) { Price = highPrice };
            hasSufficientBuyingPower = security.BuyingPowerModel.HasSufficientBuyingPowerForOrder(portfolio, security, newOrder).IsSufficient;
            Assert.IsFalse(hasSufficientBuyingPower);
        }

        [Test]
        public void FullExerciseCallAddsUnderlyingPositionReducesCash()
        {
            var algorithm = new QCAlgorithm();
            var securities = new SecurityManager(TimeKeeper);
            var transactions = new SecurityTransactionManager(null, securities);
            var transactionHandler = new BacktestingTransactionHandler();
            var portfolio = new SecurityPortfolioManager(securities, transactions);

            algorithm.Securities = securities;
            transactionHandler.Initialize(algorithm, new BacktestingBrokerage(algorithm), new TestResultHandler(Console.WriteLine));
            transactions.SetOrderProcessor(transactionHandler);

            // Adding cash: strike price times number of shares
            portfolio.SetCash(192 * 100);

            securities.Add(Symbols.SPY, new Security(SecurityExchangeHours, CreateTradeBarDataConfig(SecurityType.Equity, Symbols.SPY), new Cash(CashBook.AccountCurrency, 0, 1m), SymbolProperties.GetDefault(CashBook.AccountCurrency)));
            securities.Add(Symbols.SPY_C_192_Feb19_2016, new Option(SecurityExchangeHours, CreateTradeBarDataConfig(SecurityType.Equity, Symbols.SPY_C_192_Feb19_2016), new Cash(CashBook.AccountCurrency, 0, 1m), GetOptionSymbolProperties(Symbols.SPY_C_192_Feb19_2016)));
            securities[Symbols.SPY_C_192_Feb19_2016].Holdings.SetHoldings(1, 1);
            securities[Symbols.SPY].SetMarketPrice(new Tick { Value = 200 });

            transactions.AddOrder(new SubmitOrderRequest(OrderType.OptionExercise, SecurityType.Option, Symbols.SPY_C_192_Feb19_2016, 1, 0, 0, securities.UtcTime, ""));
            var option = (Option)securities[Symbols.SPY_C_192_Feb19_2016];
            var order = (OptionExerciseOrder)transactions.GetOrders(x => true).First();
            option.Underlying = securities[Symbols.SPY];

            var fills = option.OptionExerciseModel.OptionExercise(option, order);

            foreach (var fill in fills)
            {
                portfolio.ProcessFill(fill);
            }

            // now we have long position in SPY with average price equal to strike
            var newUnderlyingHoldings = securities[Symbols.SPY].Holdings;
            Assert.AreEqual(0, portfolio.Cash);
            Assert.AreEqual(100, newUnderlyingHoldings.Quantity);
            Assert.AreEqual(192.0, newUnderlyingHoldings.AveragePrice);

            // and long call option position has disappeared
            Assert.AreEqual(0, securities[Symbols.SPY_C_192_Feb19_2016].Holdings.Quantity);
        }

        [Test]
        public void ExerciseOTMCallDoesntChangeAnything()
        {
            var algorithm = new QCAlgorithm();
            var securities = new SecurityManager(TimeKeeper);
            var transactions = new SecurityTransactionManager(null, securities);
            var transactionHandler = new BacktestingTransactionHandler();
            var portfolio = new SecurityPortfolioManager(securities, transactions);

            algorithm.Securities = securities;
            transactionHandler.Initialize(algorithm, new BacktestingBrokerage(algorithm), new TestResultHandler(Console.WriteLine));
            transactions.SetOrderProcessor(transactionHandler);

            portfolio.SetCash(0);

            securities.Add(Symbols.SPY, new Security(SecurityExchangeHours, CreateTradeBarDataConfig(SecurityType.Equity, Symbols.SPY), new Cash(CashBook.AccountCurrency, 0, 1m), SymbolProperties.GetDefault(CashBook.AccountCurrency)));
            securities.Add(Symbols.SPY_C_192_Feb19_2016, new Option(SecurityExchangeHours, CreateTradeBarDataConfig(SecurityType.Equity, Symbols.SPY_C_192_Feb19_2016), new Cash(CashBook.AccountCurrency, 0, 1m), GetOptionSymbolProperties(Symbols.SPY_C_192_Feb19_2016)));
            securities[Symbols.SPY_C_192_Feb19_2016].Holdings.SetHoldings(1, 100);
            securities[Symbols.SPY].SetMarketPrice(new Tick { Value = 20 });

            transactions.AddOrder(new SubmitOrderRequest(OrderType.OptionExercise, SecurityType.Option, Symbols.SPY_C_192_Feb19_2016, 100, 0, 0, securities.UtcTime, ""));
            var option = (Option)securities[Symbols.SPY_C_192_Feb19_2016];
            var order = (OptionExerciseOrder)transactions.GetOrders(x => true).First();
            option.Underlying = securities[Symbols.SPY];

            var fills = option.OptionExerciseModel.OptionExercise(option, order);

            foreach (var fill in fills)
            {
                portfolio.ProcessFill(fill);
            }
            // now we have long position in SPY with average price equal to strike
            var newUnderlyingHoldings = securities[Symbols.SPY].Holdings;
            Assert.AreEqual(0, portfolio.Cash);
            Assert.AreEqual(0, newUnderlyingHoldings.Quantity);
            Assert.AreEqual(0, newUnderlyingHoldings.AveragePrice);

            // and long call option position has disappeared
            Assert.AreEqual(0, securities[Symbols.SPY_C_192_Feb19_2016].Holdings.Quantity);
        }

        [Test]
        public void CashSettledExerciseOTMPutDoesntChangeAnything()
        {
            var algorithm = new QCAlgorithm();
            var securities = new SecurityManager(TimeKeeper);
            var transactions = new SecurityTransactionManager(null, securities);
            var transactionHandler = new BacktestingTransactionHandler();
            var portfolio = new SecurityPortfolioManager(securities, transactions);

            algorithm.Securities = securities;
            transactionHandler.Initialize(algorithm, new BacktestingBrokerage(algorithm), new TestResultHandler(Console.WriteLine));
            transactions.SetOrderProcessor(transactionHandler);

            portfolio.SetCash(0);

            securities.Add(Symbols.SPY, new Security(SecurityExchangeHours, CreateTradeBarDataConfig(SecurityType.Equity, Symbols.SPY), new Cash(CashBook.AccountCurrency, 0, 1m), SymbolProperties.GetDefault(CashBook.AccountCurrency)));
            securities.Add(Symbols.SPY_P_192_Feb19_2016, new Option(SecurityExchangeHours, CreateTradeBarDataConfig(SecurityType.Equity, Symbols.SPY_P_192_Feb19_2016), new Cash(CashBook.AccountCurrency, 0, 1m), GetOptionSymbolProperties(Symbols.SPY_P_192_Feb19_2016)));
            securities[Symbols.SPY_P_192_Feb19_2016].Holdings.SetHoldings(1, 100);
            securities[Symbols.SPY].SetMarketPrice(new Tick { Value = 2000 });

            transactions.AddOrder(new SubmitOrderRequest(OrderType.OptionExercise, SecurityType.Option, Symbols.SPY_P_192_Feb19_2016, 100, 0, 0, securities.UtcTime, ""));
            var option = (Option)securities[Symbols.SPY_P_192_Feb19_2016];
            var order = (OptionExerciseOrder)transactions.GetOrders(x => true).First();
            option.Underlying = securities[Symbols.SPY];
            option.ExerciseSettlement = SettlementType.Cash;

            var fills = option.OptionExerciseModel.OptionExercise(option, order);

            foreach (var fill in fills)
            {
                portfolio.ProcessFill(fill);
            }

            // now we have long position in SPY with average price equal to strike
            var newUnderlyingHoldings = securities[Symbols.SPY].Holdings;
            Assert.AreEqual(0, portfolio.Cash);
            Assert.AreEqual(0, newUnderlyingHoldings.Quantity);
            Assert.AreEqual(0, newUnderlyingHoldings.AveragePrice);

            // and long call option position has disappeared
            Assert.AreEqual(0, securities[Symbols.SPY_P_192_Feb19_2016].Holdings.Quantity);
        }

        [Test]
        public void FullExercisePutAddsUnderlyingPositionAddsCash()
        {
            var algorithm = new QCAlgorithm();
            var securities = new SecurityManager(TimeKeeper);
            var transactions = new SecurityTransactionManager(null, securities);
            var transactionHandler = new BacktestingTransactionHandler();
            var portfolio = new SecurityPortfolioManager(securities, transactions);

            algorithm.Securities = securities;
            transactionHandler.Initialize(algorithm, new BacktestingBrokerage(algorithm), new TestResultHandler(Console.WriteLine));
            transactions.SetOrderProcessor(transactionHandler);
            portfolio.SetCash(0);

            securities.Add(Symbols.SPY, new Security(SecurityExchangeHours, CreateTradeBarDataConfig(SecurityType.Equity, Symbols.SPY), new Cash(CashBook.AccountCurrency, 0, 1m), SymbolProperties.GetDefault(CashBook.AccountCurrency)));
            securities.Add(Symbols.SPY_P_192_Feb19_2016, new Option(SecurityExchangeHours, CreateTradeBarDataConfig(SecurityType.Equity, Symbols.SPY_P_192_Feb19_2016), new Cash(CashBook.AccountCurrency, 0, 1m), GetOptionSymbolProperties(Symbols.SPY_P_192_Feb19_2016)));
            securities[Symbols.SPY_P_192_Feb19_2016].Holdings.SetHoldings(1, 1);

            transactions.AddOrder(new SubmitOrderRequest(OrderType.OptionExercise, SecurityType.Option, Symbols.SPY_P_192_Feb19_2016, 1, 0, 0, securities.UtcTime, ""));
            var option = (Option)securities[Symbols.SPY_P_192_Feb19_2016];
            var order = (OptionExerciseOrder)transactions.GetOrders(x => true).First();
            option.Underlying = securities[Symbols.SPY];

            var fills = option.OptionExerciseModel.OptionExercise(option, order);

            foreach (var fill in fills)
            {
                portfolio.ProcessFill(fill);
            }

            // now we have short position in SPY with average price equal to strike
            // and cash amount equal to strike price times number of shares
            var newUnderlyingHoldings = securities[Symbols.SPY].Holdings;
            Assert.AreEqual(192 * 100, portfolio.Cash);
            Assert.AreEqual(-100, newUnderlyingHoldings.Quantity);
            Assert.AreEqual(192.0, newUnderlyingHoldings.AveragePrice);

            // and long put option position has disappeared
            Assert.AreEqual(0, securities[Symbols.SPY_P_192_Feb19_2016].Holdings.Quantity);
        }

        [Test]
        public void PartialExerciseCallAddsUnderlyingPositionReducesCash()
        {
            var algorithm = new QCAlgorithm();
            var securities = new SecurityManager(TimeKeeper);
            var transactions = new SecurityTransactionManager(null, securities);
            var transactionHandler = new BacktestingTransactionHandler();
            var portfolio = new SecurityPortfolioManager(securities, transactions);

            algorithm.Securities = securities;
            transactionHandler.Initialize(algorithm, new BacktestingBrokerage(algorithm), new TestResultHandler(Console.WriteLine));
            transactions.SetOrderProcessor(transactionHandler);

            // Adding cash: strike price times number of shares
            portfolio.SetCash(192 * 100);

            securities.Add(Symbols.SPY, new Security(SecurityExchangeHours, CreateTradeBarDataConfig(SecurityType.Equity, Symbols.SPY), new Cash(CashBook.AccountCurrency, 0, 1m), SymbolProperties.GetDefault(CashBook.AccountCurrency)));
            securities.Add(Symbols.SPY_C_192_Feb19_2016, new Option(SecurityExchangeHours, CreateTradeBarDataConfig(SecurityType.Equity, Symbols.SPY_C_192_Feb19_2016), new Cash(CashBook.AccountCurrency, 0, 1m), GetOptionSymbolProperties(Symbols.SPY_C_192_Feb19_2016)));
            securities[Symbols.SPY_C_192_Feb19_2016].Holdings.SetHoldings(1, 2);
            securities[Symbols.SPY].SetMarketPrice(new Tick { Value = 200 });

            transactions.AddOrder(new SubmitOrderRequest(OrderType.OptionExercise, SecurityType.Option, Symbols.SPY_C_192_Feb19_2016, 1, 0, 0, securities.UtcTime, ""));
            var option = (Option)securities[Symbols.SPY_C_192_Feb19_2016];
            var order = (OptionExerciseOrder)transactions.GetOrders(x => true).First();
            option.Underlying = securities[Symbols.SPY];

            var fills = option.OptionExerciseModel.OptionExercise(option, order);

            foreach (var fill in fills)
            {
                portfolio.ProcessFill(fill);
            }

            // now we have long position in SPY with average price equal to strike
            var newUnderlyingHoldings = securities[Symbols.SPY].Holdings;
            Assert.AreEqual(0, portfolio.Cash);
            Assert.AreEqual(100, newUnderlyingHoldings.Quantity);
            Assert.AreEqual(192.0, newUnderlyingHoldings.AveragePrice);

            // and call option position still has some value
            Assert.AreEqual(1, securities[Symbols.SPY_C_192_Feb19_2016].Holdings.Quantity);
        }

        [Test]
        public void InternalCallAssignmentAddsUnderlyingPositionAddsCash()
        {
            var algorithm = new QCAlgorithm();
            var securities = new SecurityManager(TimeKeeper);
            var transactions = new SecurityTransactionManager(null, securities);
            var transactionHandler = new BacktestingTransactionHandler();
            var portfolio = new SecurityPortfolioManager(securities, transactions);

            algorithm.Securities = securities;
            transactionHandler.Initialize(algorithm, new BacktestingBrokerage(algorithm), new TestResultHandler(Console.WriteLine));
            transactions.SetOrderProcessor(transactionHandler);
            portfolio.SetCash(0);

            securities.Add(Symbols.SPY, new Security(SecurityExchangeHours, CreateTradeBarDataConfig(SecurityType.Equity, Symbols.SPY), new Cash(CashBook.AccountCurrency, 0, 1m), SymbolProperties.GetDefault(CashBook.AccountCurrency)));
            securities.Add(Symbols.SPY_C_192_Feb19_2016, new Option(SecurityExchangeHours, CreateTradeBarDataConfig(SecurityType.Equity, Symbols.SPY_C_192_Feb19_2016), new Cash(CashBook.AccountCurrency, 0, 1m), GetOptionSymbolProperties(Symbols.SPY_C_192_Feb19_2016)));
            securities[Symbols.SPY_C_192_Feb19_2016].Holdings.SetHoldings(1, -1);
            securities[Symbols.SPY].SetMarketPrice(new Tick { Value = 200 });

            transactions.AddOrder(new SubmitOrderRequest(OrderType.OptionExercise, SecurityType.Option, Symbols.SPY_C_192_Feb19_2016, -1, 0, 0, securities.UtcTime, ""));
            var option = (Option)securities[Symbols.SPY_C_192_Feb19_2016];
            var order = (OptionExerciseOrder)transactions.GetOrders(x => true).First();
            option.Underlying = securities[Symbols.SPY];

            var fills = option.OptionExerciseModel.OptionExercise(option, order);

            // we are simulating assignment by calling a method for this
            var portfolioModel = (OptionPortfolioModel)option.PortfolioModel;

            foreach (var fill in fills)
            {
                portfolioModel.ProcessFill(portfolio, option, fill);
            }

            // we just got assigned!
            // now we have short position in SPY with average price equal to strike
            // and cash amount equal to strike price times number of shares

            var newUnderlyingHoldings = securities[Symbols.SPY].Holdings;
            Assert.AreEqual(192 * 100, portfolio.Cash);
            Assert.AreEqual(-100, newUnderlyingHoldings.Quantity);
            Assert.AreEqual(192.0, newUnderlyingHoldings.AveragePrice);

            // and short call option position has disappeared
            Assert.AreEqual(0, securities[Symbols.SPY_C_192_Feb19_2016].Holdings.Quantity);
        }

        [Test]
        public void InternalPutAssignmentAddsUnderlyingPositionReducesCash()
        {
            var algorithm = new QCAlgorithm();
            var securities = new SecurityManager(TimeKeeper);
            var transactions = new SecurityTransactionManager(null, securities);
            var transactionHandler = new BacktestingTransactionHandler();
            var portfolio = new SecurityPortfolioManager(securities, transactions);

            algorithm.Securities = securities;
            transactionHandler.Initialize(algorithm, new BacktestingBrokerage(algorithm), new TestResultHandler(Console.WriteLine));
            transactions.SetOrderProcessor(transactionHandler);

            // Adding cash: strike price times number of shares
            portfolio.SetCash(192 * 100);

            securities.Add(Symbols.SPY, new Security(SecurityExchangeHours, CreateTradeBarDataConfig(SecurityType.Equity, Symbols.SPY), new Cash(CashBook.AccountCurrency, 0, 1m), SymbolProperties.GetDefault(CashBook.AccountCurrency)));
            securities.Add(Symbols.SPY_P_192_Feb19_2016, new Option(SecurityExchangeHours, CreateTradeBarDataConfig(SecurityType.Equity, Symbols.SPY_P_192_Feb19_2016), new Cash(CashBook.AccountCurrency, 0, 1m), GetOptionSymbolProperties(Symbols.SPY_P_192_Feb19_2016)));
            securities[Symbols.SPY_P_192_Feb19_2016].Holdings.SetHoldings(1, -1);

            transactions.AddOrder(new SubmitOrderRequest(OrderType.OptionExercise, SecurityType.Option, Symbols.SPY_P_192_Feb19_2016, -1, 0, 0, securities.UtcTime, ""));
            var option = (Option)securities[Symbols.SPY_P_192_Feb19_2016];
            var order = (OptionExerciseOrder)transactions.GetOrders(x => true).First();
            option.Underlying = securities[Symbols.SPY];

            var fills = option.OptionExerciseModel.OptionExercise(option, order);

            // we are simulating assignment by calling a method for this
            var portfolioModel = (OptionPortfolioModel)option.PortfolioModel;
            foreach (var fill in fills)
            {
                portfolioModel.ProcessFill(portfolio, option, fill);
            }

            // we just got assigned!
            // now we have long position in SPY with average price equal to strike
            var newUnderlyingHoldings = securities[Symbols.SPY].Holdings;
            Assert.AreEqual(0, portfolio.Cash);
            Assert.AreEqual(100, newUnderlyingHoldings.Quantity);
            Assert.AreEqual(192.0, newUnderlyingHoldings.AveragePrice);

            // and short put option position has disappeared
            Assert.AreEqual(0, securities[Symbols.SPY_P_192_Feb19_2016].Holdings.Quantity);
        }

        [Test]
        public void InternalPartialPutAssignmentAddsUnderlyingPositionReduces()
        {
            var algorithm = new QCAlgorithm();
            var securities = new SecurityManager(TimeKeeper);
            var transactions = new SecurityTransactionManager(null, securities);
            var transactionHandler = new BacktestingTransactionHandler();
            var portfolio = new SecurityPortfolioManager(securities, transactions);

            algorithm.Securities = securities;
            transactionHandler.Initialize(algorithm, new BacktestingBrokerage(algorithm), new TestResultHandler(Console.WriteLine));
            transactions.SetOrderProcessor(transactionHandler);

            // Adding cash: strike price times number of shares
            portfolio.SetCash(192 * 100);

            securities.Add(Symbols.SPY, new Security(SecurityExchangeHours, CreateTradeBarDataConfig(SecurityType.Equity, Symbols.SPY), new Cash(CashBook.AccountCurrency, 0, 1m), SymbolProperties.GetDefault(CashBook.AccountCurrency)));
            securities.Add(Symbols.SPY_P_192_Feb19_2016, new Option(SecurityExchangeHours, CreateTradeBarDataConfig(SecurityType.Equity, Symbols.SPY_P_192_Feb19_2016), new Cash(CashBook.AccountCurrency, 0, 1m), GetOptionSymbolProperties(Symbols.SPY_P_192_Feb19_2016)));
            securities[Symbols.SPY_P_192_Feb19_2016].Holdings.SetHoldings(1, -2);

            transactions.AddOrder(new SubmitOrderRequest(OrderType.OptionExercise, SecurityType.Option, Symbols.SPY_P_192_Feb19_2016, -1, 0, 0, securities.UtcTime, ""));
            var option = (Option)securities[Symbols.SPY_P_192_Feb19_2016];
            var order = (OptionExerciseOrder)transactions.GetOrders(x => true).First();
            option.Underlying = securities[Symbols.SPY];

            var fills = option.OptionExerciseModel.OptionExercise(option, order);

            // we are simulating assignment by calling a method for this
            var portfolioModel = (OptionPortfolioModel)option.PortfolioModel;

            foreach (var fill in fills)
            {
                portfolioModel.ProcessFill(portfolio, option, fill);
            }

            // we just got assigned!
            // now we have long position in SPY with average price equal to strike
            var newUnderlyingHoldings = securities[Symbols.SPY].Holdings;
            Assert.AreEqual(0, portfolio.Cash);
            Assert.AreEqual(100, newUnderlyingHoldings.Quantity);
            Assert.AreEqual(192.0, newUnderlyingHoldings.AveragePrice);

            // and short put option position still exists in the portfolio
            Assert.AreEqual(-1, securities[Symbols.SPY_P_192_Feb19_2016].Holdings.Quantity);
        }

        [Test]
        public void FullExerciseCashSettledCallAddsCash()
        {
            var algorithm = new QCAlgorithm();
            var securities = new SecurityManager(TimeKeeper);
            var transactions = new SecurityTransactionManager(null, securities);
            var transactionHandler = new BacktestingTransactionHandler();
            var portfolio = new SecurityPortfolioManager(securities, transactions);

            algorithm.Securities = securities;
            transactionHandler.Initialize(algorithm, new BacktestingBrokerage(algorithm), new TestResultHandler(Console.WriteLine));
            transactions.SetOrderProcessor(transactionHandler);

            portfolio.SetCash(0);

            securities.Add(Symbols.SPY, new Security(SecurityExchangeHours, CreateTradeBarDataConfig(SecurityType.Equity, Symbols.SPY), new Cash(CashBook.AccountCurrency, 0, 1m), SymbolProperties.GetDefault(CashBook.AccountCurrency)));
            securities.Add(Symbols.SPY_C_192_Feb19_2016, new Option(SecurityExchangeHours, CreateTradeBarDataConfig(SecurityType.Equity, Symbols.SPY_C_192_Feb19_2016), new Cash(CashBook.AccountCurrency, 0, 1m), GetOptionSymbolProperties(Symbols.SPY_C_192_Feb19_2016)));
            securities[Symbols.SPY].SetMarketPrice(new TradeBar { Time = securities.UtcTime, Symbol = Symbols.SPY, Close = 195 });
            securities[Symbols.SPY_C_192_Feb19_2016].Holdings.SetHoldings(1, 1);

            transactions.AddOrder(new SubmitOrderRequest(OrderType.OptionExercise, SecurityType.Option, Symbols.SPY_C_192_Feb19_2016, 1, 0, 0, securities.UtcTime, ""));
            var option = (Option)securities[Symbols.SPY_C_192_Feb19_2016];
            var order = (OptionExerciseOrder)transactions.GetOrders(x => true).First();
            option.Underlying = securities[Symbols.SPY];
            option.ExerciseSettlement = SettlementType.Cash;

            var fills = option.OptionExerciseModel.OptionExercise(option, order);

            foreach (var fill in fills)
            {
                portfolio.ProcessFill(fill);
            }

            // (underlying price - strike price) times number of shares
            Assert.AreEqual((195 - 192) * 100, portfolio.Cash);
            Assert.AreEqual(0, securities[Symbols.SPY].Holdings.Quantity);

            // and long call option position has disappeared
            Assert.AreEqual(0, securities[Symbols.SPY_C_192_Feb19_2016].Holdings.Quantity);
        }

        [Test]
        public void FullExerciseOTMCashSettledCallAddsNoCash()
        {
            var algorithm = new QCAlgorithm();
            var securities = new SecurityManager(TimeKeeper);
            var transactions = new SecurityTransactionManager(null, securities);
            var transactionHandler = new BacktestingTransactionHandler();
            var portfolio = new SecurityPortfolioManager(securities, transactions);

            algorithm.Securities = securities;
            transactionHandler.Initialize(algorithm, new BacktestingBrokerage(algorithm), new TestResultHandler(Console.WriteLine));
            transactions.SetOrderProcessor(transactionHandler);

            portfolio.SetCash(0);

            securities.Add(Symbols.SPY, new Security(SecurityExchangeHours, CreateTradeBarDataConfig(SecurityType.Equity, Symbols.SPY), new Cash(CashBook.AccountCurrency, 0, 1m), SymbolProperties.GetDefault(CashBook.AccountCurrency)));
            securities.Add(Symbols.SPY_C_192_Feb19_2016, new Option(SecurityExchangeHours, CreateTradeBarDataConfig(SecurityType.Equity, Symbols.SPY_C_192_Feb19_2016), new Cash(CashBook.AccountCurrency, 0, 1m), GetOptionSymbolProperties(Symbols.SPY_C_192_Feb19_2016)));
            securities[Symbols.SPY].SetMarketPrice(new TradeBar { Time = securities.UtcTime, Symbol = Symbols.SPY, Close = 190 });
            securities[Symbols.SPY_C_192_Feb19_2016].Holdings.SetHoldings(1, 100);

            transactions.AddOrder(new SubmitOrderRequest(OrderType.OptionExercise, SecurityType.Option, Symbols.SPY_C_192_Feb19_2016, 100, 0, 0, securities.UtcTime, ""));
            var option = (Option)securities[Symbols.SPY_C_192_Feb19_2016];
            var order = (OptionExerciseOrder)transactions.GetOrders(x => true).First();
            option.Underlying = securities[Symbols.SPY];
            option.ExerciseSettlement = SettlementType.Cash;

            var fills = option.OptionExerciseModel.OptionExercise(option, order);

            foreach (var fill in fills)
            {
                portfolio.ProcessFill(fill);
            }

            // no cash comes to the account because our contract was OTM
            Assert.AreEqual(0, portfolio.Cash);
            Assert.AreEqual(0, securities[Symbols.SPY].Holdings.Quantity);

            // and long call option position has disappeared
            Assert.AreEqual(0, securities[Symbols.SPY_C_192_Feb19_2016].Holdings.Quantity);
        }

        [Test]
        public void FullExerciseCashSettledPutAddsCash()
        {
            var algorithm = new QCAlgorithm();
            var securities = new SecurityManager(TimeKeeper);
            var transactions = new SecurityTransactionManager(null, securities);
            var transactionHandler = new BacktestingTransactionHandler();
            var portfolio = new SecurityPortfolioManager(securities, transactions);

            algorithm.Securities = securities;
            transactionHandler.Initialize(algorithm, new BacktestingBrokerage(algorithm), new TestResultHandler(Console.WriteLine));
            transactions.SetOrderProcessor(transactionHandler);
            portfolio.SetCash(0);

            securities.Add(Symbols.SPY, new Security(SecurityExchangeHours, CreateTradeBarDataConfig(SecurityType.Equity, Symbols.SPY), new Cash(CashBook.AccountCurrency, 0, 1m), SymbolProperties.GetDefault(CashBook.AccountCurrency)));
            securities.Add(Symbols.SPY_P_192_Feb19_2016, new Option(SecurityExchangeHours, CreateTradeBarDataConfig(SecurityType.Equity, Symbols.SPY_P_192_Feb19_2016), new Cash(CashBook.AccountCurrency, 0, 1m), GetOptionSymbolProperties(Symbols.SPY_P_192_Feb19_2016)));
            securities[Symbols.SPY].SetMarketPrice(new TradeBar { Time = securities.UtcTime, Symbol = Symbols.SPY, Close = 189 });
            securities[Symbols.SPY_P_192_Feb19_2016].Holdings.SetHoldings(1, 1);

            transactions.AddOrder(new SubmitOrderRequest(OrderType.OptionExercise, SecurityType.Option, Symbols.SPY_P_192_Feb19_2016, 1, 0, 0, securities.UtcTime, ""));
            var option = (Option)securities[Symbols.SPY_P_192_Feb19_2016];
            var order = (OptionExerciseOrder)transactions.GetOrders(x => true).First();
            option.Underlying = securities[Symbols.SPY];
            option.ExerciseSettlement = SettlementType.Cash;

            var fills = option.OptionExerciseModel.OptionExercise(option, order);

            foreach (var fill in fills)
            {
                portfolio.ProcessFill(fill);
            }

            // (strike price - underlying price) times number of shares
            Assert.AreEqual((192 - 189) * 100, portfolio.Cash);
            Assert.AreEqual(0, securities[Symbols.SPY].Holdings.Quantity);

            // and long put option position has disappeared
            Assert.AreEqual(0, securities[Symbols.SPY_P_192_Feb19_2016].Holdings.Quantity);
        }

        [Test]
        public void ComputeMarginProperlyOnOptionExercise()
        {
            var algorithm = new QCAlgorithm();
            var securities = new SecurityManager(TimeKeeper);
            var transactions = new SecurityTransactionManager(null, securities);
            var orderProcessor = new OrderProcessor();
            var portfolio = new SecurityPortfolioManager(securities, transactions);

            var time = DateTime.Now;
            algorithm.Securities = securities;
            transactions.SetOrderProcessor(orderProcessor);

            portfolio.SetCash(1000);

            securities.Add(Symbols.SPY, new Security(SecurityExchangeHours, CreateTradeBarDataConfig(SecurityType.Equity, Symbols.SPY), new Cash(CashBook.AccountCurrency, 0, 1m), SymbolProperties.GetDefault(CashBook.AccountCurrency)));
            securities.Add(Symbols.SPY_C_192_Feb19_2016, new Option(SecurityExchangeHours, CreateTradeBarDataConfig(SecurityType.Equity, Symbols.SPY_C_192_Feb19_2016), new Cash(CashBook.AccountCurrency, 0, 1m), GetOptionSymbolProperties(Symbols.SPY_C_192_Feb19_2016)));
            securities[Symbols.SPY].SetMarketPrice(new TradeBar { Time = securities.UtcTime, Symbol = Symbols.SPY, Close = 195 });
            securities[Symbols.SPY_C_192_Feb19_2016].Holdings.SetHoldings(1, 10);

            var option = (Option)securities[Symbols.SPY_C_192_Feb19_2016];
            option.Underlying = securities[Symbols.SPY];

            var order = new OptionExerciseOrder(Symbols.SPY_C_192_Feb19_2016, 10, time.AddSeconds(1));
            orderProcessor.AddOrder(order);
            var request = new SubmitOrderRequest(OrderType.OptionExercise, option.Type, option.Symbol, order.Quantity, 0, 0, order.Time, null);
            request.SetOrderId(0);
            orderProcessor.AddTicket(new OrderTicket(null, request));
            var hasSufficientBuyingPower = option.BuyingPowerModel.HasSufficientBuyingPowerForOrder(portfolio, option, order).IsSufficient;
            Assert.IsFalse(hasSufficientBuyingPower);

            securities[Symbols.SPY].SetMarketPrice(new TradeBar { Time = securities.UtcTime, Symbol = Symbols.SPY, Close = 150 });

            order = new OptionExerciseOrder(Symbols.SPY_C_192_Feb19_2016, 10, time.AddSeconds(1));
            orderProcessor.AddOrder(order);
            request = new SubmitOrderRequest(OrderType.OptionExercise, option.Type, option.Symbol, order.Quantity, 0, 0, order.Time, null);
            request.SetOrderId(0);
            orderProcessor.AddTicket(new OrderTicket(null, request));
            hasSufficientBuyingPower = option.BuyingPowerModel.HasSufficientBuyingPowerForOrder(portfolio, option, order).IsSufficient;
            Assert.IsTrue(hasSufficientBuyingPower);
        }

        [Test]
        public void ComputeMarginProperlyOnOptionAssignment()
        {
            var algorithm = new QCAlgorithm();
            var securities = new SecurityManager(TimeKeeper);
            var transactions = new SecurityTransactionManager(null, securities);
            var orderProcessor = new OrderProcessor();
            var portfolio = new SecurityPortfolioManager(securities, transactions);

            var time = DateTime.Now;
            algorithm.Securities = securities;
            transactions.SetOrderProcessor(orderProcessor);

            portfolio.SetCash(1000);

            securities.Add(Symbols.SPY, new Security(SecurityExchangeHours, CreateTradeBarDataConfig(SecurityType.Equity, Symbols.SPY), new Cash(CashBook.AccountCurrency, 0, 1m), SymbolProperties.GetDefault(CashBook.AccountCurrency)));
            securities.Add(Symbols.SPY_C_192_Feb19_2016, new Option(SecurityExchangeHours, CreateTradeBarDataConfig(SecurityType.Equity, Symbols.SPY_C_192_Feb19_2016), new Cash(CashBook.AccountCurrency, 0, 1m), GetOptionSymbolProperties(Symbols.SPY_C_192_Feb19_2016)));
            securities[Symbols.SPY].SetMarketPrice(new TradeBar { Time = securities.UtcTime, Symbol = Symbols.SPY, Close = 195 });
            securities[Symbols.SPY_C_192_Feb19_2016].Holdings.SetHoldings(1, -10);

            var option = (Option)securities[Symbols.SPY_C_192_Feb19_2016];
            option.Underlying = securities[Symbols.SPY];

            var order = new OptionExerciseOrder(Symbols.SPY_C_192_Feb19_2016, -10, time.AddSeconds(1));
            orderProcessor.AddOrder(order);
            var request = new SubmitOrderRequest(OrderType.OptionExercise, option.Type, option.Symbol, order.Quantity, 0, 0, order.Time, null);
            request.SetOrderId(0);
            orderProcessor.AddTicket(new OrderTicket(null, request));
            var hasSufficientBuyingPower = option.BuyingPowerModel.HasSufficientBuyingPowerForOrder(portfolio, option, order).IsSufficient;
            Assert.IsFalse(hasSufficientBuyingPower);

            securities[Symbols.SPY].SetMarketPrice(new TradeBar { Time = securities.UtcTime, Symbol = Symbols.SPY, Close = 150 });

            order = new OptionExerciseOrder(Symbols.SPY_C_192_Feb19_2016, -10, time.AddSeconds(1));
            orderProcessor.AddOrder(order);
            request = new SubmitOrderRequest(OrderType.OptionExercise, option.Type, option.Symbol, order.Quantity, 0, 0, order.Time, null);
            request.SetOrderId(0);
            orderProcessor.AddTicket(new OrderTicket(null, request));
            hasSufficientBuyingPower = option.BuyingPowerModel.HasSufficientBuyingPowerForOrder(portfolio, option, order).IsSufficient;
            Assert.IsTrue(hasSufficientBuyingPower);
        }

        [Test]
        public void PartialExerciseCashSettledCallAddsSomeCash()
        {
            var algorithm = new QCAlgorithm();
            var securities = new SecurityManager(TimeKeeper);
            var transactions = new SecurityTransactionManager(null, securities);
            var transactionHandler = new BacktestingTransactionHandler();
            var portfolio = new SecurityPortfolioManager(securities, transactions);

            algorithm.Securities = securities;
            transactionHandler.Initialize(algorithm, new BacktestingBrokerage(algorithm), new TestResultHandler(Console.WriteLine));
            transactions.SetOrderProcessor(transactionHandler);
            portfolio.SetCash(0);

            securities.Add(Symbols.SPY, new Security(SecurityExchangeHours, CreateTradeBarDataConfig(SecurityType.Equity, Symbols.SPY), new Cash(CashBook.AccountCurrency, 0, 1m), SymbolProperties.GetDefault(CashBook.AccountCurrency)));
            securities.Add(Symbols.SPY_C_192_Feb19_2016, new Option(SecurityExchangeHours, CreateTradeBarDataConfig(SecurityType.Equity, Symbols.SPY_C_192_Feb19_2016), new Cash(CashBook.AccountCurrency, 0, 1m), GetOptionSymbolProperties(Symbols.SPY_C_192_Feb19_2016)));
            securities[Symbols.SPY].SetMarketPrice(new TradeBar { Time = securities.UtcTime, Symbol = Symbols.SPY, Close = 195 });
            securities[Symbols.SPY_C_192_Feb19_2016].Holdings.SetHoldings(1, 2);

            transactions.AddOrder(new SubmitOrderRequest(OrderType.OptionExercise, SecurityType.Option, Symbols.SPY_C_192_Feb19_2016, 1, 0, 0, securities.UtcTime, ""));
            var option = (Option)securities[Symbols.SPY_C_192_Feb19_2016];
            var order = (OptionExerciseOrder)transactions.GetOrders(x => true).First();
            option.Underlying = securities[Symbols.SPY];
            option.ExerciseSettlement = SettlementType.Cash;

            var fills = option.OptionExerciseModel.OptionExercise(option, order);

            foreach (var fill in fills)
            {
                portfolio.ProcessFill(fill);
            }

            // (underlying price - strike price) times number of shares
            Assert.AreEqual((195 - 192) * 100, portfolio.Cash);
            Assert.AreEqual(0, securities[Symbols.SPY].Holdings.Quantity);

            // and call option position still has some value
            Assert.AreEqual(1, securities[Symbols.SPY_C_192_Feb19_2016].Holdings.Quantity);
        }

        [Test]
        public void InternalCashSettledCallAssignmentReducesCash()
        {
            var algorithm = new QCAlgorithm();
            var securities = new SecurityManager(TimeKeeper);
            var transactions = new SecurityTransactionManager(null, securities);
            var transactionHandler = new BacktestingTransactionHandler();
            var portfolio = new SecurityPortfolioManager(securities, transactions);

            algorithm.Securities = securities;
            transactionHandler.Initialize(algorithm, new BacktestingBrokerage(algorithm), new TestResultHandler(Console.WriteLine));
            transactions.SetOrderProcessor(transactionHandler);

            // (underlying price - strike price) times number of shares
            portfolio.SetCash((195 - 192) * 100);

            securities.Add(Symbols.SPY, new Security(SecurityExchangeHours, CreateTradeBarDataConfig(SecurityType.Equity, Symbols.SPY), new Cash(CashBook.AccountCurrency, 0, 1m), SymbolProperties.GetDefault(CashBook.AccountCurrency)));
            securities.Add(Symbols.SPY_C_192_Feb19_2016, new Option(SecurityExchangeHours, CreateTradeBarDataConfig(SecurityType.Equity, Symbols.SPY_C_192_Feb19_2016), new Cash(CashBook.AccountCurrency, 0, 1m), GetOptionSymbolProperties(Symbols.SPY_C_192_Feb19_2016)));
            securities[Symbols.SPY].SetMarketPrice(new TradeBar { Time = securities.UtcTime, Symbol = Symbols.SPY, Close = 195 });
            securities[Symbols.SPY_C_192_Feb19_2016].Holdings.SetHoldings(1, -1);

            transactions.AddOrder(new SubmitOrderRequest(OrderType.OptionExercise, SecurityType.Option, Symbols.SPY_C_192_Feb19_2016, -1, 0, 0, securities.UtcTime, ""));
            var option = (Option)securities[Symbols.SPY_C_192_Feb19_2016];
            var order = (OptionExerciseOrder)transactions.GetOrders(x => true).First();
            option.Underlying = securities[Symbols.SPY];
            option.ExerciseSettlement = SettlementType.Cash;

            var fills = option.OptionExerciseModel.OptionExercise(option, order);

            // we are simulating assignment by calling a method for this
            var portfolioModel = (OptionPortfolioModel)option.PortfolioModel;
            foreach (var fill in fills)
            {
                portfolioModel.ProcessFill(portfolio, option, fill);
            }

            // we just got assigned!
            var newUnderlyingHoldings = securities[Symbols.SPY].Holdings;
            Assert.AreEqual(0, portfolio.Cash);
            Assert.AreEqual(0, securities[Symbols.SPY].Holdings.Quantity);

            // and short call option position has disappeared
            Assert.AreEqual(0, securities[Symbols.SPY_C_192_Feb19_2016].Holdings.Quantity);
        }

        [Test]
        public void InternalCashSettledOTMCallAssignmentDoesntChangeAnything()
        {
            var algorithm = new QCAlgorithm();
            var securities = new SecurityManager(TimeKeeper);
            var transactions = new SecurityTransactionManager(null, securities);
            var transactionHandler = new BacktestingTransactionHandler();
            var portfolio = new SecurityPortfolioManager(securities, transactions);

            algorithm.Securities = securities;
            transactionHandler.Initialize(algorithm, new BacktestingBrokerage(algorithm), new TestResultHandler(Console.WriteLine));
            transactions.SetOrderProcessor(transactionHandler);

            portfolio.SetCash(0);

            securities.Add(Symbols.SPY, new Security(SecurityExchangeHours, CreateTradeBarDataConfig(SecurityType.Equity, Symbols.SPY), new Cash(CashBook.AccountCurrency, 0, 1m), SymbolProperties.GetDefault(CashBook.AccountCurrency)));
            securities.Add(Symbols.SPY_C_192_Feb19_2016, new Option(SecurityExchangeHours, CreateTradeBarDataConfig(SecurityType.Equity, Symbols.SPY_C_192_Feb19_2016), new Cash(CashBook.AccountCurrency, 0, 1m), GetOptionSymbolProperties(Symbols.SPY_C_192_Feb19_2016)));
            securities[Symbols.SPY].SetMarketPrice(new TradeBar { Time = securities.UtcTime, Symbol = Symbols.SPY, Close = 10 });
            securities[Symbols.SPY_C_192_Feb19_2016].Holdings.SetHoldings(1, -100);

            transactions.AddOrder(new SubmitOrderRequest(OrderType.OptionExercise, SecurityType.Option, Symbols.SPY_C_192_Feb19_2016, -100, 0, 0, securities.UtcTime, ""));
            var option = (Option)securities[Symbols.SPY_C_192_Feb19_2016];
            var order = (OptionExerciseOrder)transactions.GetOrders(x => true).First();
            option.Underlying = securities[Symbols.SPY];
            option.ExerciseSettlement = SettlementType.Cash;

            var fills = option.OptionExerciseModel.OptionExercise(option, order);

            // we are simulating assignment by calling a method for this
            var portfolioModel = (OptionPortfolioModel)option.PortfolioModel;

            foreach (var fill in fills)
            {
                portfolioModel.ProcessFill(portfolio, option, fill);
            }

            // we just got assigned! nothing changed...
            var newUnderlyingHoldings = securities[Symbols.SPY].Holdings;
            Assert.AreEqual(0, portfolio.Cash);
            Assert.AreEqual(0, securities[Symbols.SPY].Holdings.Quantity);

            // and short call option position has disappeared
            Assert.AreEqual(0, securities[Symbols.SPY_C_192_Feb19_2016].Holdings.Quantity);
        }

        [Test]
        public void InternalCashSettledPutAssignmentReducesCash()
        {
            var algorithm = new QCAlgorithm();
            var securities = new SecurityManager(TimeKeeper);
            var transactions = new SecurityTransactionManager(null, securities);
            var transactionHandler = new BacktestingTransactionHandler();
            var portfolio = new SecurityPortfolioManager(securities, transactions);

            algorithm.Securities = securities;
            transactionHandler.Initialize(algorithm, new BacktestingBrokerage(algorithm), new TestResultHandler(Console.WriteLine));
            transactions.SetOrderProcessor(transactionHandler);

            // (strike price - underlying price) times number of shares
            portfolio.SetCash((192 - 189) * 100);

            securities.Add(Symbols.SPY, new Security(SecurityExchangeHours, CreateTradeBarDataConfig(SecurityType.Equity, Symbols.SPY), new Cash(CashBook.AccountCurrency, 0, 1m), SymbolProperties.GetDefault(CashBook.AccountCurrency)));
            securities.Add(Symbols.SPY_P_192_Feb19_2016, new Option(SecurityExchangeHours, CreateTradeBarDataConfig(SecurityType.Equity, Symbols.SPY_P_192_Feb19_2016), new Cash(CashBook.AccountCurrency, 0, 1m), GetOptionSymbolProperties(Symbols.SPY_P_192_Feb19_2016)));
            securities[Symbols.SPY].SetMarketPrice(new TradeBar { Time = securities.UtcTime, Symbol = Symbols.SPY, Close = 189 });
            securities[Symbols.SPY_P_192_Feb19_2016].Holdings.SetHoldings(1, -1);

            transactions.AddOrder(new SubmitOrderRequest(OrderType.OptionExercise, SecurityType.Option, Symbols.SPY_P_192_Feb19_2016, -1, 0, 0, securities.UtcTime, ""));
            var option = (Option)securities[Symbols.SPY_P_192_Feb19_2016];
            var order = (OptionExerciseOrder)transactions.GetOrders(x => true).First();
            option.Underlying = securities[Symbols.SPY];
            option.ExerciseSettlement = SettlementType.Cash;

            var fills = option.OptionExerciseModel.OptionExercise(option, order);

            // we are simulating assignment by calling a method for this
            var portfolioModel = (OptionPortfolioModel)option.PortfolioModel;

            foreach (var fill in fills)
            {
                portfolioModel.ProcessFill(portfolio, option, fill);
            }

            // we just got assigned!
            var newUnderlyingHoldings = securities[Symbols.SPY].Holdings;
            Assert.AreEqual(0, portfolio.Cash);
            Assert.AreEqual(0, securities[Symbols.SPY].Holdings.Quantity);

            // and short put option position has disappeared
            Assert.AreEqual(0, securities[Symbols.SPY_P_192_Feb19_2016].Holdings.Quantity);
        }

        [Test]
        public void InternalPartialCashSettledPutAssignmentReducesSomeCash()
        {
            var algorithm = new QCAlgorithm();
            var securities = new SecurityManager(TimeKeeper);
            var transactions = new SecurityTransactionManager(null, securities);
            var transactionHandler = new BacktestingTransactionHandler();
            var portfolio = new SecurityPortfolioManager(securities, transactions);

            algorithm.Securities = securities;
            transactionHandler.Initialize(algorithm, new BacktestingBrokerage(algorithm), new TestResultHandler(Console.WriteLine));
            transactions.SetOrderProcessor(transactionHandler);

            // (strike price - underlying price) times number of shares
            portfolio.SetCash((192 - 189) * 100);

            securities.Add(Symbols.SPY, new Security(SecurityExchangeHours, CreateTradeBarDataConfig(SecurityType.Equity, Symbols.SPY), new Cash(CashBook.AccountCurrency, 0, 1m), SymbolProperties.GetDefault(CashBook.AccountCurrency)));
            securities.Add(Symbols.SPY_P_192_Feb19_2016, new Option(SecurityExchangeHours, CreateTradeBarDataConfig(SecurityType.Equity, Symbols.SPY_P_192_Feb19_2016), new Cash(CashBook.AccountCurrency, 0, 1m), GetOptionSymbolProperties(Symbols.SPY_P_192_Feb19_2016)));
            securities[Symbols.SPY].SetMarketPrice(new TradeBar { Time = securities.UtcTime, Symbol = Symbols.SPY, Close = 189 });
            securities[Symbols.SPY_P_192_Feb19_2016].Holdings.SetHoldings(1, -2);

            transactions.AddOrder(new SubmitOrderRequest(OrderType.OptionExercise, SecurityType.Option, Symbols.SPY_P_192_Feb19_2016, -1, 0, 0, securities.UtcTime, ""));
            var option = (Option)securities[Symbols.SPY_P_192_Feb19_2016];
            var order = (OptionExerciseOrder)transactions.GetOrders(x => true).First();
            option.Underlying = securities[Symbols.SPY];
            option.ExerciseSettlement = SettlementType.Cash;

            var fills = option.OptionExerciseModel.OptionExercise(option, order);

            // we are simulating assignment by calling a method for this
            var portfolioModel = (OptionPortfolioModel)option.PortfolioModel;
            foreach (var fill in fills)
            {
                portfolioModel.ProcessFill(portfolio, option, fill);
            }

            // we just got assigned!
            var newUnderlyingHoldings = securities[Symbols.SPY].Holdings;
            Assert.AreEqual(0, portfolio.Cash);
            Assert.AreEqual(0, securities[Symbols.SPY].Holdings.Quantity);

            // and short put option position still exists in the portfolio
            Assert.AreEqual(-1, securities[Symbols.SPY_P_192_Feb19_2016].Holdings.Quantity);
        }

        private SubscriptionDataConfig CreateTradeBarDataConfig(SecurityType type, Symbol symbol)
        {
            if (type == SecurityType.Equity)
                return new SubscriptionDataConfig(typeof (TradeBar), symbol, Resolution.Minute, TimeZones.NewYork, TimeZones.NewYork, true, true, true);
            if (type == SecurityType.Forex)
                return new SubscriptionDataConfig(typeof (TradeBar), symbol, Resolution.Minute, TimeZones.NewYork, TimeZones.NewYork, true, true, true);
            if (type == SecurityType.Future)
                return new SubscriptionDataConfig(typeof(TradeBar), symbol, Resolution.Minute, TimeZones.NewYork, TimeZones.NewYork, true, true, true);
            if (type == SecurityType.Crypto)
                return new SubscriptionDataConfig(typeof(TradeBar), symbol, Resolution.Minute, TimeZones.NewYork, TimeZones.NewYork, true, true, true);
            throw new NotImplementedException(type.ToString());
        }

        private static OptionSymbolProperties GetOptionSymbolProperties(Symbol symbol)
        {
            return new OptionSymbolProperties(SymbolPropertiesDatabase.FromDataFolder().GetSymbolProperties(symbol.ID.Market, symbol, symbol.SecurityType, CashBook.AccountCurrency));
        }

        private static TimeKeeper TimeKeeper
        {
            get { return new TimeKeeper(DateTime.Now, new[] { TimeZones.NewYork }); }
        }

        class OrderProcessor : IOrderProcessor
        {
            private readonly ConcurrentDictionary<int, Order> _orders = new ConcurrentDictionary<int, Order>();
            private readonly ConcurrentDictionary<int, OrderTicket> _tickets = new ConcurrentDictionary<int, OrderTicket>();
            public void AddOrder(Order order)
            {
                _orders[order.Id] = order;
            }

            public void AddTicket(OrderTicket ticket)
            {
                _tickets[ticket.OrderId] = ticket;
            }
            public int OrdersCount { get; private set; }
            public Order GetOrderById(int orderId)
            {
                Order order;
                _orders.TryGetValue(orderId, out order);
                return order;
            }

            public Order GetOrderByBrokerageId(string brokerageId)
            {
                return _orders.Values.FirstOrDefault(x => x.BrokerId.Contains(brokerageId));
            }

            public IEnumerable<OrderTicket> GetOrderTickets(Func<OrderTicket, bool> filter = null)
            {
                return _tickets.Values.Where(filter ?? (x => true));
            }

            public OrderTicket GetOrderTicket(int orderId)
            {
                OrderTicket ticket;
                _tickets.TryGetValue(orderId, out ticket);
                return ticket;
            }

            public IEnumerable<Order> GetOrders(Func<Order, bool> filter = null)
            {
                return _orders.Values.Where(filter ?? (x => true));
            }

            public OrderTicket Process(OrderRequest request)
            {
                throw new NotImplementedException();
            }
        }
    }
}
