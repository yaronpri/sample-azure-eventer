using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;

namespace Samples.Azure.Eventer.FileGenerator
{
    class Program
    {
        private static string SAMPLE_FILE = "SampleSourceFile.xml";

        static async Task Main(string[] args)
        {
            try
            {
                /*
                var cl = new BlobContainerClient("DefaultEndpointsProtocol=https;AccountName=yaronincomingfiles;AccountKey=/Sd4fZcvvdoTcKUZHgLlGyE6hLqW2qVZDBTa8vtdJmJ0Nl5Ri70WxLanA26Xyxo5SxxQp2kjPbasGYBNgYu2tw==;EndpointSuffix=core.windows.net",
                       "wav");
                var blobClient = cl.GetBlobClient("demofile-6e82acdb-1882-4ded-b8a1-8f5dee538650.xml");
                MemoryStream mem = new MemoryStream();
                blobClient.DownloadTo(mem);                                                               
                byte[] content = mem.ToArray();                              
                string xml = Encoding.UTF8.GetString(content);
                var doc = new XmlDocument();
                doc.LoadXml(xml);


                //var c = doc.GetElementById("File");

                var b = doc.ChildNodes[1]["Data"].InnerText;
                byte[] tempBytes = Convert.FromBase64String(b);
                //File.WriteAllBytes(@"new.wav", tempBytes);

                var upClient = cl.GetBlobClient("new.wav");
                await upClient.UploadAsync(new MemoryStream(tempBytes));


                //XmlReader rea = XmlReader.Create(content);
                //rea.ReadToFollowing("Name");
                //var b2 = rea.ReadElementContentAsString();

                //XmlReader reader = XmlReader.Create(SAMPLE_FILE);


                //reader.ReadToFollowing("Data");
                //var b = reader.ReadElementContentAsString();

                //var b1 = File.ReadAllText("testbase64.txt");
                //byte[] tempBytes = Convert.FromBase64String(b);
                //File.WriteAllBytes(@"new.wav", tempBytes);
                */
                Console.WriteLine("Let's send some orders, how many files per second you want to upload ?");
                var requestedAmount = DetermineOrderAmount();
                Console.WriteLine("For how many seconds you would like to send it?");
                var requestedSeconds = DetermineSecondAmount();

                await SendFiles(requestedAmount, requestedSeconds);

                Console.WriteLine("That's it, see you later!");
            }
            catch(Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
        }

        private static List<string> GenerateFileNames(int count)
        {
            List<string> retVal = new List<string>();

            for(int i = 0; i < count; i++)
            {
                retVal.Add("demofile-" + Guid.NewGuid().ToString() + ".xml");
            }

            return retVal;
        }        

        private static async Task SendFiles(int requestedAmount, int requestedSeconds)
        {
            Console.WriteLine("Uploading files...");            

            var env = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Development";
            IConfiguration Config = new ConfigurationBuilder()
                .AddJsonFile($"appsettings.{env}.json")
                .Build();

            var connectionString = Config.GetSection("BLOB_CONNECTIONSTRING").Value;
            var containerName = Config.GetSection("BLOB_COTAINERNAME").Value;

            BlobServiceClient blobServiceClient = new BlobServiceClient(connectionString);
            BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(containerName);


            int numOfFiles = requestedAmount * requestedSeconds;
            var generatedNames = GenerateFileNames(numOfFiles);          
            
            DateTime before = DateTime.Now;


            for (int i = 0; i < requestedSeconds; i++)
            {                    
                List<Task> tasks = new List<Task>();
                for (int j = 0; j < requestedAmount; j++)
                {
                    var index = (i * requestedAmount) + j;
                    var filename = generatedNames[index];
                    BlobClient blobClient = containerClient.GetBlobClient(filename);
                    Console.WriteLine("Second {0} id {1} - operation num. {2} - uploading - {3}", i+1, j+1, index + 1, blobClient.Uri);
                    tasks.Add(blobClient.UploadAsync(SAMPLE_FILE, true));
                }
                //Task.WaitAll(tasks.ToArray());
                await Task.WhenAll(tasks);

                var after = DateTime.Now.Subtract(before);

                //add the time need to wait for 1 second 
                if (1000 - after.Milliseconds > 0)
                    await Task.Delay(TimeSpan.FromMilliseconds(1000 - after.Milliseconds));                
            }            

            Console.WriteLine("Finished uploading images");
        }        

        private static int DetermineOrderAmount()
        {
            var rawAmount = Console.ReadLine();
            if (int.TryParse(rawAmount, out int amount))
            {
                return amount;
            }

            Console.WriteLine("That's not a valid amount, let's try that again");
            return DetermineOrderAmount();
        }

        private static int DetermineSecondAmount()
        {
            var rawAmount = Console.ReadLine();
            if (int.TryParse(rawAmount, out int amount))
            {
                return amount;
            }

            Console.WriteLine("That's not a valid seconds, let's try that again");
            return DetermineOrderAmount();
        }
    }
}
