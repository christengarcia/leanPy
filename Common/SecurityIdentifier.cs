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
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Newtonsoft.Json;
using QuantConnect.Configuration;
using QuantConnect.Interfaces;
using QuantConnect.Logging;
using QuantConnect.Util;

namespace QuantConnect
{
    /// <summary>
    /// Defines a unique identifier for securities
    /// </summary>
    /// <remarks>
    /// The SecurityIdentifier contains information about a specific security.
    /// This includes the symbol and other data specific to the SecurityType.
    /// The symbol is limited to 12 characters
    /// </remarks>
    [JsonConverter(typeof(SecurityIdentifierJsonConverter))]
    public struct SecurityIdentifier : IEquatable<SecurityIdentifier>
    {
        #region Empty, DefaultDate Fields

        private static readonly string MapFileProviderTypeName = Config.Get("map-file-provider", "LocalDiskMapFileProvider");
        private static readonly char[] InvalidCharacters = {'|', ' '};

        /// <summary>
        /// Gets an instance of <see cref="SecurityIdentifier"/> that is empty, that is, one with no symbol specified
        /// </summary>
        public static readonly SecurityIdentifier Empty = new SecurityIdentifier(string.Empty, 0);

        /// <summary>
        /// Gets the date to be used when it does not apply.
        /// </summary>
        public static readonly DateTime DefaultDate = DateTime.FromOADate(0);

        /// <summary>
        /// Gets the set of invalids symbol characters
        /// </summary>
        public static readonly HashSet<char> InvalidSymbolCharacters = new HashSet<char>(InvalidCharacters);

        #endregion

        #region Scales, Widths and Market Maps

        // these values define the structure of the 'otherData'
        // the constant width fields are used via modulus, so the width is the number of zeros specified,
        // {put/call:1}{oa-date:5}{style:1}{strike:6}{strike-scale:2}{market:3}{security-type:2}

        private const ulong SecurityTypeWidth = 100;
        private const ulong SecurityTypeOffset = 1;

        private const ulong MarketWidth = 1000;
        private const ulong MarketOffset = SecurityTypeOffset * SecurityTypeWidth;

        private const int StrikeDefaultScale = 4;
        private static readonly ulong StrikeDefaultScaleExpanded = Pow(10, StrikeDefaultScale);

        private const ulong StrikeScaleWidth = 100;
        private const ulong StrikeScaleOffset = MarketOffset * MarketWidth;

        private const ulong StrikeWidth = 1000000;
        private const ulong StrikeOffset = StrikeScaleOffset * StrikeScaleWidth;

        private const ulong OptionStyleWidth = 10;
        private const ulong OptionStyleOffset = StrikeOffset * StrikeWidth;

        private const ulong DaysWidth = 100000;
        private const ulong DaysOffset = OptionStyleOffset * OptionStyleWidth;

        private const ulong PutCallOffset = DaysOffset * DaysWidth;
        private const ulong PutCallWidth = 10;

        #endregion

        #region Member variables

        private readonly string _symbol;
        private readonly ulong _properties;
        private readonly SidBox _underlying;

        #endregion

        #region Properties

        /// <summary>
        /// Gets whether or not this <see cref="SecurityIdentifier"/> is a derivative,
        /// that is, it has a valid <see cref="Underlying"/> property
        /// </summary>
        public bool HasUnderlying
        {
            get { return _underlying != null; }
        }

        /// <summary>
        /// Gets the underlying security identifier for this security identifier. When there is
        /// no underlying, this property will return a value of <see cref="Empty"/>.
        /// </summary>
        public SecurityIdentifier Underlying
        {
            get
            {
                if (_underlying == null)
                {
                    throw new InvalidOperationException("No underlying specified for this identifier. Check that HasUnderlying is true before accessing the Underlying property.");
                }
                return _underlying.SecurityIdentifier;
            }
        }

