using Microsoft.Azure;
using System.Configuration;
using System.IO;
using System;
using System.Linq;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure;
using System.Globalization;
using System.Collections;
using System.Threading;
using System.Collections.Generic;

namespace updateTool
{
    class Program
    {
        static void Main(string[] args)
        {
            //connect Azure blob,get container
            string[] prepros = { "MS-ASAIRS", "MS-ASCAL", "MS-ASCMD", "MS-ASCNTC", "MS-ASCON", "MS-ASDOC", "MS-ASEMAIL", "MS-ASHTTP", "MS-ASNOTE", "MS-ASPROV", "MS-ASRM", "MS-ASTASK" };
            string sasToken = "maybe you need to copy the sastoken";
            string testSuites = ConfigurationManager.AppSettings["testSuites"];
            string summaryBlobName = ConfigurationManager.AppSettings["summaryBlobName"];
            string connectionString = ConfigurationManager.AppSettings["StorageConnectionString"]; //blob connection string
            string sourceContainerName = ConfigurationManager.AppSettings["sourcecontainerName"]; //source blob container name            
            var containerClient = new BlobContainerClient(connectionString, sourceContainerName);
            
            
            //write the head of summary.csv
            string localfilePath = ConfigurationManager.AppSettings["localfilePath"];
            FileStream fileStream = new FileStream(localfilePath, FileMode.Append);
            StreamWriter streamWriter = new StreamWriter(fileStream, System.Text.Encoding.UTF8);
            string dataHead = string.Empty;
            dataHead = "\"date\"," + "\"protocolName\"," + "\"datailResult\"," + "\"total\"," + "\"executed\"," + "\"passed\"," + "\"failed\"," + "\"notExecuted\"," + "\"inexecuted\"";
            streamWriter.WriteLine(dataHead);
            streamWriter.Flush();
            streamWriter.Close();
            fileStream.Close();

            //read csv and write summary.csv
            ListBlobsHierarchicalListing(containerClient, testSuites + "/", 1, prepros, sasToken);

            ///<summary>
            ///upload csv上传summary.csv文件
            ///</summary>
            using (var filestream = System.IO.File.OpenRead(localfilePath))
            {
                containerClient.GetBlobClient(summaryBlobName).Upload(filestream);
            }
            Console.WriteLine("update Complete!");
            Console.ReadKey();
        }

        ///<summary>
        ///read csv and write summary.csv
        /// </summary>
        public static void ListBlobsHierarchicalListing(BlobContainerClient container, string prefix, int segmentSize, string[] prepros, string sasToken)
        {
            string continuationToken = null;

            try
            {
                // Call the listing operation and enumerate the result segment.
                // When the continuation token is empty, the last segment has been returned and
                // execution can exit the loop.
                do
                {
                    var resultSegment = container.GetBlobsByHierarchy(prefix: prefix, delimiter: "/").AsPages(continuationToken, segmentSize);

                    foreach (Azure.Page<BlobHierarchyItem> blobPage in resultSegment)
                    {
                        // A hierarchical listing may return both virtual directories and blobs.
                        foreach (BlobHierarchyItem blobhierarchyItem in blobPage.Values)
                        {
                            if (blobhierarchyItem.IsPrefix)
                            {
                                // Write out the prefix of the virtual directory.
                                Console.WriteLine("Virtual directory prefix: {0}", blobhierarchyItem.Prefix);
                                //get the latest csv file for each protocol
                                foreach (string prepro in prepros)
                                {
                                    ListBlobsFlatListing(container, 1, blobhierarchyItem.Prefix, prepro, sasToken);
                                }
                            }
                        }

                        Console.WriteLine();

                        // Get the continuation token and loop until it is empty.
                        continuationToken = blobPage.ContinuationToken;
                    }
                } while (continuationToken != "");
            }
            catch (RequestFailedException e)
            {
                Console.WriteLine(e.Message);
                Console.ReadLine();
                throw;
            }
        }

