using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Microsoft.Extensions.Configuration;

namespace Samples.Azure.Eventer.FileGenerator
{
    public class GeneratorDetails
    {        
        public int RequestedAmount { get; set; }
        public int RequestedSeconds { get; set; }
    }


    class Program
    {
        //private static string SAMPLE_FILE = "SampleSourceFile.xml";
        private static IConfiguration Config;

        static async Task Main(string[] args)
        {            
            try
            {
                var env = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Development";
                Config = new ConfigurationBuilder()
                    .AddJsonFile($"appsettings.{env}.json")
                    .Build();

                Console.WriteLine("Let's send some files, how many parallel processes you want (1-32) ?");
                var requestedProcesses = DetermineProcessesAmount();
                Console.WriteLine("how many files per second you want to upload ?");
                var requestedAmount = DetermineOrderAmount();
                Console.WriteLine("For how many seconds you would like to send it?");
                var requestedSeconds = DetermineSecondAmount();

                await SendEventToQueue(requestedProcesses, requestedAmount, requestedSeconds);

                //Test ONLY
                //await ReadEventFromQueue();
                //await SendFiles(requestedAmount, requestedSeconds);

                Console.WriteLine("That's it, see you later!");
            }
            catch(Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
        }

        private static async Task SendEventToQueue(int requestedProcesses, int requestedAmount, int requestedSeconds)
        {
            string connectionString = Config.GetSection("BLOB_CONNECTIONSTRING").Value;
            var queueName = Config.GetSection("QUEUE_NAME").Value;
         
            QueueClient queueClient = new QueueClient(connectionString, queueName);
            queueClient.CreateIfNotExists();
            if (queueClient.Exists())
            {
                var request = new GeneratorDetails() { RequestedAmount = requestedAmount, RequestedSeconds = requestedSeconds };
                var jsonReq = JsonSerializer.Serialize<GeneratorDetails>(request);
                for (int i=0; i < requestedProcesses; i++)
                {                    
                    await queueClient.SendMessageAsync(jsonReq);
                }          
            }
        }

        /*****  TEST PURPOSES *****

        private static List<string> GenerateFileNames(int count)
        {
            List<string> retVal = new List<string>();

            for(int i = 0; i < count; i++)
            {
                retVal.Add("demofile-" + Guid.NewGuid().ToString() + ".xml");
            }

            return retVal;
        }

        private static async Task ReadEventFromQueue()
        {
            string connectionString = Config.GetSection("BLOB_CONNECTIONSTRING").Value;
            var queueName = Config.GetSection("QUEUE_NAME").Value;

            QueueClient queueClient = new QueueClient(connectionString, queueName);

            
            var msg = await queueClient.ReceiveMessageAsync(new TimeSpan(0,0, 10)); //set visibility timeout to 10 sec
            var request = msg.Value.Body.ToObjectFromJson<GeneratorDetails>();
            await queueClient.DeleteMessageAsync(msg.Value.MessageId, msg.Value.PopReceipt);

            await SendFiles(request.RequestedAmount, request.RequestedSeconds);
        }

        private static async Task SendFiles(int requestedAmount, int requestedSeconds)
        {
            Console.WriteLine("Uploading files...");                                    

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
        }*/


        private static int DetermineProcessesAmount()
        {
            var rawAmount = Console.ReadLine();
            if (int.TryParse(rawAmount, out int amount))
            {
                if ((amount >= 1) && (amount <= 32))
                {
                    return amount;
                }
            }

            Console.WriteLine("That's not a valid amount (1 - 32), let's try that again");
            return DetermineOrderAmount();
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
