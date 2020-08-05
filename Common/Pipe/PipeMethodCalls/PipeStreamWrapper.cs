using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

using NLog;

namespace Pipe.PipeMethodCalls
{
    /// <summary>
    /// Wraps the raw pipe stream with messaging and request/response capability.
    /// </summary>
    internal class PipeStreamWrapper
    {
        private static readonly UInt32 BUFSIZE = 2048; // Was 128; // Was 1024
        private readonly byte[] readBuffer = new byte[BUFSIZE];
        private readonly PipeStream stream;
        private readonly Action<string> logger;
        private static Logger nlogger_ = LogManager.GetLogger("PipeStreamWrapper");
        private static readonly JsonSerializerSettings serializerSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore
        };

        // Prevents more than one thread from writing to the pipe stream at once
        private readonly SemaphoreSlim writeLock = new SemaphoreSlim(1, 1);

        /// <summary>
        /// Initializes a new instance of the <see cref="PipeStreamWrapper"/> class.
        /// </summary>
        /// <param name="stream">The raw pipe stream to wrap.</param>
        /// <param name="logger">The action to run to log events.</param>
        public PipeStreamWrapper(PipeStream stream, Action<string> logger)
        {
            this.stream = stream;
            this.logger = logger;
        }

        /// <summary>
        /// Gets or sets the registered request handler.
        /// </summary>
        public IRequestHandler RequestHandler { get; set; }

        /// <summary>
        /// Gets or sets the registered response handler.
        /// </summary>
        public IResponseHandler ResponseHandler { get; set; }

        /// <summary>
        /// Sends a request.
        /// </summary>
        /// <param name="request">The request to send.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        public Task SendRequestAsync(PipeRequest request, CancellationToken cancellationToken)
        {
            return this.SendMessageAsync(MessageType.Request, request, cancellationToken);
        }

        /// <summary>
        /// Sends a response.
        /// </summary>
        /// <param name="response">The response to send.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        public Task SendResponseAsync(PipeResponse response, CancellationToken cancellationToken)
        {
            return this.SendMessageAsync(MessageType.Response, response, cancellationToken);
        }

        /// <summary>
        /// Sends a message.
        /// </summary>
        /// <param name="messageType">The type of message.</param>
        /// <param name="payloadObject">The massage payload object.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        private async Task SendMessageAsync(MessageType messageType, object payloadObject, CancellationToken cancellationToken)
        {
            string payloadJson = JsonConvert.SerializeObject(payloadObject, serializerSettings);
            //this.logger.Log(() => $"Sending {messageType} message" + Environment.NewLine + payloadJson);
            //nlogger_.Trace($"SendMessageAsync: Sending {messageType} message" + Environment.NewLine + payloadJson);

            // Force message size >= payload
            // If size = N * buffer length, pipe will close on last read of message data!
            byte[] payloadBytes = Encoding.UTF8.GetBytes(payloadJson);
            byte[] messageBytes;
            int payloadLength = payloadBytes.Length;
            //this.logger.Log(() => $"PIPEDBEUG: Current payload is size {payloadLength}");
            //nlogger_.Trace("SendMessageAsync: Current payload is size {0}", payloadLength);

            if (((payloadLength + 1) % BUFSIZE) == 0)
            {
                messageBytes = new byte[payloadLength + 2]; // Need an extra byte to avoid boundary size issue
                messageBytes[payloadLength + 1] = 0;        // Is this a 'null' byte?
            }
            else
            {
                messageBytes = new byte[payloadLength + 1];
            }
            messageBytes[0] = (byte)messageType;
            payloadBytes.CopyTo(messageBytes, 1);

            await this.writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                //this.logger.Log(() => $"PIPEDBEUG: Sending message of size {messageBytes.Length}");
                //nlogger_.Trace("SendMessageAsync: Sending message of size {0}", messageBytes.Length);
                //nlogger_.Trace($"SendMessageAsync()  stream.IsConnected: [{stream.IsConnected}]");
                await this.stream.WriteAsync(messageBytes, 0, messageBytes.Length, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                //this.logger.Log(() => "PIPEDBEUG: Releasing writelock...");
                //nlogger_.Trace("SendMessageAsync: Releasing write lock.");
                this.writeLock.Release();
            }
        }

