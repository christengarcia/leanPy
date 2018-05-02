﻿using QuantConnect.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QuantConnect.Securities.Interfaces
{
    /// <summary>
    /// Enum defines types of possible price adjustments in continuous contract modeling. 
    /// </summary>
    public enum AdjustmentType
    {
        /// ForwardAdjusted - new quotes are adjusted as new data comes
        ForwardAdjusted,
        
        /// BackAdjusted - old quotes are retrospectively adjusted as new data comes
        BackAdjusted
    };

    /// <summary>
    /// Continuous contract model interface. Interfaces is implemented by different classes 
    /// realizing various methods for modeling continuous security series. Primarily, modeling of continuous futures. 
    /// Continuous contracts are used in backtesting of otherwise expiring derivative contracts. 
    /// Continuous contracts are not traded, and are not products traded on exchanges.
    /// </summary>
    public interface IContinuousContractModel
    {
        /// <summary>
        /// Adjustment type, implemented by the model
        /// </summary>
        AdjustmentType AdjustmentType { get; set; }

        /// <summary>
        /// List of current and historical data series for one root symbol. 
        /// e.g. 6BH16, 6BM16, 6BU16, 6BZ16
        /// </summary>
        IEnumerator<BaseData> InputSeries { get; set; }

        /// <summary>
        /// Method returns continuous prices from the list of current and historical data series for one root symbol. 
        /// It returns enumerator of stitched continuous quotes, produced by the model.
        /// e.g. 6BH15, 6BM15, 6BU15, 6BZ15 will result in one 6B continuous historical series for 2015
        /// </summary>
        /// <returns>Continuous prices</returns>
        IEnumerator<BaseData> GetContinuousData(DateTime dateTime);

        /// <summary>
        /// Returns the list of roll dates for the contract. 
        /// </summary>
        /// <returns>The list of roll dates</returns>
        IEnumerator<DateTime> GetRollDates();

        /// <summary>
        /// Returns current symbol name that corresponds to the current continuous model, 
        /// or null if none.
        /// </summary>
        /// <returns>Current symbol name</returns>
        Symbol GetCurrentSymbol(DateTime dateTime);
    }
}
