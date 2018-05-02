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

using System.IO;
using System.Linq;
using System.Text;
using Ionic.Zip;
using NUnit.Framework;

namespace QuantConnect.Tests.Compression
{
    [TestFixture]
    public class CompressionTests
    {
        [Test]
        public void ReadLinesCountMatchesLineCount()
        {
            const string file = "../../../Data/equity/usa/minute/spy/20131008_trade.zip";

            const int expected = 827;
            int actual = QuantConnect.Compression.ReadLines(file).Count();

            Assert.AreEqual(expected, actual);
        }

        [Test]
        public void ZipBytes()
        {
            const string fileContents = "this is the contents of a file!";
            var fileBytes = Encoding.ASCII.GetBytes(fileContents); // using asci because UnzipData uses 1byte=1char
            var zippedBytes = QuantConnect.Compression.ZipBytes(fileBytes, "entry");
            File.WriteAllBytes("entry.zip", zippedBytes);

            using (var streamReader = QuantConnect.Compression.UnzipStreamToStreamReader(File.OpenRead("entry.zip")))
            {
                var contents = streamReader.ReadToEnd();
                Assert.AreEqual(fileContents, contents);
            }
        }

        [Test]
        public void ExtractsZipEntryByName()
        {
            var zip = Path.Combine("TestData", "multizip.zip");
            ZipFile zipFile;
            using (var entryStream = QuantConnect.Compression.Unzip(zip, "multizip/two.txt", out zipFile))
            using (zipFile)
            {
                var text = entryStream.ReadToEnd();
                Assert.AreEqual("2", text);
            }
        }

        [Test]
        public void ReadsZipEntryFileNames()
        {
            var zipFileName = Path.Combine("TestData", "20151224_quote_american.zip");
            var entryFileNames = QuantConnect.Compression.GetZipEntryFileNames(zipFileName).ToList();

            var expectedFileNames = new[]
            {
                "20151224_xlre_tick_quote_american_call_210000_20160819.csv",
                "20151224_xlre_tick_quote_american_call_220000_20160819.csv",
                "20151224_xlre_tick_quote_american_put_370000_20160819.csv"
            };

            Assert.AreEqual(expectedFileNames.Length, entryFileNames.Count);

            for (var i = 0; i < entryFileNames.Count; i++)
            {
                Assert.AreEqual(expectedFileNames[i], entryFileNames[i]);
            }
        }
    }
}
