using System;
using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Samples.Azure.Eventer.ExtractorProcessor
{
    public static class BlobClientFactory
    {
        public enum eBlobPurpose
        {
            Checkpoint,
            Store,
            StoreResult
        }

        public static BlobContainerClient CreateWithManagedIdentityAuthentication(IConfiguration configuration, ILogger logger, eBlobPurpose purpose)
        {
            var containerUri = new Uri(GetContainerUri(configuration, purpose));            

            var clientIdentityId = configuration.GetValue<string>("GLOBAL_IDENTITY_USERASSIGNEDID", defaultValue: null);
            if (string.IsNullOrWhiteSpace(clientIdentityId) == false)
            {
                logger.LogInformation("Using user-assigned identity with ID {UserAssignedIdentityId}", clientIdentityId);
            }

            return new BlobContainerClient(containerUri, new ManagedIdentityCredential(clientId: clientIdentityId));
        }

        public static BlobContainerClient CreateWithServicePrincipleAuthentication(IConfiguration configuration, eBlobPurpose purpose)
        {
            var containerUri = new Uri(GetContainerUri(configuration, purpose));
            var tenantId = configuration.GetValue<string>("GLOBAL_TENANT_ID");
            var appIdentityId = configuration.GetValue<string>("GLOBAL_IDENTITY_APPID");
            var appIdentitySecret = configuration.GetValue<string>("GLOBAL_IDENTITY_SECRET");
            
            return new BlobContainerClient(containerUri, new ClientSecretCredential(tenantId, appIdentityId, appIdentitySecret));
        }

        public static BlobContainerClient CreateWithConnectionStringAuthentication(IConfiguration configuration, eBlobPurpose purpose)
        {            
            var connectionString = GetConnectionString(configuration, purpose);
            var containerName = GetContainerName(configuration, purpose);

            return new BlobContainerClient(connectionString, containerName);
        }

        private static string GetConnectionString(IConfiguration configuration, eBlobPurpose purpose)
        {
            return purpose switch
            {
                eBlobPurpose.Checkpoint => configuration.GetValue<string>("BLOB_CHECKPOINT_CONNECTIONSTRING"),
                eBlobPurpose.Store => configuration.GetValue<string>("BLOB_STORE_CONNECTIONSTRING"),
                eBlobPurpose.StoreResult => configuration.GetValue<string>("BLOB_STORE_CONNECTIONSTRING_RESULT"),
                _ => configuration.GetValue<string>("BLOB_CHECKPOINT_CONNECTIONSTRING"),
            };
        }

        private static string GetContainerName(IConfiguration configuration, eBlobPurpose purpose)
        {
            return purpose switch
            {
                eBlobPurpose.Checkpoint => configuration.GetValue<string>("BLOB_CHECKPOINT_CONTAINERNAME"),
                eBlobPurpose.Store => configuration.GetValue<string>("BLOB_STORE_CONTAINERNAME"),
                eBlobPurpose.StoreResult => configuration.GetValue<string>("BLOB_STORE_CONTAINERNAME_RESULT"),
                _ => configuration.GetValue<string>("BLOB_CHECKPOINT_CONTAINERNAME"),
            };
        }

        private static string GetContainerUri(IConfiguration configuration, eBlobPurpose purpose)
        {
            return purpose switch
            {
                eBlobPurpose.Checkpoint => configuration.GetValue<string>("BLOB_CHECKPOINT_CONTAINERURI"),
                eBlobPurpose.Store => configuration.GetValue<string>("BLOB_STORE_CONTAINERURI"),
                eBlobPurpose.StoreResult => configuration.GetValue<string>("BLOB_STORE_CONTAINERURI_RESULT"),
                _ => configuration.GetValue<string>("BLOB_CHECKPOINT_CONTAINERURI"),
            };
        }
    }
}