        public static void ListBlobsFlatListing(BlobContainerClient container, int segmentSize, string prefix, string prepro, string sasToken)
        {
            string continuationToken = null;

            try
            {
                // Call the listing operation and enumerate the result segment.
                // When the continuation token is empty, the last segment has been returned
                // and execution can exit the loop.
                do
                {
                    ///<summary>
                    ///获取到要读的csv文件的name，即为result
                    /// </summary>
                    var resultSegment1 = container.GetBlobs(prefix: prefix + prepro).AsPages(continuationToken, segmentSize);
                    string result = null;
                    ///<summary>
                    ///计算出最新的csv文件
                    /// </summary>
                    /// 

                    foreach (Azure.Page<BlobItem> blobPage in resultSegment1)
                    {
                        foreach (BlobItem blobItem in blobPage.Values)
                        {
                            result = blobItem.Name;

                            if (blobItem.Name.CompareTo(result) == 1)
                            {
                                result = blobItem.Name;
                            }

                        }
                        // Get the continuation token and loop until it is empty.
                        continuationToken = blobPage.ContinuationToken;
                    }
                    if (result != null)
                    {
                        Console.WriteLine(result);
                    }                   

                    ///<summary>
                    ///read csv,从Azure Blob上读取指定的csv文件，转换为string,即为text
                    /// </summary>
                    string text;
                    if (result != null)
                    {
                        using (var memoryStream = new MemoryStream())
                        {

                            container.GetBlobClient(result).DownloadTo(memoryStream);

                            //puts the byte arrays to a string
                            text = System.Text.Encoding.UTF8.GetString(memoryStream.ToArray());

                        }
                        ///<summary>
                        ///计算从Azure Blob上读取的csv文件passed,failed,,的数量
                        /// </summary>
                        int passedCount = 0;
                        int failCount = 0;
                        int notExecutedCount = 0;
                        int inConclusiveCount = 0;
                        using (StringReader stringReader = new StringReader(text))
                        {
                            while (stringReader.Peek() >= 0)
                            {
                                string line = stringReader.ReadLine();
                                //Console.WriteLine(line);

                                string[] values = line.Split(',');
                                for (int i = 0; i < values.Length; i++)
                                {
                                    if (i == 2 && values[2] == "\"Passed\"")
                                    {
                                        passedCount++;
                                    }
                                    else if (i == 2 && values[2] == "\"Failed\"")
                                    {
                                        failCount++;
                                    }
                                    else if (i == 2 && values[2] == "\"NotExecuted\"")
                                    {
                                        notExecutedCount++;
                                    }
                                    else if (i == 2 && values[2] == "\"InConclusive\"")
                                    {
                                        inConclusiveCount++;
                                    }
                                }
                            }
                        }
                        ///<summary>
                        ///将获取到的csv文件的string写入到本地一个新的csv文件中，若没有，则创建
                        /// </summary>
                        string localfilePath = ConfigurationManager.AppSettings["localfilePath"];

                        FileStream fileStream = new FileStream(localfilePath, FileMode.Append);
                        StreamWriter streamWriter = new StreamWriter(fileStream, System.Text.Encoding.UTF8);
                        //csv文件中的内容
                        string testSuites = ConfigurationManager.AppSettings["testSuites"];
                        string date = prefix.Replace(testSuites+"/", "\"").Replace("/", "\",");
                        string protocolName = prepro;
                        string datailResult = container.Uri.ToString() + "/" + result + "?" + sasToken;


                        int passed = passedCount;
                        int failed = failCount;
                        int notExecuted = notExecutedCount;
                        int inexecuted = inConclusiveCount;
                        int executed = passed + failed;
                        int total = executed + notExecuted + inexecuted;

                        string summary = date + "\"" + protocolName + "\"," + "\"" + datailResult + "\"," + "\"" + total + "\"," + "\"" + executed + "\"," + "\"" + passed + "\"," + "\"" + failed + "\"," + "\"" + notExecuted + "\"," + "\"" + inexecuted;
                        Console.WriteLine(summary);
                        streamWriter.WriteLine(summary);

                        streamWriter.Flush();
                        streamWriter.Close();
                        fileStream.Close();
                               
                    }
                } while (continuationToken != "");

            }
            catch (RequestFailedException e)
            {
                Console.WriteLine(e.Message);
                Console.ReadLine();
                throw;
            }
        }
    }
}
