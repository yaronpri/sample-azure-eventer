import sys, time
import asyncio, os, uuid
import xml.etree.ElementTree as ET
import json
from datetime import datetime
from asyncio.events import new_event_loop
from azure.eventhub.aio import EventHubConsumerClient
from azure.eventhub.extensions.checkpointstoreblobaio import BlobCheckpointStore
from azure.storage.blob.aio import BlobServiceClient, BlobClient, ContainerClient

#blob source details
blob_service_connection = '<SOURCE BLOB STORAGE CONNECTION STRING>'
blob_service_client = BlobServiceClient.from_connection_string(blob_service_connection)
preprocess_container_name = "<SOURCE CONTAINER NAME>"
container_uri = blob_service_client.primary_endpoint + preprocess_container_name

#blob result details
blob_result_service_connection = '<RESULT BLOB STORAGE CONNECTION STRING>'
blob_result_service_client = BlobServiceClient.from_connection_string(blob_result_service_connection)
result_container_name = "<RESULT CONTAINER NAME>"

#checkpoint blob details
checkpoint_store_connection = '<CHECKPOOINT CONNECTION STRING>'
checkpoint_store_container = "<CHECKPOINT CONTAINER NAME>"

#EventHub details
event_hub_connection = '<EVENTHUB NAMESPACE CONNECTION STRING>'
event_hub_name = "<EVENTHUB NAME>"
consumer_group_name = '<CONSUMER GROUP>'

async def on_event(partition_context, event):
    try:
        start = time.time()
        new_event_json = json.loads(event.body_as_str(encoding='UTF-8'))
        print("Receive event ", new_event_json[0]["data"]["requestId"], datetime.now())
        
        full_blob_name = new_event_json[0]["data"]["url"]
        start_substring = full_blob_name.index(container_uri) + len(container_uri) + 1
        blob_name = full_blob_name[start_substring:len(full_blob_name)]  

        blob_client = blob_service_client.get_blob_client(container=preprocess_container_name, blob=blob_name)
    
        stream = await blob_client.download_blob()
        data = await stream.readall()

        #TODO: research for async implementation
        tree = ET.fromstring(data)
        new1element = ET.fromstring("<New1>new1</New1>")
        new2element = ET.fromstring("<New2>new2</New2>")
        tree.append(new1element)
        tree.append(new2element)

        result_blob_client = blob_result_service_client.get_blob_client(container=result_container_name, blob=blob_name)
        await result_blob_client.upload_blob(ET.tostring(tree, encoding='utf8'))
        
         # Update the checkpoint so that the program doesn't read the events
        await partition_context.update_checkpoint(event)

        end = time.time()
        print("End processing event ", new_event_json[0]["data"]["requestId"], datetime.now(), "took (sec)", end - start)
    except Exception as e:
        print("Error - ", e.args[0])

    # Print the event data.
    #print("Received the event: \"{}\" from the partition with ID: \"{}\"".format(event.body_as_str(encoding='UTF-8'), partition_context.partition_id))

async def main():
    # Create an Azure blob checkpoint store to store the checkpoints.
    checkpoint_store = BlobCheckpointStore.from_connection_string(checkpoint_store_connection, checkpoint_store_container)
    
    # Create a consumer client for the event hub.
    client = EventHubConsumerClient.from_connection_string(event_hub_connection, consumer_group=consumer_group_name, eventhub_name=event_hub_name, checkpoint_store=checkpoint_store)
    async with client:
        # Call the receive method. Read from the beginning of the partition (starting_position: "-1")
        print("Start events listening")
        await client.receive(on_event=on_event,  starting_position="-1")

if __name__ == '__main__':
    loop = asyncio.get_event_loop()
    # Run the main method.
    loop.run_until_complete(main())