        /// <summary>
        /// Processes the next message on the input stream.
        /// </summary>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        public async Task ProcessMessageAsync(CancellationToken cancellationToken)
        {
            var message = await this.ReadMessageAsync(cancellationToken).ConfigureAwait(false);
            string json = message.jsonPayload;

            switch (message.messageType)
            {
                case MessageType.Request:
                    //this.logger.Log(() => "Handling request:" + Environment.NewLine + json);
                    nlogger_.Trace($"ProcessMessageAsync: Handling request: [{json}]");
                    PipeRequest request = JsonConvert.DeserializeObject<PipeRequest>(json, serializerSettings);

                    if (this.RequestHandler == null)
                    {
                        nlogger_.Trace("ProcessMessageAsync: throwing - Request received but this endpoint is not set up to handle requests");
                        throw new InvalidOperationException("Request received but this endpoint is not set up to handle requests.");
                    }

                    this.RequestHandler.HandleRequest(request);
                    break;
                case MessageType.Response:
                    //this.logger.Log(() => "Handling response:" + Environment.NewLine + json);
                    nlogger_.Trace($"ProcessMessageAsync: Handling response: [{json}]");
                    PipeResponse response = JsonConvert.DeserializeObject<PipeResponse>(json, serializerSettings);

                    if (this.ResponseHandler == null)
                    {
                        nlogger_.Trace("ProcessMessageAsync: throwing - Response received but this endpoint is not set up to make requests");
                        throw new InvalidOperationException("Response received but this endpoint is not set up to make requests.");
                    }

                    this.ResponseHandler.HandleResponse(response);
                    break;
                default:
                    nlogger_.Trace($"ProcessMessageAsync: throwing - Unrecognized message type: {message.messageType}");
                    throw new InvalidOperationException($"Unrecognized message type: {message.messageType}");
            }
        }

        /// <summary>
        /// Reads the message off the input stream.
        /// </summary>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>The read message type and payload.</returns>
        private async Task<(MessageType messageType, string jsonPayload)> ReadMessageAsync(CancellationToken cancellationToken)
        {
            byte[] message = await this.ReadRawMessageAsync(cancellationToken).ConfigureAwait(false);
            var messageType = (MessageType)message[0];
            string jsonPayload = Encoding.UTF8.GetString(message, 1, message.Length - 1);

            return (messageType, jsonPayload);
        }

        /// <summary>
        /// Reads the raw message from the input stream.
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private async Task<byte[]> ReadRawMessageAsync(CancellationToken cancellationToken)
        {
            using (var memoryStream = new MemoryStream())
            {
                Int32 readBytes;
                do
                {
                    readBytes = await this.stream.ReadAsync(this.readBuffer, 0, this.readBuffer.Length, cancellationToken).ConfigureAwait(false);

                    //nlogger_.Trace($"ReadRawMessageAsync()  stream.IsConnected: [{stream.IsConnected}]");
                    // System is designed to read 0 bytes as a pipe closure
                    if (readBytes == 0)
                    {
                        string message = $"Pipe has closed with {readBytes} remaining bytes in buffer";
                        this.logger.Log(() => message);
                        nlogger_.Trace($"ReadRawMessageAsync()  Pipe has closed with {readBytes} remaining bytes in buffer");

                        // OperationCanceledException is handled as pipe closing gracefully.
                        throw new OperationCanceledException(message);
                    }

                    //this.logger.Log(() => $"Buffer is size {readBytes}");
                    //nlogger_.Trace("ReadRawMessageAsync: Buffer is size {0}", readBytes);

                    await memoryStream.WriteAsync(this.readBuffer, 0, readBytes, cancellationToken).ConfigureAwait(false);
                }
                //while (!this.stream.IsMessageComplete);
                while (!(readBytes < this.readBuffer.Length)); // *nix systems do not have support for Messages! Assume end-of-stream when readBytes is between (0, buffer size].

                return memoryStream.ToArray();
            }
        }
    }
}
