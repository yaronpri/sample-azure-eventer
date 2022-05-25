    using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.Storage.Queues;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace Samples.Azure.Eventer.FileGenerator
{
    public class GeneratorDetails
    {        
        public int RequestedAmount { get; set; }
        public int RequestedSeconds { get; set; }
        public bool IsReadFromMemory { get; set; }
        public int SendFileMode { get; set; }
    }

    public class MyTelemetryInitializer : ITelemetryInitializer
    {
        public void Initialize(ITelemetry telemetry)
        {
            if (string.IsNullOrEmpty(telemetry.Context.Cloud.RoleName))
            {
                //set custom role name here
                telemetry.Context.Cloud.RoleName = "FileGenerator";
                telemetry.Context.Cloud.RoleInstance = Environment.MachineName;
            }
        }
    }


    class Program
    {
        //private static string SAMPLE_FILE = "SampleSourceFile.xml";
        private static IConfiguration Config;
        public const string SOURCE_NAMESPACE = "Samples.Azure.Eventer.FileGenerator";
        private static readonly ActivitySource MyActivitySource = new ActivitySource(Program.SOURCE_NAMESPACE);

        static async Task Main(string[] args)
        {            
            try
            {                
                var env = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Development";
                Config = new ConfigurationBuilder()
                    .AddJsonFile($"appsettings.{env}.json")
                    .Build();

                Console.WriteLine("What is the queue name ?");
                var qName = Console.ReadLine();
                Console.WriteLine("Let's send some files,  how to read sample file, from memory (1) from disk (2) ?");
                var isReadFromMemory = DetermineInMemory();
                Console.WriteLine("Do you want to send 1MB (1) or 3MB (2) or differet 1MB files (3) ?");
                var sendFileMode = DetermineSendFileMode(isReadFromMemory);
                Console.WriteLine("how many parallel processes you want ?");
                var requestedProcesses = DetermineProcessesAmount();
                Console.WriteLine("how many files per second you want to upload ?");
                var requestedAmount = DetermineOrderAmount();
                Console.WriteLine("For how many seconds you would like to send it?");
                var requestedSeconds = DetermineSecondAmount();

                IServiceCollection services = new ServiceCollection();
                services.AddLogging(loggingBuilder =>
                {
                    loggingBuilder.AddFilter<Microsoft.Extensions.Logging.ApplicationInsights.ApplicationInsightsLoggerProvider>("Samples.Azure.Eventer.FileGenerator.Program", LogLevel.Information);
                    loggingBuilder.AddConsole();
                });

                var appinsights_key = Config.GetSection("APPINSIGHTS_INSTRUMENTATIONKEY").Value ?? string.Empty;

                ILogger<Program> logger;
                if (string.IsNullOrEmpty(appinsights_key) == false)
                {
                    services.AddApplicationInsightsTelemetryWorkerService(appinsights_key);
                    services.AddSingleton<ITelemetryInitializer, MyTelemetryInitializer>();
                    IServiceProvider serviceProvider = services.BuildServiceProvider();
                    var tc = serviceProvider.GetRequiredService<TelemetryClient>();
                    logger = serviceProvider.GetRequiredService<ILogger<Program>>();
                    await SendEventToQueue(sendFileMode, isReadFromMemory, requestedProcesses, requestedAmount, requestedSeconds, qName, tc, logger);
                    tc.Flush();
                }
                else
                {
                    IServiceProvider serviceProvider = services.BuildServiceProvider();
                    logger = serviceProvider.GetRequiredService<ILogger<Program>>();
                    await SendEventToQueue(sendFileMode, isReadFromMemory, requestedProcesses, requestedAmount, requestedSeconds, qName, logger);
                }
  
                //Test ONLY
                //await ReadEventFromQueue();
                //await SendFiles(requestedAmount, requestedSeconds);

                logger.LogInformation("That's it, see you later!");                
                await Task.Delay(500);
            }
            catch(Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
        }

        private static async Task SendEventToQueue(int sendFileMode, bool isReadFromMemory, int requestedProcesses, int requestedAmount, int requestedSeconds, string queueName,
            ILogger logger)
        {
            string connectionString = Config.GetSection("BLOB_CONNECTIONSTRING").Value;
            //var queueName = Config.GetSection("QUEUE_NAME").Value;
         
            QueueClient queueClient = new QueueClient(connectionString, queueName);
      
            var request = new GeneratorDetails() { SendFileMode = sendFileMode, IsReadFromMemory = isReadFromMemory,  RequestedAmount = requestedAmount, RequestedSeconds = requestedSeconds };
            var jsonReq = JsonSerializer.Serialize<GeneratorDetails>(request);

            for (int i=0; i < requestedProcesses; i++)
            {                
                await queueClient.SendMessageAsync(jsonReq);
                logger.LogInformation("Send queue request " + jsonReq);                
            }                                     
        }

        private static async Task SendEventToQueue(int sendFileMode, bool isReadFromMemory, int requestedProcesses, int requestedAmount, int requestedSeconds, string queueName,
            TelemetryClient tc, ILogger logger)
        {
            string connectionString = Config.GetSection("BLOB_CONNECTIONSTRING").Value;
            //var queueName = Config.GetSection("QUEUE_NAME").Value;

            QueueClient queueClient = new QueueClient(connectionString, queueName);

            var request = new GeneratorDetails() { SendFileMode = sendFileMode, IsReadFromMemory = isReadFromMemory, RequestedAmount = requestedAmount, RequestedSeconds = requestedSeconds };
            var jsonReq = JsonSerializer.Serialize<GeneratorDetails>(request);

            for (int i = 0; i < requestedProcesses; i++)
            {
                using (var operation = tc.StartOperation<RequestTelemetry>("send-queue-request" + i))
                {
                    await queueClient.SendMessageAsync(jsonReq);
                    logger.LogInformation("Send queue request " + jsonReq);
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

        private static bool DetermineInMemory()
        {
            var rawAmount = Console.ReadLine();
            if (int.TryParse(rawAmount, out int amount))
            {
                if (amount == 1)
                {
                    return true;
                }
                else if (amount == 2)
                {
                    return false;
                }
            }

            Console.WriteLine("That's not a valid amount (1 - inmem / 2 - from disk), let's try that again");
            return DetermineInMemory();
        }

        private static int DetermineSendFileMode(bool isInMemory)
        {
            var rawAmount = Console.ReadLine();
            if (int.TryParse(rawAmount, out int amount))
            {
                if ((isInMemory) && (amount == 3))
                {
                    Console.WriteLine("That's not a valid combination InMemory and List of files, let's try that again");
                }
                else
                {
                    if ((amount >= 1) && (amount <= 3))
                    {
                        return amount;
                    }
                }
            }

            Console.WriteLine("That's not a valid amount (1 - 1MB file / 2 - 3MB file / 3 - list of files), let's try that again");
            return DetermineSendFileMode(isInMemory);
        }

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
            return DetermineProcessesAmount();
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
            return DetermineSecondAmount();
        }
    }
}
