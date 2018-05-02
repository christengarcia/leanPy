/* 
 * OANDA v20 REST API
 *
 * The full OANDA v20 REST API Specification. This specification defines how to interact with v20 Accounts, Trades, Orders, Pricing and more.
 *
 * OpenAPI spec version: 3.0.15
 * Contact: api@oanda.com
 * Generated by: https://github.com/swagger-api/swagger-codegen.git
 */

using System;
using System.Linq;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.ComponentModel.DataAnnotations;

namespace Oanda.RestV20.Model
{
    /// <summary>
    /// The response body for the Transaction Stream uses chunked transfer encoding.  Each chunk contains Transaction and/or TransactionHeartbeat objects encoded as JSON.  Each JSON object is serialized into a single line of text, and multiple objects found in the same chunk are separated by newlines. TransactionHeartbeats are sent every 5 seconds.
    /// </summary>
    [DataContract]
    public partial class InlineResponse20020 :  IEquatable<InlineResponse20020>, IValidatableObject
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="InlineResponse20020" /> class.
        /// </summary>
        /// <param name="Transaction">Transaction.</param>
        /// <param name="Heartbeat">Heartbeat.</param>
        public InlineResponse20020(Transaction Transaction = default(Transaction), TransactionHeartbeat Heartbeat = default(TransactionHeartbeat))
        {
            this.Transaction = Transaction;
            this.Heartbeat = Heartbeat;
        }
        
        /// <summary>
        /// Gets or Sets Transaction
        /// </summary>
        [DataMember(Name="transaction", EmitDefaultValue=false)]
        public Transaction Transaction { get; set; }
        /// <summary>
        /// Gets or Sets Heartbeat
        /// </summary>
        [DataMember(Name="heartbeat", EmitDefaultValue=false)]
        public TransactionHeartbeat Heartbeat { get; set; }
        /// <summary>
        /// Returns the string presentation of the object
        /// </summary>
        /// <returns>String presentation of the object</returns>
        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append("class InlineResponse20020 {\n");
            sb.Append("  Transaction: ").Append(Transaction).Append("\n");
            sb.Append("  Heartbeat: ").Append(Heartbeat).Append("\n");
            sb.Append("}\n");
            return sb.ToString();
        }
  
        /// <summary>
        /// Returns the JSON string presentation of the object
        /// </summary>
        /// <returns>JSON string presentation of the object</returns>
        public string ToJson()
        {
            return JsonConvert.SerializeObject(this, Formatting.Indented);
        }

        /// <summary>
        /// Returns true if objects are equal
        /// </summary>
        /// <param name="obj">Object to be compared</param>
        /// <returns>Boolean</returns>
        public override bool Equals(object obj)
        {
            // credit: http://stackoverflow.com/a/10454552/677735
            return this.Equals(obj as InlineResponse20020);
        }

        /// <summary>
        /// Returns true if InlineResponse20020 instances are equal
        /// </summary>
        /// <param name="other">Instance of InlineResponse20020 to be compared</param>
        /// <returns>Boolean</returns>
        public bool Equals(InlineResponse20020 other)
        {
            // credit: http://stackoverflow.com/a/10454552/677735
            if (other == null)
                return false;

            return 
                (
                    this.Transaction == other.Transaction ||
                    this.Transaction != null &&
                    this.Transaction.Equals(other.Transaction)
                ) && 
                (
                    this.Heartbeat == other.Heartbeat ||
                    this.Heartbeat != null &&
                    this.Heartbeat.Equals(other.Heartbeat)
                );
        }

        /// <summary>
        /// Gets the hash code
        /// </summary>
        /// <returns>Hash code</returns>
        public override int GetHashCode()
        {
            // credit: http://stackoverflow.com/a/263416/677735
            unchecked // Overflow is fine, just wrap
            {
                int hash = 41;
                // Suitable nullity checks etc, of course :)
                if (this.Transaction != null)
                    hash = hash * 59 + this.Transaction.GetHashCode();
                if (this.Heartbeat != null)
                    hash = hash * 59 + this.Heartbeat.GetHashCode();
                return hash;
            }
        }

        /// <summary>
        /// To validate all properties of the instance
        /// </summary>
        /// <param name="validationContext">Validation context</param>
        /// <returns>Validation Result</returns>
        IEnumerable<System.ComponentModel.DataAnnotations.ValidationResult> IValidatableObject.Validate(ValidationContext validationContext)
        { 
            yield break;
        }
    }

}
