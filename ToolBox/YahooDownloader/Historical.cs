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
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;

namespace QuantConnect.ToolBox.YahooDownloader
{
    /// <summary>
    /// Class for fetching stock historical price from Yahoo Finance
    /// </summary>
    public class Historical
    {
        /// <summary>
        /// Get stock historical price from Yahoo Finance
        /// </summary>
        /// <param name="symbol">Stock ticker symbol</param>
        /// <param name="start">Starting datetime</param>
        /// <param name="end">Ending datetime</param>
        /// <returns>List of history price</returns>
        public static List<HistoryPrice> Get(string symbol, DateTime start, DateTime end, string eventCode)
        {
            var historyPrices = new List<HistoryPrice>();

            try
            {
                var csvData = GetRaw(symbol, start, end, eventCode);
                if (csvData != null)
                {
                    historyPrices = Parse(csvData);
                }
            }
            catch (Exception ex)
            {
                Debug.Print(ex.Message);
            }

            return historyPrices;

        }

        /// <summary>
        /// Get raw stock historical price from Yahoo Finance
        /// </summary>
        /// <param name="symbol">Stock ticker symbol</param>
        /// <param name="start">Starting datetime</param>
        /// <param name="end">Ending datetime</param>
        /// <returns>Raw history price string</returns>
        public static string GetRaw(string symbol, DateTime start, DateTime end, string eventCode)
        {

            string csvData = null;

            try
            {
                var url = "https://query1.finance.yahoo.com/v7/finance/download/{0}?period1={1}&period2={2}&interval=1d&events={3}&crumb={4}";

                //if no token found, refresh it
                if (string.IsNullOrEmpty(Token.Cookie) | string.IsNullOrEmpty(Token.Crumb))
                {
                    if (!Token.Refresh(symbol))
                    {
                        return GetRaw(symbol, start, end, eventCode);
                    }
                }

                url = string.Format(url, symbol, Math.Round(Time.DateTimeToUnixTimeStamp(start), 0), Math.Round(Time.DateTimeToUnixTimeStamp(end), 0), eventCode, Token.Crumb);
                using (var wc = new WebClient())
                {
                    wc.Headers.Add(HttpRequestHeader.Cookie, Token.Cookie);
                    csvData = wc.DownloadString(url);
                }

            }
            catch (WebException webEx)
            {
                var response = (HttpWebResponse)webEx.Response;

                //Re-fecthing token
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    Debug.Print(webEx.Message);
                    Token.Reset();
                    Debug.Print("Re-fetch");
                    return GetRaw(symbol, start, end, eventCode);
                }
                throw;

            }
            catch (Exception ex)
            {
                Debug.Print(ex.Message);
            }

            return csvData;

        }

        /// <summary>
        /// Parse raw historical price data into list
        /// </summary>
        /// <param name="csvData"></param>
        /// <returns></returns>
        private static List<HistoryPrice> Parse(string csvData)
        {

            var hps = new List<HistoryPrice>();

            try
            {
                var rows = csvData.Split(Convert.ToChar(10));

                //row(0) was ignored because is column names 
                //data is read from oldest to latest
                for (var i = 1; i <= rows.Length - 1; i++)
                {

                    var row = rows[i];
                    if (string.IsNullOrEmpty(row))
                    {
                        continue;
                    }

                    var cols = row.Split(',');
                    if (cols[1] == "null")
                    {
                        continue;
                    }

                    var hp = new HistoryPrice
                    {
                        Date = DateTime.Parse(cols[0]),
                        Open = Convert.ToDecimal(cols[1]),
                        High = Convert.ToDecimal(cols[2]),
                        Low = Convert.ToDecimal(cols[3]),
                        Close = Convert.ToDecimal(cols[4]),
                        AdjClose = Convert.ToDecimal(cols[5])
                    };

                    //fixed issue in some currencies quote (e.g: SGDAUD=X)
                    if (cols[6] != "null")
                    {
                        hp.Volume = Convert.ToDecimal(cols[6]);
                    }

                    hps.Add(hp);

                }

            }
            catch (Exception ex)
            {
                Debug.Print(ex.Message);
            }

            return hps;

        }

    }

    public class HistoryPrice
    {
        public DateTime Date { get; set; }
        public decimal Open { get; set; }
        public decimal High { get; set; }
        public decimal Low { get; set; }
        public decimal Close { get; set; }
        public decimal Volume { get; set; }
        public decimal AdjClose { get; set; }
    }
}