        /// <summary>
        /// Gets the date component of this identifier. For equities this
        /// is the first date the security traded. Technically speaking,
        /// in LEAN, this is the first date mentioned in the map_files.
        /// For options this is the expiry date. For futures this is the
        /// settlement date. For forex and cfds this property will throw an
        /// exception as the field is not specified.
        /// </summary>
        public DateTime Date
        {
            get
            {
                var stype = SecurityType;
                switch (stype)
                {
                    case SecurityType.Equity:
                    case SecurityType.Option:
                    case SecurityType.Future:
                        var oadate = ExtractFromProperties(DaysOffset, DaysWidth);
                        return DateTime.FromOADate(oadate);
                    default:
                        throw new InvalidOperationException("Date is only defined for SecurityType.Equity, SecurityType.Option and SecurityType.Future");
                }
            }
        }

        /// <summary>
        /// Gets the original symbol used to generate this security identifier.
        /// For equities, by convention this is the first ticker symbol for which
        /// the security traded
        /// </summary>
        public string Symbol
        {
            get { return _symbol; }
        }

        /// <summary>
        /// Gets the market component of this security identifier. If located in the
        /// internal mappings, the full string is returned. If the value is unknown,
        /// the integer value is returned as a string.
        /// </summary>
        public string Market
        {
            get
            {
                var marketCode = ExtractFromProperties(MarketOffset, MarketWidth);
                var market = QuantConnect.Market.Decode((int)marketCode);

                // if we couldn't find it, send back the numeric representation
                return market ?? marketCode.ToString();
            }
        }

        /// <summary>
        /// Gets the security type component of this security identifier.
        /// </summary>
        public SecurityType SecurityType
        {
            get { return (SecurityType)ExtractFromProperties(SecurityTypeOffset, SecurityTypeWidth); }
        }

        /// <summary>
        /// Gets the option strike price. This only applies to SecurityType.Option
        /// and will thrown anexception if accessed otherwse.
        /// </summary>
        public decimal StrikePrice
        {
            get
            {
                if (SecurityType != SecurityType.Option)
                {
                    throw new InvalidOperationException("OptionType is only defined for SecurityType.Option");
                }
                var scale = ExtractFromProperties(StrikeScaleOffset, StrikeScaleWidth);
                var unscaled = ExtractFromProperties(StrikeOffset, StrikeWidth);
                var pow = Math.Pow(10, (int)scale - StrikeDefaultScale);
                return unscaled * (decimal)pow;
            }
        }

        /// <summary>
        /// Gets the option type component of this security identifier. This
        /// only applies to SecurityType.Open and will throw an exception if
        /// accessed otherwise.
        /// </summary>
        public OptionRight OptionRight
        {
            get
            {
                if (SecurityType != SecurityType.Option)
                {
                    throw new InvalidOperationException("OptionRight is only defined for SecurityType.Option");
                }
                return (OptionRight)ExtractFromProperties(PutCallOffset, PutCallWidth);
            }
        }

