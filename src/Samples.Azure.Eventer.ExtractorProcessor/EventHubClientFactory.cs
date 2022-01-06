using Azure.Identity;
using Azure.Messaging.EventHubs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using static Samples.Azure.Eventer.ExtractorProcessor.BlobClientFactory;

namespace Samples.Azure.Eventer.ExtractorProcessor
{
    public static class EventHubClientFactory
    {
        public static EventProcessorClient CreateWithManagedIdentityAuthentication(IConfiguration configuration, ILogger logger)
        {
            var hostname = configuration.GetValue<string>("EVENTHUB_HOST_NAME");
            var consumerGroup = configuration.GetValue<string>("EVENTHUB_CONSUMERGROUP");
            var eventHubName = configuration.GetValue<string>("EVENTHUB_NAME");         
            var clientIdentityId = configuration.GetValue<string>("KEDA_EVENTHUB_IDENTITY_USERASSIGNEDID", defaultValue: null);

            if (string.IsNullOrWhiteSpace(clientIdentityId) == false)
            {
                logger.LogInformation("Using user-assigned identity with ID {UserAssignedIdentityId}", clientIdentityId);
            }

            return new EventProcessorClient(null, consumerGroup, hostname, eventHubName, new ManagedIdentityCredential(clientId: clientIdentityId));
        }

        public static EventProcessorClient CreateWithServicePrincipleAuthentication(IConfiguration configuration)
        {          
            var tenantId = configuration.GetValue<string>("GLOBAL_TENANT_ID");
            var appIdentityId = configuration.GetValue<string>("GLOBAL_IDENTITY_APPID");
            var appIdentitySecret = configuration.GetValue<string>("GLOBAL_IDENTITY_SECRET");
            var hostname = configuration.GetValue<string>("EVENTHUB_HOST_NAME");
            var consumerGroup = configuration.GetValue<string>("EVENTHUB_CONSUMERGROUP");
            var eventHubName = configuration.GetValue<string>("EVENTHUB_NAME");
            var blobContainerClient = BlobClientFactory.CreateWithServicePrincipleAuthentication(configuration, eBlobPurpose.Checkpoint);

            return new EventProcessorClient(blobContainerClient, consumerGroup, hostname, eventHubName, new ClientSecretCredential(tenantId, appIdentityId, appIdentitySecret));
        }

        public static EventProcessorClient CreateWithConnectionStringAuthentication(IConfiguration configuration, ILogger logger)
        {
            var connectionString = configuration.GetValue<string>("EVENTHUB_CONNECTIONSTRING");
            var eventHubName = configuration.GetValue<string>("EVENTHUB_NAME");
            var consumerGroup = configuration.GetValue<string>("EVENTHUB_CONSUMERGROUP");
            var blobContainerClient = BlobClientFactory.CreateWithConnectionStringAuthentication(configuration, eBlobPurpose.Checkpoint);

            logger.LogInformation("Creating EventHubProcessor using ConnectionString {name} {group}", eventHubName, consumerGroup);

            return new EventProcessorClient(blobContainerClient, consumerGroup, connectionString, eventHubName);
        }
    }
}
