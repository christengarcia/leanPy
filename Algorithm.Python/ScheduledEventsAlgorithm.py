﻿# QUANTCONNECT.COM - Democratizing Finance, Empowering Individuals.
# Lean Algorithmic Trading Engine v2.0. Copyright 2014 QuantConnect Corporation.
#
# Licensed under the Apache License, Version 2.0 (the "License");
# you may not use this file except in compliance with the License.
# You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
#
# Unless required by applicable law or agreed to in writing, software
# distributed under the License is distributed on an "AS IS" BASIS,
# WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
# See the License for the specific language governing permissions and
# limitations under the License.

from clr import AddReference
AddReference("System")
AddReference("QuantConnect.Algorithm")
AddReference("QuantConnect.Common")

from System import *
from QuantConnect import *
from QuantConnect.Algorithm import *
from QuantConnect.Data import *
from datetime import timedelta

### <summary>
### Demonstration of the Scheduled Events features available in QuantConnect.
### </summary>
### <meta name="tag" content="scheduled events" />
### <meta name="tag" content="date rules" />
### <meta name="tag" content="time rules" />
class ScheduledEventsAlgorithm(QCAlgorithm):

    def Initialize(self):
        '''Initialise the data and resolution required, as well as the cash and start-end dates for your algorithm. All algorithms must initialized.'''

        self.SetStartDate(2013,10,7)   #Set Start Date
        self.SetEndDate(2013,10,11)    #Set End Date
        self.SetCash(100000)           #Set Strategy Cash
        # Find more symbols here: http://quantconnect.com/data
        self.AddEquity("SPY")

        # events are scheduled using date and time rules
        # date rules specify on what dates and event will fire
        # time rules specify at what time on thos dates the event will fire

        # Python note:
        # Schedule.On third argument type is System.Action or System.Action[System.String,System.DateTime]
        # we need to cast the callback function using Action(...) to make it work

        # schedule an event to fire at a specific date/time
        self.Schedule.On(self.DateRules.On(2013, 10, 7), self.TimeRules.At(13, 0), Action(self.SpecificTime))

        # schedule an event to fire every trading day for a security the
        # time rule here tells it to fire 10 minutes after SPY's market open
        self.Schedule.On(self.DateRules.EveryDay("SPY"), self.TimeRules.AfterMarketOpen("SPY", 10), Action(self.EveryDayAfterMarketOpen))

        # schedule an event to fire every trading day for a security the
        # time rule here tells it to fire 10 minutes before SPY's market close
        self.Schedule.On(self.DateRules.EveryDay("SPY"), self.TimeRules.BeforeMarketClose("SPY", 10), Action(self.EveryDayAfterMarketClose))

        # schedule an event to fire on certain days of the week
        self.Schedule.On(self.DateRules.Every(DayOfWeek.Monday, DayOfWeek.Friday), self.TimeRules.At(12, 0), Action(self.EveryMonFriAtNoon))

        # the scheduling methods return the ScheduledEvent object which can be used for other things here I set
        # the event up to check the portfolio value every 10 minutes, and liquidate if we have too many losses
        self.Schedule.On(self.DateRules.EveryDay(), self.TimeRules.Every(timedelta(minutes=10)), Action(self.LiquidateUnrealizedLosses))

        # schedule an event to fire at the beginning of the month, the symbol is optional
        # if specified, it will fire the first trading day for that symbol of the month,
        # if not specified it will fire on the first day of the month
        self.Schedule.On(self.DateRules.MonthStart("SPY"), self.TimeRules.AfterMarketOpen("SPY"), Action(self.RebalancingCode))


    def OnData(self, data):
        '''OnData event is the primary entry point for your algorithm. Each new data point will be pumped in here.'''
        if not self.Portfolio.Invested:
            self.SetHoldings("SPY", 1)


    def SpecificTime(self):
        self.Log("SpecificTime: Fired at : {0}".format(self.Time))


    def EveryDayAfterMarketOpen(self):
        self.Log("EveryDay.SPY 10 min after open: Fired at: {0}".format(self.Time))


    def EveryDayAfterMarketClose(self):
        self.Log("EveryDay.SPY 10 min before close: Fired at: {0}".format(self.Time))


    def EveryMonFriAtNoon(self):
        self.Log("Mon/Fri at 12pm: Fired at: {0}".format(self.Time))


    def LiquidateUnrealizedLosses(self):
        ''' if we have over 1000 dollars in unrealized losses, liquidate'''
        if self.Portfolio.TotalUnrealizedProfit < -1000:
            self.Log("Liquidated due to unrealized losses at: {0}".format(self.Time))
            self.Liquidate()


    def RebalancingCode(self):
        ''' Good spot for rebalancing code?'''
        pass