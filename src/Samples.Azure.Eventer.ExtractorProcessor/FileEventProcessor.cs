using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using static Samples.Azure.Eventer.ExtractorProcessor.BlobClientFactory;

namespace Samples.Azure.Eventer.ExtractorProcessor
{
    public class FileEventProcessor : EventsWorker<JArray>
    {
        public FileEventProcessor(IConfiguration configuration, ILogger<FileEventProcessor> logger)
            : base(configuration, logger)
        {
        }

        protected override async Task ProcessEvent(JArray eventData, string messageId, IEnumerable<KeyValuePair<string, object>> userProperties, CancellationToken cancellationToken)
        {
            try
            {
                DateTime before = DateTime.Now;                

                Logger.LogInformation("ProcessEvent {MessageId} ------- at {TimeStarted}", messageId, Helper.GetTimeWithMileseconds(before));
                
                var fileUri = eventData[0]["data"]["url"].ToString();                
                var blobContainerClient = BlobClientFactory.CreateWithConnectionStringAuthentication(Configuration, eBlobPurpose.Store);
                var containerUri = blobContainerClient.Uri.ToString();
                var blobName = fileUri.Substring(fileUri.IndexOf(containerUri) + containerUri.Length + 1);
                var blobClient = blobContainerClient.GetBlobClient(blobName);

                
                //TODO: find better way to load the xml file to memory 
                MemoryStream mem = new MemoryStream();
                blobClient.DownloadTo(mem);
                byte[] content = mem.ToArray();
                string xml = Encoding.UTF8.GetString(content);
                var doc = new XmlDocument();
                doc.LoadXml(xml);

                DateTime afterLoad = DateTime.Now;

                Logger.LogInformation("ProcessEvent AfterLoadXml {MessageId} - took {TimeProcessed} ms at {TimeCompleted}", messageId, afterLoad.Subtract(before).Milliseconds, Helper.GetTimeWithMileseconds(afterLoad));

                //read the base64 data and convert it to byte[]
                var dataElement = doc.ChildNodes[1]["Data"].InnerText;
                byte[] dataElementBytes = Convert.FromBase64String(dataElement);
                DateTime afterConvert = DateTime.Now;

                Logger.LogInformation("ProcessEvent AfterConvert {MessageId} - took {TimeProcessed} ms at {TimeCompleted}", messageId, afterConvert.Subtract(afterLoad).Milliseconds, Helper.GetTimeWithMileseconds(afterConvert));

                var uploadBlobName = blobName.Replace(".xml", ".wav");
                var blobUploadContainerClient = BlobClientFactory.CreateWithConnectionStringAuthentication(Configuration, eBlobPurpose.StoreResult);
                var uploadBlobClient = blobUploadContainerClient.GetBlobClient(uploadBlobName);
                await uploadBlobClient.UploadAsync(new MemoryStream(dataElementBytes));

                //TODO: code to send event to eventhub
                

                DateTime afterTheEnd = DateTime.Now;
                Logger.LogInformation("ProcessEvent AfterTheEnd {MessageId} - took {TimeProcessed} ms", messageId, afterTheEnd.Subtract(afterLoad).Milliseconds,  
                    Helper.GetTimeWithMileseconds(afterTheEnd));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"Process Event - Unable to handle message {messageId}");
            }
        }
    }
}
