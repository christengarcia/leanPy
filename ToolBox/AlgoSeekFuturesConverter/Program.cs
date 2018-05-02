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
using QuantConnect.Logging;
using System.Diagnostics;
using System.Globalization;
using QuantConnect.Configuration;
using QuantConnect.Util;
using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace QuantConnect.ToolBox.AlgoSeekFuturesConverter
{
    /// <summary>
    /// AlgoSeek Options Converter: Convert raw OPRA channel files into QuantConnect Options Data Format.
    /// </summary>
    public class Program
    {
        public static void Main(string[] args)
        {
            var date = args[0];

            // There are practical file limits we need to override for this to work. 
            // By default programs are only allowed 1024 files open; for futures parsing we need 100k
            Environment.SetEnvironmentVariable("MONO_MANAGED_WATCHER", "disabled");
            Log.LogHandler = new CompositeLogHandler(new ILogHandler[] { new ConsoleLogHandler(), new FileLogHandler("log.txt") });

            // Directory for the data, output and processed cache:
            var remoteMask = Config.Get("futures-remote-file-mask", "*.bz2").Replace("{0}", date);
            var remoteDirectory = Config.Get("futures-remote-directory").Replace("{0}", date);
            var sourceDirectory = Config.Get("futures-source-directory").Replace("{0}", date);
            var dataDirectory = Config.Get("data-directory").Replace("{0}", date);
            var resolutions = Config.Get("resolutions");
            var cleanSourceDirectory = Config.GetBool("clean-source-directory", false);

            Log.Trace("CONFIGURATION:");
            Log.Trace("Processor Count: " + Environment.ProcessorCount);
            Log.Trace("Remote Directory: " + remoteDirectory);
            Log.Trace("Source Directory: " + sourceDirectory);
            Log.Trace("Destination Directory: " + dataDirectory);

            // Date for the option bz files.
            var referenceDate = DateTime.ParseExact(date, DateFormat.EightCharacter, CultureInfo.InvariantCulture);

            Log.Trace("DateTime: " + referenceDate.Date.ToString());

            // checking if remote folder exists
            if(!Directory.Exists(remoteDirectory))
            {
                Log.Error("Remote Directory doesn't exist: " + remoteDirectory);
                return;
            }

            // prepare tick types
            var resolutionList = new[] { Resolution.Minute };

            if (!string.IsNullOrEmpty(resolutions))
            {
                var names = resolutions.Split(new[] { ';' });
                resolutionList = 
                    names
                    .Where(x => !string.IsNullOrEmpty(x))
                    .Select(name => (Resolution)Enum.Parse(typeof(Resolution), name, true)).ToArray();
            }
  
            Log.Trace("Resolutions: " + string.Join(";", resolutionList.Select(x => x.ToString()).ToArray()));

            // Convert the date:
            var timer = Stopwatch.StartNew();
            var converter = new AlgoSeekFuturesConverter(resolutionList.ToList() , referenceDate, remoteDirectory, remoteMask, sourceDirectory, dataDirectory);
            converter.Convert();
            Log.Trace(string.Format("AlgoSeekFuturesConverter.Main(): {0} Conversion finished in time: {1}", referenceDate, timer.Elapsed));

            // Compress the memory cache to zips.
            timer.Restart();
            converter.Package(referenceDate);
            Log.Trace(string.Format("AlgoSeekFuturesConverter.Main(): {0} Compression finished in time: {1}", referenceDate, timer.Elapsed));

            if (cleanSourceDirectory)
            {
                Log.Trace(string.Format("AlgoSeekFuturesConverter.Main(): Cleaning source directory: {0}", sourceDirectory));

                try
                {
                    Directory.Delete(sourceDirectory, true);
                }
                catch(Exception err)
                {
                    Log.Trace(string.Format("AlgoSeekFuturesConverter.Main(): Error while cleaning source directory {0}", err.Message));
                }
            }
        }
    }
}
