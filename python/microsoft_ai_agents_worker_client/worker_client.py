import asyncio
import logging

import os
import grpc
import json
import service_pb2
import service_pb2_grpc

from google.protobuf.json_format import MessageToJson
from service_pb2_grpc import AgentRpcStub
from service_pb2 import Message
from service_pb2 import RegisterAgentType
from service_pb2 import RpcRequest


async def run(connection_string: str) -> None:
    logger = logging.getLogger("AgentWorker")
    json_config = json.dumps(
        {
            "methodConfig": [
                {
                    "name": [{}],
                    "retryPolicy": {
                        "maxAttempts": 3,
                        "initialBackoff": "0.01s",
                        "maxBackoff": "5s",
                        "backoffMultiplier": 2,
                        "retryableStatusCodes": ["UNAVAILABLE"],
                    },
                }
            ],
        }
    )
    logger.info("Connecting to '%s'...", connection_string)
    async with grpc.aio.insecure_channel(
        connection_string, options=(("grpc.service_config", json_config),)
    ) as channel:
        logger.info("Connected.")
        stub = service_pb2_grpc.AgentRpcStub(channel)

        # # Read from an async generator
        # async for response in stub.OpenChannel():
        #     logger.info(
        #         "Greeter client received from async generator: "
        #         + response.message
        #     )

        message_stream = stub.OpenChannel()
        try:
            while True:
                registerType = service_pb2.Message(
                    registerAgentType=service_pb2.RegisterAgentType(type="py_greeter")
                )
                logger.info("Sending: %s", MessageToJson(registerType))
                await message_stream.write(registerType)
                logger.info("Sent. Now reading...")
                message = await message_stream.read()
                logger.info("Got message: %s", MessageToJson(message))
                if message == grpc.aio.EOF:
                    logger.info("EOF")
                    break
        except Exception as e:
            logger.error("Error: %s", e)

        logger.info("Exiting...")

if __name__ == "__main__":
    logging.basicConfig(level=logging.INFO)
    logger = logging.getLogger("Main")
    logger.info("Starting...")
    asyncio.run(run(os.environ["AGENT_HOST"]))
    logger.info("Done?")
