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
AddReference("QuantConnect.Common")

from System import *
from QuantConnect import *
from QuantConnect.Orders import *


class ImmediateExecutionModel:
    '''Provides an implementation of IExecutionModel that immediately submits market orders to achieve the desired portfolio targets'''

    def Execute(self, algorithm, targets):
        '''Immediately submits orders for the specified portfolio targets.
        Args:
            algorithm: The algorithm instance
            targets: The portfolio targets to be ordered'''
        for target in targets:
            open_quantity = sum([x.Quantity for x in algorithm.Transactions.GetOpenOrders(target.Symbol)])
            existing = algorithm.Securities[target.Symbol].Holdings.Quantity + open_quantity
            quantity = target.Quantity - existing
            if quantity != 0:
                algorithm.MarketOrder(target.Symbol, quantity)

    def OnSecuritiesChanged(self, algorithm, changes):
        '''Event fired each time the we add/remove securities from the data feed
        Args:
            algorithm: The algorithm instance that experienced the change in securities
            changes: The security additions and removals from the algorithm'''
        pass