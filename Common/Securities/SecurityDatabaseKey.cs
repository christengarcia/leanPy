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

namespace QuantConnect.Securities
{
    /// <summary>
    /// Represents the key to a single entry in the <see cref="MarketHoursDatabase"/> or the <see cref="SymbolPropertiesDatabase"/>
    /// </summary>
    public class SecurityDatabaseKey : IEquatable<SecurityDatabaseKey>
    {
        /// <summary>
        /// Represents that the specified symbol or market field will match all
        /// </summary>
        public const string Wildcard = "[*]";

        /// <summary>
        /// The market. If null, ignore market filtering
        /// </summary>
        public readonly string Market;

        /// <summary>
        /// The symbol. If null, ignore symbol filtering
        /// </summary>
        public readonly string Symbol;

        /// <summary>
        /// The security type
        /// </summary>
        public readonly SecurityType SecurityType;

        /// <summary>
        /// Initializes a new instance of the <see cref="SecurityDatabaseKey"/> class
        /// </summary>
        /// <param name="market">The market</param>
        /// <param name="symbol">The symbol. specify null to apply to all symbols in market/security type</param>
        /// <param name="securityType">The security type</param>
        public SecurityDatabaseKey(string market, string symbol, SecurityType securityType)
        {
            Market = string.IsNullOrEmpty(market) ? Wildcard : market;
            SecurityType = securityType;
            Symbol = string.IsNullOrEmpty(symbol) ? Wildcard : symbol;
        }

        /// <summary>
        /// Parses the specified string as a <see cref="SecurityDatabaseKey"/>
        /// </summary>
        /// <param name="key">The string representation of the key</param>
        /// <returns>A new <see cref="SecurityDatabaseKey"/> instance</returns>
        public static SecurityDatabaseKey Parse(string key)
        {
            var parts = key.Split('-');
            if (parts.Length != 3)
            {
                throw new FormatException("The specified key was not in the expected format: " + key);
            }
            SecurityType type;
            if (!Enum.TryParse(parts[0], out type))
            {
                throw new ArgumentException("Unable to parse '" + parts[2] + "' as a SecurityType.");
            }

            return new SecurityDatabaseKey(parts[1], parts[2], type);
        }

        #region Equality members

        /// <summary>
        /// Indicates whether the current object is equal to another object of the same type.
        /// </summary>
        /// <returns>
        /// true if the current object is equal to the <paramref name="other"/> parameter; otherwise, false.
        /// </returns>
        /// <param name="other">An object to compare with this object.</param>
        public bool Equals(SecurityDatabaseKey other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return string.Equals(Market, other.Market) && Equals(Symbol, other.Symbol) && SecurityType == other.SecurityType;
        }

        /// <summary>
        /// Determines whether the specified object is equal to the current object.
        /// </summary>
        /// <returns>
        /// true if the specified object  is equal to the current object; otherwise, false.
        /// </returns>
        /// <param name="obj">The object to compare with the current object. </param>
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((SecurityDatabaseKey) obj);
        }

        /// <summary>
        /// Serves as the default hash function. 
        /// </summary>
        /// <returns>
        /// A hash code for the current object.
        /// </returns>
        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = (Market != null ? Market.GetHashCode() : 0);
                hashCode = (hashCode*397) ^ (Symbol != null ? Symbol.GetHashCode() : 0);
                hashCode = (hashCode*397) ^ (int) SecurityType;
                return hashCode;
            }
        }

        public static bool operator ==(SecurityDatabaseKey left, SecurityDatabaseKey right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(SecurityDatabaseKey left, SecurityDatabaseKey right)
        {
            return !Equals(left, right);
        }

        #endregion

        /// <summary>
        /// Returns a string that represents the current object.
        /// </summary>
        /// <returns>
        /// A string that represents the current object.
        /// </returns>
        public override string ToString()
        {
            return string.Format("{0}-{1}-{2}", SecurityType, Market ?? Wildcard, Symbol ?? Wildcard);
        }
    }
}
