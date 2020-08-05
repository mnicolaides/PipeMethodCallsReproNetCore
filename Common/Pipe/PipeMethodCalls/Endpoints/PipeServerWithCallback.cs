﻿using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace Pipe.PipeMethodCalls
{
    /// <summary>
    /// A named pipe server with a callback channel.
    /// </summary>
    /// <typeparam name="TRequesting">The callback channel interface that the client will be handling.</typeparam>
    /// <typeparam name="THandling">The interface for requests that this server will be handling.</typeparam>
    public class PipeServerWithCallback<TRequesting, THandling> : IDisposable, IPipeServerWithCallback<TRequesting>, IPipeInvokerHost<TRequesting>
        where TRequesting : class
        where THandling : class
    {
        private readonly string pipeName;
        private readonly Func<THandling> handlerFactoryFunc;
        private readonly PipeOptions? options;
        private NamedPipeServerStream rawPipeStream;
        private Action<string> logger;
        private PipeMessageProcessor messageProcessor = new PipeMessageProcessor();
        private string _uniqueId = string.Empty;

        /// <summary>
        /// Initializes a new instance of the <see cref="PipeServerWithCallback"/> class.
        /// </summary>
        /// <param name="pipeName">The pipe name.</param>
        /// <param name="handlerFactoryFunc">A factory function to provide the handler implementation.</param>
        /// <param name="options">Extra options for the pipe.</param>
        public PipeServerWithCallback(string pipeName, Func<THandling> handlerFactoryFunc, PipeOptions? options = null)
        {
            this.pipeName = pipeName;
            this.handlerFactoryFunc = handlerFactoryFunc;
            this.options = options;

            if (string.IsNullOrEmpty(_uniqueId))
            {
                _uniqueId = Guid.NewGuid().ToString();
                int index = _uniqueId.LastIndexOf('-');
                _uniqueId = _uniqueId.Substring(index + 1, _uniqueId.Length - index - 1);
            }
            //Console.WriteLine($"uniqueId: [{_uniqueId}]");
        }

        /// <summary>
        /// Gets the state of the pipe.
        /// </summary>
        public PipeState State => this.messageProcessor.State;

        /// <summary>
        /// Gets the method invoker.
        /// </summary>
        /// <remarks>This is null before connecting.</remarks>
        public IPipeInvoker<TRequesting> Invoker { get; private set; }

        /// <summary>
        /// Sets up the given action as a logger for the module.
        /// </summary>
        /// <param name="logger">The logger action.</param>
        public void SetLogger(Action<string> logger)
        {
            this.logger = logger;
        }

        public string UniqueId => this._uniqueId;

        public object ObjClient { set; get; }

        public string Pipename { get { return pipeName; } }

        /// <summary>
        /// Waits for a client to connect to the pipe.
        /// </summary>
        /// <param name="cancellationToken">A token to cancel the request.</param>
        /// <exception cref="IOException">Thrown when the connection fails.</exception>
        public async Task WaitForConnectionAsync(CancellationToken cancellationToken = default)
        {
            PipeOptions pipeOptionsToPass;
            if (this.options == null)
            {
                pipeOptionsToPass = PipeOptions.Asynchronous;
            }
            else
            {
                pipeOptionsToPass = this.options.Value | PipeOptions.Asynchronous;
            }

            this.rawPipeStream = new NamedPipeServerStream(this.pipeName, PipeDirection.InOut, 5, PipeTransmissionMode.Byte, pipeOptionsToPass);
            this.rawPipeStream.ReadMode = PipeTransmissionMode.Byte;

            //this.logger.Log(() => $"Set up named pipe server '{this.pipeName}'.");

            await this.rawPipeStream.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);

            //this.logger.Log(() => $"Connected to client '{this.pipeName}'.");

            var wrappedPipeStream = new PipeStreamWrapper(this.rawPipeStream, this.logger);
            this.Invoker = new MethodInvoker<TRequesting>(wrappedPipeStream, this.messageProcessor);
            var requestHandler = new RequestHandler<THandling>(wrappedPipeStream, this.handlerFactoryFunc);

            this.messageProcessor.StartProcessing(wrappedPipeStream);
        }

        /// <summary>
        /// Wait for the other end to close the pipe.
        /// </summary>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <exception cref="IOException">Thrown when the pipe has closed due to an unknown error.</exception>
        /// <remarks>This does not throw when the other end closes the pipe.</remarks>
        public Task WaitForRemotePipeCloseAsync(CancellationToken cancellationToken = default)
        {
            //this.logger.Log(() => $"WaitForRemotePipeCloseAsync()  '{this.pipeName}'");
            return this.messageProcessor.WaitForRemotePipeCloseAsync(cancellationToken);
        }

        public void ClosePipeStream()
        {
            if (this.rawPipeStream != null)
            {
                //this.logger.Log(() => $"ClosePipeStream()  '{this.pipeName}'");
                this.rawPipeStream.Close();
            }
        }

        #region IDisposable Support
        private bool disposed = false;

        protected virtual void Dispose(bool disposing)
        {
            if (!this.disposed)
            {
                if (disposing)
                {
                    this.messageProcessor.Dispose();

                    if (this.rawPipeStream != null)
                    {
                        this.rawPipeStream.Dispose();
                    }
                }

                this.disposed = true;
            }
        }

        /// <summary>
        /// Closes the pipe.
        /// </summary>
        public void Dispose()
        {
            this.Dispose(true);
        }
        #endregion
    }
}