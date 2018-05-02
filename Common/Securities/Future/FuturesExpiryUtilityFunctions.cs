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
using System.Linq;


namespace QuantConnect.Securities.Future
{
    /// <summary>
    /// Class to implement common functions used in FuturesExpiryFunctions
    /// </summary>
    public static class FuturesExpiryUtilityFunctions
    {
        /// <summary>
        /// Method to retrieve n^th succeeding/preceding business day for a given day
        /// </summary>
        /// <param name="time">The current Time</param>
        /// <param name="n">Number of business days succeeding current time. Use negative value for preceding business days</param>
        /// <returns>The date-time after adding n business days</returns>
        public static DateTime AddBusinessDays(DateTime time, int n)
        {
            if (n < 0)
            {
                var businessDays = (-1) * n;
                var totalDays = 1;
                do
                {
                    var previousDay = time.AddDays(-totalDays);
                    if (NotHoliday(previousDay))
                    {
                        businessDays--;
                    }
                    if (businessDays > 0) totalDays++;
                } while (businessDays > 0);

                return time.AddDays(-totalDays);
            }
            else
            {
                var businessDays = n;
                var totalDays = 1;
                do
                {
                    var previousDay = time.AddDays(totalDays);
                    if (NotHoliday(previousDay))
                    {
                        businessDays--;
                    }
                    if (businessDays > 0) totalDays++;
                } while (businessDays > 0);

                return time.AddDays(totalDays);
            }
        }

        /// <summary>
        /// Method to retrieve the n^th last business day of the delivery month.
        /// </summary>
        /// <param name="time">DateTime for delivery month</param>
        /// <param name="n">Number of days</param>
        /// <returns>Nth Last Business day of the month</returns>
        public static DateTime NthLastBusinessDay(DateTime time, int n)
        {
            var daysInMonth = DateTime.DaysInMonth(time.Year, time.Month);
            var lastDayOfMonth = new DateTime(time.Year, time.Month, daysInMonth);
            if(n > daysInMonth)
            {
                throw new ArgumentOutOfRangeException("n",String.Format("Number of days ({0}) is larger than the size of month({1})", n, daysInMonth));
            }
            // Count the number of days in the month after the third to last business day
            var businessDays = n;
            var totalDays = 0;
            do
            {
                var previousDay = lastDayOfMonth.AddDays(-totalDays);
                if (NotHoliday(previousDay))
                {
                    businessDays--;
                }
                if (businessDays > 0) totalDays++;
            } while (businessDays > 0);

            return lastDayOfMonth.AddDays(-totalDays);
        }

        /// <summary>
        /// Method to retrieve the 3rd Friday of the given month
        /// </summary>
        /// <param name="time">Date from the given month</param>
        /// <returns>ThirdFriday of given month</returns>
        public static DateTime ThirdFriday(DateTime time)
        {
            var daysInMonth = DateTime.DaysInMonth(time.Year, time.Month);
            return (from day in Enumerable.Range(1, daysInMonth)
                    where new DateTime(time.Year, time.Month, day).DayOfWeek == DayOfWeek.Friday
                    select new DateTime(time.Year, time.Month, day)).ElementAt(2);
        }

        /// <summary>
        /// Method to retrieve third Wednesday of the given month (usually Monday).
        /// </summary>
        /// <param name="time">Date from the given month</param>
        /// <returns>Third Wednesday of the given month</returns>
        public static DateTime ThirdWednesday(DateTime time)
        {
            var daysInMonth = DateTime.DaysInMonth(time.Year, time.Month);
            return (from day in Enumerable.Range(1, daysInMonth)
                                  where new DateTime(time.Year, time.Month, day).DayOfWeek == DayOfWeek.Wednesday
                                  select new DateTime(time.Year, time.Month, day)).ElementAt(2);
        }
        
        /// <summary>
        /// Method to check whether a given time is holiday or not
        /// </summary>
        /// <param name="time">The DateTime for consideration</param>
        /// <returns>True if the time is not a holidays, otherwise returns false</returns>
        public static bool NotHoliday(DateTime time)
        {
            return time.IsCommonBusinessDay() && !USHoliday.Dates.Contains(time);
        }

        /// <summary>
        /// This function takes Thursday as input and returns true if four weekdays preceding it are not Holidays
        /// </summary>
        /// <param name="thursday">DateTime of a given Thursday</param>
        /// <returns>False if DayOfWeek is not Thursday or is not preceded by four weekdays,Otherwise returns True</returns>
        public static bool NotPrecededByHoliday(DateTime thursday)
        {
            if (thursday.DayOfWeek != DayOfWeek.Thursday)
            {
                throw new ArgumentException("Input to NotPrecededByHolidays must be a Thursday");
            }
            var result = true;
            // for Monday, Tuesday and Wednesday
            for (var i = 1; i <= 3; i++)
            {
                if (!NotHoliday(thursday.AddDays(-i)))
                {
                    result = false;
                }
            }
            // for Friday
            if (!NotHoliday(thursday.AddDays(-6)))
            {
                result = false;
            }
            return result;
        }

    }
}
