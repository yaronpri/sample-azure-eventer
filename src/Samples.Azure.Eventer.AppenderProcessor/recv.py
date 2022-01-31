import sys, time, logging
import asyncio, os, uuid
import xml.etree.ElementTree as ET
import json
from datetime import datetime
from asyncio.events import new_event_loop
from azure.eventhub.aio import EventHubConsumerClient
from azure.eventhub.extensions.checkpointstoreblobaio import BlobCheckpointStore
from azure.storage.blob.aio import BlobServiceClient, BlobClient, ContainerClient
from opencensus.ext.azure import metrics_exporter
from opencensus.ext.azure.log_exporter import AzureLogHandler
from opencensus.ext.azure.trace_exporter import AzureExporter
from opencensus.trace.samplers import ProbabilitySampler
from opencensus.trace.tracer import Tracer
from opencensus.trace import config_integration

logger = logging.getLogger(__name__)
logger.setLevel(logging.INFO)   

app_insights_instrumentationkey = os.environ["APPINSIGHTS_INSTRUMENTATIONKEY"]

logger.addHandler(logging.StreamHandler(sys.stdout))

if app_insights_instrumentationkey:
    config_integration.trace_integrations(['requests']) 
    
    logger.addHandler(AzureLogHandler(
        connection_string="InstrumentationKey=" + app_insights_instrumentationkey)
    )
    
    exporter = metrics_exporter.new_metrics_exporter(
        connection_string="InstrumentationKey=" + app_insights_instrumentationkey)

    tracer = Tracer(
        exporter=AzureExporter(
            connection_string="InstrumentationKey=" + app_insights_instrumentationkey),
        sampler=ProbabilitySampler(1.0)
    )

#blob source details
blob_service_connection = os.environ["BLOB_STORE_CONNECTIONSTRING"]
blob_service_client = BlobServiceClient.from_connection_string(blob_service_connection)
preprocess_container_name = os.environ["BLOB_STORE_CONTAINERNAME"]
container_uri = blob_service_client.primary_endpoint + preprocess_container_name

#blob result details
blob_result_service_connection = os.environ["BLOB_STORE_CONNECTIONSTRING_RESULT"]
blob_result_service_client = BlobServiceClient.from_connection_string(blob_result_service_connection)
result_container_name = os.environ["BLOB_STORE_CONTAINERNAME_RESULT"]

#checkpoint blob details
checkpoint_store_connection = os.environ["BLOB_CHECKPOINT_CONNECTIONSTRING"]
checkpoint_store_container = os.environ["BLOB_CHECKPOINT_CONTAINERNAME"]

#EventHub details
event_hub_connection = os.environ["EVENTHUB_CONNECTIONSTRING"]
event_hub_name = os.environ["EVENTHUB_NAME"]
consumer_group_name = os.environ["EVENTHUB_CONSUMERGROUP"]

async def on_event(partition_context, event):
    try:
        with tracer.span(name="newappenderrquest") as span:
            span.parent_span
            start = time.time()
            new_event_json = json.loads(event.body_as_str(encoding='UTF-8'))

            full_blob_name = new_event_json[0]["data"]["url"]
            start_substring = full_blob_name.index(container_uri) + len(container_uri) + 1
            blob_name = full_blob_name[start_substring:len(full_blob_name)]  
            
            # Getting the file uid
            fileuid = blob_name.replace("demofile-", "")
            fileuid = fileuid.replace(".xml", "")
            fileuid = fileuid.replace("-", "")

            properties = {'custom_dimensions': {'fileuid': fileuid }}
            logger.info("Receive Appender event " + blob_name + " " + datetime.now().strftime("%m/%d/%Y, %H:%M:%S"), extra=properties)
   
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

            properties = {'custom_dimensions': {'fileuid': fileuid}}
            logger.info("End Appender event " + blob_name + " " + datetime.now().strftime("%m/%d/%Y, %H:%M:%S") + " took (sec) " + str(end-start), extra=properties)
    except Exception as e:
        logger.error("Error - ", e.args[0])

    # Print the event data.
    #print("Received the event: \"{}\" from the partition with ID: \"{}\"".format(event.body_as_str(encoding='UTF-8'), partition_context.partition_id))

async def main():
    # Create an Azure blob checkpoint store to store the checkpoints.
    checkpoint_store = BlobCheckpointStore.from_connection_string(checkpoint_store_connection, checkpoint_store_container)
    
    # Create a consumer client for the event hub.
    client = EventHubConsumerClient.from_connection_string(event_hub_connection, consumer_group=consumer_group_name, eventhub_name=event_hub_name, checkpoint_store=checkpoint_store)
    async with client:
        # Call the receive method. Read from the beginning of the partition (starting_position: "-1")
        logger.info("Start Appender listening")
        await client.receive(on_event=on_event,  starting_position="-1")

if __name__ == '__main__':
    loop = asyncio.get_event_loop()
    # Run the main method.
    loop.run_until_complete(main())