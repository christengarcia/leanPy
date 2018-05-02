﻿using NUnit.Framework;
using QuantConnect.Brokerages;
using QuantConnect.Algorithm;

namespace QuantConnect.Tests.Algorithm
{
    /// <summary>
    /// Test class for 
    ///  - SetBrokerageModel() in QCAlgorithm
    ///  - Default market for new securities
    /// </summary>
    [TestFixture]
    public class AlgorithmSetBrokerageTests
    {
        private QCAlgorithm _algo;
        private const string ForexSym = "EURUSD";
        private const string Sym = "SPY";

        /// <summary>
        /// Instatiate a new algorithm before each test.
        /// Clear the <see cref="SymbolCache"/> so that no symbols and associated brokerage models are cached between test
        /// </summary>
        [SetUp]
        public void Setup()
        {
            _algo = new QCAlgorithm();
            SymbolCache.TryRemove(ForexSym);
            SymbolCache.TryRemove(Sym);
        }

        /// <summary>
        /// The default market for FOREX should be FXCM
        /// </summary>
        [Test]
        public void DefaultBrokerageModel_IsFXCM_ForForex()
        {
            var forex = _algo.AddForex(ForexSym);


            Assert.IsTrue(forex.Symbol.ID.Market == Market.FXCM);
            Assert.IsTrue(_algo.BrokerageModel.GetType() == typeof(DefaultBrokerageModel));
        }

        /// <summary>
        /// The default market for equities should be USA
        /// </summary>
        [Test]
        public void DefaultBrokerageModel_IsUSA_ForEquity()
        {
            var equity = _algo.AddEquity(Sym);


            Assert.IsTrue(equity.Symbol.ID.Market == Market.USA);
            Assert.IsTrue(_algo.BrokerageModel.GetType() == typeof(DefaultBrokerageModel));
        }

        /// <summary>
        /// The default market for options should be USA
        /// </summary>
        [Test]
        public void DefaultBrokerageModel_IsUSA_ForOption()
        {
            var option = _algo.AddOption(Sym);


            Assert.IsTrue(option.Symbol.ID.Market == Market.USA);
            Assert.IsTrue(_algo.BrokerageModel.GetType() == typeof(DefaultBrokerageModel));
        }

        /// <summary>
        /// Brokerage model for an algorithm can be changed using <see cref="QCAlgorithm.SetBrokerageModel(IBrokerageModel)"/>
        /// This changes the brokerage models used when forex currency pairs are added via AddForex and no brokerage is specified. 
        /// </summary>
        [Test]
        public void BrokerageModel_CanBeSpecifiedWith_SetBrokerageModel()
        {
            _algo.SetBrokerageModel(BrokerageName.OandaBrokerage);
            var forex = _algo.AddForex(ForexSym);

            string brokerage = GetDefaultBrokerageForSecurityType(SecurityType.Forex);


            Assert.IsTrue(forex.Symbol.ID.Market == Market.Oanda);
            Assert.IsTrue(_algo.BrokerageModel.GetType() == typeof(OandaBrokerageModel));
            Assert.IsTrue(brokerage == Market.Oanda);
        }

        /// <summary>
        /// Specifying the market in <see cref="QCAlgorithm.AddForex"/> will change the market of the security created.
        /// </summary>
        [Test]
        public void BrokerageModel_CanBeSpecifiedWith_AddForex()
        {
            var forex = _algo.AddForex(ForexSym, Resolution.Minute, Market.Oanda);

            string brokerage = GetDefaultBrokerageForSecurityType(SecurityType.Forex);


            Assert.IsTrue(forex.Symbol.ID.Market == Market.Oanda);
            Assert.IsTrue(_algo.BrokerageModel.GetType() == typeof(DefaultBrokerageModel));
            Assert.IsTrue(brokerage == Market.FXCM);  // Doesn't change brokerage defined in BrokerageModel.DefaultMarkets
        }

        /// <summary>
        /// The method <see cref="QCAlgorithm.AddSecurity(SecurityType, string, Resolution, bool, bool)"/> should use the default brokerage for the sepcific security.
        /// Setting the brokerage with <see cref="QCAlgorithm.SetBrokerageModel(IBrokerageModel)"/> will affect the market of securities added with  <see cref="QCAlgorithm.AddSecurity(SecurityType, string, Resolution, bool, bool)"/>
        /// </summary>
        [Test]
        public void AddSecurity_Follows_SetBrokerageModel()
        {
            // No brokerage set
            var equity = _algo.AddSecurity(SecurityType.Equity, Sym);

            string equityBrokerage = GetDefaultBrokerageForSecurityType(SecurityType.Equity);


            Assert.IsTrue(equity.Symbol.ID.Market == Market.USA);
            Assert.IsTrue(_algo.BrokerageModel.GetType() == typeof(DefaultBrokerageModel));
            Assert.IsTrue(equityBrokerage == Market.USA);

            // Set Brokerage
            _algo.SetBrokerageModel(BrokerageName.OandaBrokerage);

            var sec = _algo.AddSecurity(SecurityType.Forex, ForexSym, Resolution.Daily, false, 1, false);

            string forexBrokerage = GetDefaultBrokerageForSecurityType(SecurityType.Forex);


            Assert.IsTrue(sec.Symbol.ID.Market == Market.Oanda);
            Assert.IsTrue(_algo.BrokerageModel.GetType() == typeof(OandaBrokerageModel));
            Assert.IsTrue(forexBrokerage ==  Market.Oanda);
        }

        /// <summary>
        /// Returns the default market for a security type
        /// </summary>
        /// <param name="secType">The type of security</param>
        /// <returns>A string representing the default market of a security</returns>
        private string GetDefaultBrokerageForSecurityType(SecurityType secType)
        {
            string brokerage;
            _algo.BrokerageModel.DefaultMarkets.TryGetValue(secType, out brokerage);
            return brokerage;
        }
    }
}
