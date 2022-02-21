# samples-azure-eventer

This is a sample solution which demonstrate how to build event-based solution running on AKS with KEDA.

The scenario start by uploading many new files to Azure Blob Storage using 'ServiceGenerator' service, which trigger an EventGrid flow listenning to 'BlobCreated' event and send the message to Azure EventHub.
There is 2nd Service - 'AppenderService' which listen to EventHub message written in Python, read the files from blob storage, do some manipuliation overthe file and upload it again to Azure Storage.

We will use also AKS with KEDA for auto-scalling pod.

Sample solution sketch:

![image](https://user-images.githubusercontent.com/89332819/154981548-e7955cb9-86b0-482d-a001-662b8ef820bc.png)


