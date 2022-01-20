# Example for log exporter
import logging, asyncio
import requests

from opencensus.ext.azure.log_exporter import AzureLogHandler
from opencensus.ext.azure.trace_exporter import AzureExporter
from opencensus.trace import config_integration
from opencensus.trace.samplers import ProbabilitySampler
from opencensus.trace.tracer import Tracer
from azure.storage.blob.aio import BlobServiceClient
from opencensus.trace.samplers import AlwaysOnSampler

blob_service_connection = "<BLOB CONNECTION>"
blob_service_client = BlobServiceClient.from_connection_string(blob_service_connection)
preprocess_container_name = "test"
container_uri = blob_service_client.primary_endpoint + preprocess_container_name


config_integration.trace_integrations(['requests'])


logger = logging.getLogger(__name__)

# Callback function to append '_hello' to each log message telemetry
def callback_function(envelope):
    envelope.tags['ai.cloud.role'] = 'test python'
    

handler = AzureLogHandler(connection_string='InstrumentationKey=APP KEY')
handler.add_telemetry_processor(callback_function)

exporter = AzureExporter(
    connection_string='InstrumentationKey=APP KEY'
)
exporter.add_telemetry_processor(callback_function)

logger.addHandler(handler)


async def main(): 
  #tracer = Tracer(exporter=exporter, sampler=ProbabilitySampler(1.0))
  tracer = Tracer(exporter=exporter, sampler=AlwaysOnSampler())
  with tracer.span(name='parent'):
    result_blob_client = blob_service_client.get_blob_client(container=preprocess_container_name, blob="text.txt")
    await result_blob_client.upload_blob("tetetetetete", overwrite=True)
    logger.warning('Complete upload')
    #response = requests.get(url='https://www.wikipedia.org/wiki/Rabbit')

if __name__ == '__main__':
    loop = asyncio.get_event_loop()
    # Run the main method.
    loop.run_until_complete(main())
  