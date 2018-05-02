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
AddReference("QuantConnect.Algorithm.Framework")

from QuantConnect.Algorithm.Framework.Portfolio import PortfolioTarget

class MaximumDrawdownPercentPerSecurity():
    '''Provides an implementation of IRiskManagementModel that limits the drawdown per holding to the specified percentage'''

    def __init__(self, maximumDrawdownPercent = 0.05):
        '''Initializes a new instance of the MaximumDrawdownPercentPerSecurity class
        Args:
            maximumDrawdownPercent: The maximum percentage drawdown allowed for any single security holding'''
        self.maximumDrawdownPercent = -abs(maximumDrawdownPercent)

    def ManageRisk(self, algorithm, targets):
        '''Manages the algorithm's risk at each time step
        Args:
            algorithm: The algorithm instance'''
        for kvp in algorithm.Securities:
            security = kvp.Value

            if not security.Invested:
                continue

            pnl = security.Holdings.UnrealizedProfitPercent
            if pnl < self.maximumDrawdownPercent:
                # remove PortfolioTarget with symbol to be liquidated and add new PortfolioTarget
                targets = list(filter(lambda x: x.Symbol != security.Symbol, targets))
                targets.append(PortfolioTarget(security.Symbol, 0))

        return targets

    def OnSecuritiesChanged(self, algorithm, changes):
        '''Event fired each time the we add/remove securities from the data feed
        Args:
            algorithm: The algorithm instance that experienced the change in securities
            changes: The security additions and removals from the algorithm'''
        pass