        /// <summary>
        /// Gets the option style component of this security identifier. This
        /// only applies to SecurityType.Open and will throw an exception if
        /// accessed otherwise.
        /// </summary>
        public OptionStyle OptionStyle
        {
            get
            {
                if (SecurityType != SecurityType.Option)
                {
                    throw new InvalidOperationException("OptionStyle is only defined for SecurityType.Option");
                }
                return (OptionStyle)(ExtractFromProperties(OptionStyleOffset, OptionStyleWidth));
            }
        }

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="SecurityIdentifier"/> class
        /// </summary>
        /// <param name="symbol">The base36 string encoded as a long using alpha [0-9A-Z]</param>
        /// <param name="properties">Other data defining properties of the symbol including market,
        /// security type, listing or expiry date, strike/call/put/style for options, ect...</param>
        public SecurityIdentifier(string symbol, ulong properties)
        {
            if (symbol == null)
            {
                throw new ArgumentNullException("symbol", "SecurityIdentifier requires a non-null string 'symbol'");
            }
            if (symbol.IndexOfAny(InvalidCharacters) != -1)
            {
                throw new ArgumentException("symbol must not contain the characters '|' or ' '.", "symbol");
            }
            _symbol = symbol;
            _properties = properties;
            _underlying = null;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SecurityIdentifier"/> class
        /// </summary>
        /// <param name="symbol">The base36 string encoded as a long using alpha [0-9A-Z]</param>
        /// <param name="properties">Other data defining properties of the symbol including market,
        /// security type, listing or expiry date, strike/call/put/style for options, ect...</param>
        /// <param name="underlying">Specifies a <see cref="SecurityIdentifier"/> that represents the underlying security</param>
        public SecurityIdentifier(string symbol, ulong properties, SecurityIdentifier underlying)
            : this(symbol, properties)
        {
            if (symbol == null)
            {
                throw new ArgumentNullException("symbol", "SecurityIdentifier requires a non-null string 'symbol'");
            }
            _symbol = symbol;
            _properties = properties;
            if (underlying != Empty)
            {
                _underlying = new SidBox(underlying);
            }
        }

        #endregion

        #region AddMarket, GetMarketCode, and Generate

        /// <summary>
        /// Generates a new <see cref="SecurityIdentifier"/> for an option
        /// </summary>
        /// <param name="expiry">The date the option expires</param>
        /// <param name="underlying">The underlying security's symbol</param>
        /// <param name="market">The market</param>
        /// <param name="strike">The strike price</param>
        /// <param name="optionRight">The option type, call or put</param>
        /// <param name="optionStyle">The option style, American or European</param>
        /// <returns>A new <see cref="SecurityIdentifier"/> representing the specified option security</returns>
        public static SecurityIdentifier GenerateOption(DateTime expiry,
            SecurityIdentifier underlying,
            string market,
            decimal strike,
            OptionRight optionRight,
            OptionStyle optionStyle)
        {
            return Generate(expiry, underlying.Symbol, SecurityType.Option, market, strike, optionRight, optionStyle, underlying);
        }

        /// <summary>
        /// Generates a new <see cref="SecurityIdentifier"/> for a future
        /// </summary>
        /// <param name="expiry">The date the future expires</param>
        /// <param name="symbol">The security's symbol</param>
        /// <param name="market">The market</param>
        /// <returns>A new <see cref="SecurityIdentifier"/> representing the specified futures security</returns>
        public static SecurityIdentifier GenerateFuture(DateTime expiry,
            string symbol,
            string market)
        {
            return Generate(expiry, symbol, SecurityType.Future, market);
        }

        /// <summary>
        /// Helper overload that will search the mapfiles to resolve the first date. This implementation
        /// uses the configured <see cref="IMapFileProvider"/> via the <see cref="Composer.Instance"/>
        /// </summary>
        /// <param name="symbol">The symbol as it is known today</param>
        /// <param name="market">The market</param>
        /// <param name="mapSymbol">Specifies if symbol should be mapped using map file provider</param>
        /// <returns>A new <see cref="SecurityIdentifier"/> representing the specified symbol today</returns>
        public static SecurityIdentifier GenerateEquity(string symbol, string market, bool mapSymbol = true)
        {
            if (mapSymbol)
            {
                var provider = Composer.Instance.GetExportedValueByTypeName<IMapFileProvider>(MapFileProviderTypeName);
                var resolver = provider.Get(market);
                var mapFile = resolver.ResolveMapFile(symbol, DateTime.Today);
                var firstDate = mapFile.FirstDate;
                if (mapFile.Any())
                {
                    symbol = mapFile.OrderBy(x => x.Date).First().MappedSymbol;
                }
                return GenerateEquity(firstDate, symbol, market);
            }
            else
            {
                return GenerateEquity(DefaultDate, symbol, market);
            }

        }

        /// <summary>
        /// Generates a new <see cref="SecurityIdentifier"/> for an equity
        /// </summary>
        /// <param name="date">The first date this security traded (in LEAN this is the first date in the map_file</param>
        /// <param name="symbol">The ticker symbol this security traded under on the <paramref name="date"/></param>
        /// <param name="market">The security's market</param>
        /// <returns>A new <see cref="SecurityIdentifier"/> representing the specified equity security</returns>
        public static SecurityIdentifier GenerateEquity(DateTime date, string symbol, string market)
        {
            return Generate(date, symbol, SecurityType.Equity, market);
        }

        /// <summary>
        /// Generates a new <see cref="SecurityIdentifier"/> for a custom security
        /// </summary>
        /// <param name="symbol">The ticker symbol of this security</param>
        /// <param name="market">The security's market</param>
        /// <returns>A new <see cref="SecurityIdentifier"/> representing the specified base security</returns>
        public static SecurityIdentifier GenerateBase(string symbol, string market)
        {
            return Generate(DefaultDate, symbol, SecurityType.Base, market);
        }

        /// <summary>
        /// Generates a new <see cref="SecurityIdentifier"/> for a forex pair
        /// </summary>
        /// <param name="symbol">The currency pair in the format similar to: 'EURUSD'</param>
        /// <param name="market">The security's market</param>
        /// <returns>A new <see cref="SecurityIdentifier"/> representing the specified forex pair</returns>
        public static SecurityIdentifier GenerateForex(string symbol, string market)
        {
            return Generate(DefaultDate, symbol, SecurityType.Forex, market);
        }

        /// <summary>
        /// Generates a new <see cref="SecurityIdentifier"/> for a Crypto pair
        /// </summary>
        /// <param name="symbol">The currency pair in the format similar to: 'EURUSD'</param>
        /// <param name="market">The security's market</param>
        /// <returns>A new <see cref="SecurityIdentifier"/> representing the specified Crypto pair</returns>
        public static SecurityIdentifier GenerateCrypto(string symbol, string market)
        {
            return Generate(DefaultDate, symbol, SecurityType.Crypto, market);
        }

        /// <summary>
        /// Generates a new <see cref="SecurityIdentifier"/> for a CFD security
        /// </summary>
        /// <param name="symbol">The CFD contract symbol</param>
        /// <param name="market">The security's market</param>
        /// <returns>A new <see cref="SecurityIdentifier"/> representing the specified CFD security</returns>
        public static SecurityIdentifier GenerateCfd(string symbol, string market)
        {
            return Generate(DefaultDate, symbol, SecurityType.Cfd, market);
        }

        /// <summary>
        /// Generic generate method. This method should be used carefully as some parameters are not required and
        /// some parameters mean different things for different security types
        /// </summary>
        private static SecurityIdentifier Generate(DateTime date,
            string symbol,
            SecurityType securityType,
            string market,
            decimal strike = 0,
            OptionRight optionRight = 0,
            OptionStyle optionStyle = 0,
            SecurityIdentifier? underlying = null)
        {
            if ((ulong)securityType >= SecurityTypeWidth || securityType < 0)
            {
                throw new ArgumentOutOfRangeException("securityType", "securityType must be between 0 and 99");
            }
            if ((int)optionRight > 1 || optionRight < 0)
            {
                throw new ArgumentOutOfRangeException("optionRight", "optionType must be either 0 or 1");
            }

            // normalize input strings
            market = market.ToLower();
            symbol = symbol.ToUpper();

            var marketIdentifier = QuantConnect.Market.Encode(market);
            if (!marketIdentifier.HasValue)
            {
                throw new ArgumentOutOfRangeException("market", string.Format("The specified market wasn't found in the markets lookup. Requested: {0}. " +
                    "You can add markets by calling QuantConnect.Market.AddMarket(string,ushort)", market));
            }

            var days = (ulong)date.ToOADate() * DaysOffset;
            var marketCode = (ulong)marketIdentifier * MarketOffset;

            ulong strikeScale;
            var strk = NormalizeStrike(strike, out strikeScale) * StrikeOffset;
            strikeScale *= StrikeScaleOffset;
            var style = (ulong)optionStyle * OptionStyleOffset;
            var putcall = (ulong)optionRight * PutCallOffset;

            var otherData = putcall + days + style + strk + strikeScale + marketCode + (ulong)securityType;

            return new SecurityIdentifier(symbol, otherData, underlying ?? Empty);
        }

        /// <summary>
        /// Converts an upper case alpha numeric string into a long
        /// </summary>
        private static ulong DecodeBase36(string symbol)
        {
            var result = 0ul;
            var baseValue = 1ul;
            for (var i = symbol.Length - 1; i > -1; i--)
            {
                var c = symbol[i];

                // assumes alpha numeric upper case only strings
                var value = (uint)(c <= 57
                    ? c - '0'
                    : c - 'A' + 10);

                result += baseValue * value;
                baseValue *= 36;
            }

            return result;
        }

        /// <summary>
        /// Converts a long to an uppercase alpha numeric string
        /// </summary>
        private static string EncodeBase36(ulong data)
        {
            var stack = new Stack<char>();
            while (data != 0)
            {
                var value = data % 36;
                var c = value < 10
                    ? (char)(value + '0')
                    : (char)(value - 10 + 'A');

                stack.Push(c);
                data /= 36;
            }
            return new string(stack.ToArray());
        }

        /// <summary>
        /// The strike is normalized into deci-cents and then a scale factor
        /// is also saved to bring it back to un-normalized
        /// </summary>
        private static ulong NormalizeStrike(decimal strike, out ulong scale)
        {
            var str = strike;

            if (strike == 0)
            {
                scale = 0;
                return 0;
            }

            // convert strike to default scaling, this keeps the scale always positive
            strike *= StrikeDefaultScaleExpanded;

            scale = 0;
            while (strike % 10 == 0)
            {
                strike /= 10;
                scale++;
            }

            if (strike >= 1000000)
            {
                throw new ArgumentException("The specified strike price's precision is too high: " + str);
            }

            return (ulong)strike;
        }

        /// <summary>
        /// Accurately performs the integer exponentiation
        /// </summary>
        private static ulong Pow(uint x, int pow)
        {
            // don't use Math.Pow(double, double) due to precision issues
            return (ulong)BigInteger.Pow(x, pow);
        }

        #endregion

        #region Parsing routines

        /// <summary>
        /// Parses the specified string into a <see cref="SecurityIdentifier"/>
        /// The string must be a 40 digit number. The first 20 digits must be parseable
        /// to a 64 bit unsigned integer and contain ancillary data about the security.
        /// The second 20 digits must also be parseable as a 64 bit unsigned integer and
        /// contain the symbol encoded from base36, this provides for 12 alpha numeric case
        /// insensitive characters.
        /// </summary>
        /// <param name="value">The string value to be parsed</param>
        /// <returns>A new <see cref="SecurityIdentifier"/> instance if the <paramref name="value"/> is able to be parsed.</returns>
        /// <exception cref="FormatException">This exception is thrown if the string's length is not exactly 40 characters, or
        /// if the components are unable to be parsed as 64 bit unsigned integers</exception>
        public static SecurityIdentifier Parse(string value)
        {
            Exception exception;
            SecurityIdentifier identifier;
            if (!TryParse(value, out identifier, out exception))
            {
                throw exception;
            }

            return identifier;
        }

        /// <summary>
        /// Attempts to parse the specified <see paramref="value"/> as a <see cref="SecurityIdentifier"/>.
        /// </summary>
        /// <param name="value">The string value to be parsed</param>
        /// <param name="identifier">The result of parsing, when this function returns true, <paramref name="identifier"/>
        /// was properly created and reflects the input string, when this function returns false <paramref name="identifier"/>
        /// will equal default(SecurityIdentifier)</param>
        /// <returns>True on success, otherwise false</returns>
        public static bool TryParse(string value, out SecurityIdentifier identifier)
        {
            Exception exception;
            return TryParse(value, out identifier, out exception);
        }

        /// <summary>
        /// Helper method impl to be used by parse and tryparse
        /// </summary>
        private static bool TryParse(string value, out SecurityIdentifier identifier, out Exception exception)
        {
            if (!TryParseProperties(value, out exception, out identifier))
            {
                return false;
            }

            return true;
        }

        private static readonly char[] SplitSpace = {' '};

        /// <summary>
        /// Parses the string into its component ulong pieces
        /// </summary>
        private static bool TryParseProperties(string value, out Exception exception, out SecurityIdentifier identifier)
        {
            exception = null;
            identifier = Empty;

            if (string.IsNullOrWhiteSpace(value))
            {
                return true;
            }

            try
            {
                var sids = value.Split('|');
                for (var i = sids.Length - 1; i > -1; i--)
                {
                    var current = sids[i];
                    var parts = current.Split(SplitSpace, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length != 2)
                    {
                        exception = new FormatException("The string must be splittable on space into two parts.");
                        return false;
                    }

                    var symbol = parts[0];
                    var otherData = parts[1];
                    var props = DecodeBase36(otherData);

                    // toss the previous in as the underlying, if Empty, ignored by ctor
                    identifier = new SecurityIdentifier(symbol, props, identifier);
                }
            }
            catch (Exception error)
            {
                exception = error;
                Log.Error("SecurityIdentifier.TryParseProperties(): Error parsing SecurityIdentifier: '{0}', Exception: {1}", value, exception);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Extracts the embedded value from _otherData
        /// </summary>
        private ulong ExtractFromProperties(ulong offset, ulong width)
        {
            return (_properties/offset)%width;
        }

        #endregion

        #region Equality members and ToString

        /// <summary>
        /// Indicates whether the current object is equal to another object of the same type.
        /// </summary>
        /// <returns>
        /// true if the current object is equal to the <paramref name="other"/> parameter; otherwise, false.
        /// </returns>
        /// <param name="other">An object to compare with this object.</param>
        public bool Equals(SecurityIdentifier other)
        {
            return _properties == other._properties
                && _symbol == other._symbol
                && _underlying == other._underlying;
        }

        /// <summary>
        /// Determines whether the specified <see cref="T:System.Object"/> is equal to the current <see cref="T:System.Object"/>.
        /// </summary>
        /// <returns>
        /// true if the specified object  is equal to the current object; otherwise, false.
        /// </returns>
        /// <param name="obj">The object to compare with the current object. </param><filterpriority>2</filterpriority>
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (obj.GetType() != GetType()) return false;
            return Equals((SecurityIdentifier)obj);
        }

        /// <summary>
        /// Serves as a hash function for a particular type.
        /// </summary>
        /// <returns>
        /// A hash code for the current <see cref="T:System.Object"/>.
        /// </returns>
        /// <filterpriority>2</filterpriority>
        public override int GetHashCode()
        {
            unchecked { return (_symbol.GetHashCode()*397) ^ _properties.GetHashCode(); }
        }

        /// <summary>
        /// Override equals operator
        /// </summary>
        public static bool operator ==(SecurityIdentifier left, SecurityIdentifier right)
        {
            return Equals(left, right);
        }

        /// <summary>
        /// Override not equals operator
        /// </summary>
        public static bool operator !=(SecurityIdentifier left, SecurityIdentifier right)
        {
            return !Equals(left, right);
        }

        /// <summary>
        /// Returns a string that represents the current object.
        /// </summary>
        /// <returns>
        /// A string that represents the current object.
        /// </returns>
        /// <filterpriority>2</filterpriority>
        public override string ToString()
        {
            var props = EncodeBase36(_properties);
            if (HasUnderlying)
            {
                return _symbol + ' ' + props + '|' + _underlying.SecurityIdentifier;
            }
            return _symbol + ' ' + props;
        }

        #endregion

        /// <summary>
        /// Provides a reference type container for a security identifier instance.
        /// This is used to maintain a reference to an underlying
        /// </summary>
        private sealed class SidBox : IEquatable<SidBox>
        {
            public readonly SecurityIdentifier SecurityIdentifier;
            public SidBox(SecurityIdentifier securityIdentifier)
            {
                SecurityIdentifier = securityIdentifier;
            }
            public bool Equals(SidBox other)
            {
                if (ReferenceEquals(null, other)) return false;
                if (ReferenceEquals(this, other)) return true;
                return SecurityIdentifier.Equals(other.SecurityIdentifier);
            }
            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj)) return false;
                if (ReferenceEquals(this, obj)) return true;
                return obj is SidBox && Equals((SidBox)obj);
            }
            public override int GetHashCode()
            {
                return SecurityIdentifier.GetHashCode();
            }
            public static bool operator ==(SidBox left, SidBox right)
            {
                return Equals(left, right);
            }
            public static bool operator !=(SidBox left, SidBox right)
            {
                return !Equals(left, right);
            }
        }
    }
}
