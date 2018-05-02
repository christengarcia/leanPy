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
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using QuantConnect.Data.Market;
using QuantConnect.Logging;

namespace QuantConnect.ToolBox.AlgoSeekFuturesConverter
{
    using Processors = Dictionary<Symbol, List<List<AlgoSeekFuturesProcessor>>>;
    
    /// <summary>
    /// Process a directory of algoseek futures files into separate resolutions.
    /// </summary>
    public class AlgoSeekFuturesConverter
    {
        private const int ExecTimeout = 60;// sec
        private readonly string _source;
        private readonly string _remote;
        private readonly string _remoteMask;
        private readonly string _destination;
        private readonly List<Resolution> _resolutions;
        private readonly DateTime _referenceDate;

        /// <summary>
        /// Create a new instance of the AlgoSeekFutures Converter. Parse a single input directory into an output.
        /// </summary>
        /// <param name="resolutions">Convert this resolution</param>
        /// <param name="referenceDate">Datetime to be added to the milliseconds since midnight. Algoseek data is stored in channel files (XX.bz2) and in a source directory</param>
        /// <param name="remote">Remote directory of the .bz algoseek files</param>
        /// <param name="source">Source directory of the .csv algoseek files</param>
        /// <param name="destination">Destination directory of the processed future files</param>
        public AlgoSeekFuturesConverter(List<Resolution> resolutions, DateTime referenceDate, string remote, string remoteMask, string source, string destination)
        {
            _source = source;
            _remote = remote;
            _remoteMask = remoteMask;
            _referenceDate = referenceDate;
            _destination = destination;
            _resolutions = resolutions;
        }

        /// <summary>
        /// Give the reference date and source directory, convert the algoseek data into n-resolutions LEAN format.
        /// </summary>
        public void Convert()
        {
            //Get the list of all the files, then for each file open a separate streamer.
            var files = Directory.EnumerateFiles(_remote, _remoteMask);
            files = files.Where(x => Path.GetFileNameWithoutExtension(x).ToLower().IndexOf("option") == -1);

            Log.Trace("AlgoSeekFuturesConverter.Convert(): Loading {0} AlgoSeekFuturesReader for {1} ", files.Count(), _referenceDate);

            //Initialize parameters
            var totalLinesProcessed = 0L;
            var totalFiles = files.Count();
            var totalFilesProcessed = 0;
            var start = DateTime.MinValue;

            var zipper = OS.IsWindows ? "C:/Program Files/7-Zip/7z.exe" : "7z";

            var symbolMultipliers = LoadSymbolMultipliers();

            //Extract each file massively in parallel.
            Parallel.ForEach(files, file =>
            {
                try
                {
                    Log.Trace("Remote File :" + file);

                    var csvFile = Path.Combine(_source, Path.GetFileName(file).Replace(Path.GetExtension(file), ""));

                    Log.Trace("Source File :" + csvFile);

                    if (!File.Exists(csvFile))
                    {
                        // create the directory first or else 7z will fail
                        var csvFileInfo = new FileInfo(csvFile);
                        Directory.CreateDirectory(csvFileInfo.DirectoryName);

                        Log.Trace("AlgoSeekFuturesConverter.Convert(): Extracting " + file);
                        var psi = new ProcessStartInfo(zipper, " e " + file + " -o" + _source)
                        {
                            CreateNoWindow = true,
                            WindowStyle = ProcessWindowStyle.Hidden,
                            UseShellExecute = false,
                            RedirectStandardOutput = true
                        };

                        var process = new Process();
                        process.StartInfo = psi;
                        process.Start();

                        while (!process.StandardOutput.EndOfStream)
                        {
                            process.StandardOutput.ReadLine();
                        }

                        if (!process.WaitForExit(ExecTimeout * 1000))
                        {
                            Log.Error("7Zip timed out: " + file);
                        }
                        else
                        {
                            if (process.ExitCode > 0)
                            {
                                Log.Error("7Zip Exited Unsuccessfully: " + file);
                            }
                        }
                    }

                    // setting up local processors 
                    var processors = new Processors();

                    var reader = new AlgoSeekFuturesReader(csvFile, symbolMultipliers);
                    if (start == DateTime.MinValue)
                    {
                        start = DateTime.Now;
                    }

                    if (reader.Current != null) // reader contains the data
                    {
                        do
                        {
                            var tick = reader.Current as Tick;

                            //Add or create the consolidator-flush mechanism for symbol:
                            List<List<AlgoSeekFuturesProcessor>> symbolProcessors;
                            if (!processors.TryGetValue(tick.Symbol, out symbolProcessors))
                            {
                                symbolProcessors = new List<List<AlgoSeekFuturesProcessor>>(3)
                                        {
                                            { _resolutions.Select(x => new AlgoSeekFuturesProcessor(tick.Symbol, _referenceDate, TickType.Trade, x, _destination)).ToList() },
                                            { _resolutions.Select(x => new AlgoSeekFuturesProcessor(tick.Symbol, _referenceDate, TickType.Quote, x, _destination)).ToList() },
                                            { _resolutions.Select(x => new AlgoSeekFuturesProcessor(tick.Symbol, _referenceDate, TickType.OpenInterest, x, _destination)).ToList() }
                                        };

                                processors[tick.Symbol] = symbolProcessors;
                            }

                            // Pass current tick into processor: enum 0 = trade; 1 = quote, 2 = oi
                            foreach (var processor in symbolProcessors[(int)tick.TickType])
                            {
                                processor.Process(tick);
                            }

                            if (Interlocked.Increment(ref totalLinesProcessed) % 1000000m == 0)
                            {
                                var pro = (double)processors.Values.SelectMany(p => p.SelectMany(x => x)).Count();
                                var symbols = (double)processors.Keys.Count();
                                Log.Trace("AlgoSeekFuturesConverter.Convert(): Processed {0,3}M ticks( {1}k / sec); Memory in use: {2} MB; Total progress: {3}%, Processor per symbol {4}", Math.Round(totalLinesProcessed / 1000000m, 2), Math.Round(totalLinesProcessed / 1000L / (DateTime.Now - start).TotalSeconds), Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024), 100 * totalFilesProcessed / totalFiles, pro / symbols);
                            }

                        }
                        while (reader.MoveNext());

                        Log.Trace("AlgoSeekFuturesConverter.Convert(): Performing final flush to disk... ");
                        Flush(processors, DateTime.MaxValue, true);
                    }

                    processors = null;
                    GC.Collect();
                    GC.WaitForPendingFinalizers();

                    Log.Trace("AlgoSeekFuturesConverter.Convert(): Finished processing file: " + file);
                    Interlocked.Increment(ref totalFilesProcessed);
                }
                catch(Exception err)
                {
                    Log.Error("Exception caught! File: {0} Err: {1} Source {2} Stack {3}", file, err.Message, err.Source, err.StackTrace);
                }
            });


        }

        /// <summary>
        /// Private method loads symbol multipliers from algoseek csv file
        /// </summary>
        /// <returns></returns>

        private Dictionary<string, decimal> LoadSymbolMultipliers()
        {
            const int columnsCount = 4;
            const int columnUnderlying = 0;
            const int columnProductName = 1;
            const int columnMultipleFactor = 2;
            const int columnInfo = 3;

            return File.ReadAllLines("AlgoSeekFuturesConverter/AlgoSeek.US.Futures.PriceMultipliers.1.1.csv")
                    .Select(line => line.ToCsvData())
                    // skipping empty fields
                    .Where(line => !string.IsNullOrEmpty(line[columnUnderlying]) &&
                                   !string.IsNullOrEmpty(line[columnMultipleFactor]))
                    // skipping header
                    .Skip(1)
                    .ToDictionary(line => line[columnUnderlying],
                                  line => System.Convert.ToDecimal(line[columnMultipleFactor]));
        }

        private void Flush(Processors processors, DateTime time, bool final)
        {
            foreach (var symbol in processors.Keys)
            {
                processors[symbol].ForEach(p => p.ForEach(x => x.FlushBuffer(time, final)));
            }
        }

        /// <summary>
        /// Compress the queue buffers directly to a zip file. Lightening fast as streaming ram-> compressed zip.
        /// </summary>
        public void Package(DateTime date)
        {
            var zipper = OS.IsWindows ? "C:/Program Files/7-Zip/7z.exe" : "7z";

            Log.Trace("AlgoSeekFuturesConverter.Package(): Zipping all files ...");

            var destination = Path.Combine(_destination, "future");
            Directory.CreateDirectory(destination);
            var dateMask = date.ToString(DateFormat.EightCharacter);

            var files =
                Directory.EnumerateFiles(destination, dateMask + "*.csv", SearchOption.AllDirectories)
                .GroupBy(x => Directory.GetParent(x).FullName)
                .ToList();

            // Zip each file massively in parallel
            Parallel.ForEach(files, file =>
                //foreach (var file in files)
            {
                try
                {
                    var outputFileName = file.Key + ".zip";

                    // Create and open a new ZIP file
                    var filesToCompress = Directory.GetFiles(file.Key, "*.csv", SearchOption.AllDirectories);
                    var zip = ZipFile.Open(outputFileName, ZipArchiveMode.Create);

                    foreach (var fileToCompress in filesToCompress)
                    {
                        // Add the entry for each file
                        zip.CreateEntryFromFile(fileToCompress, Path.GetFileName(fileToCompress), CompressionLevel.Optimal);
                    }

                    // Dispose of the object when we are done
                    zip.Dispose();

                    try
                    {
                        Directory.Delete(file.Key, true);
                    }
                    catch (Exception err)
                    {
                        Log.Error("Directory.Delete returned error: " + err.Message);
                    }
                }
                catch (Exception err)
                {
                    Log.Error("File: {0} Err: {1} Source {2} Stack {3}", file, err.Message, err.Source, err.StackTrace);
                }
            });
        }

    }
}
