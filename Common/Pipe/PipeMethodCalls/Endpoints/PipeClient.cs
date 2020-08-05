﻿using System;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;

namespace Pipe.PipeMethodCalls
{
    /// <summary>
    /// A named pipe client.
    /// </summary>
    /// <typeparam name="TRequesting">The interface that the client will be invoking on the server.</typeparam>
    public class PipeClient<TRequesting> : IPipeClient<TRequesting>, IDisposable
        where TRequesting : class
    {
        private readonly string pipeName;
        private readonly string serverName;
        private readonly PipeOptions? options;
        private readonly TokenImpersonationLevel? impersonationLevel;
        private readonly HandleInheritability? inheritability;
        private NamedPipeClientStream rawPipeStream;
        private PipeStreamWrapper wrappedPipeStream;
        private Action<string> logger;
        private PipeMessageProcessor messageProcessor = new PipeMessageProcessor();

        /// <summary>
        /// Initializes a new instance of the <see cref="PipeClient"/> class.
        /// </summary>
        /// <param name="pipeName">The name of the pipe.</param>
        public PipeClient(string pipeName)
            : this(".", pipeName)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PipeClient"/> class.
        /// </summary>
        /// <param name="pipeName">The name of the pipe.</param>
        /// <param name="options">One of the enumeration values that determines how to open or create the pipe.</param>
        /// <param name="impersonationLevel">One of the enumeration values that determines the security impersonation level.</param>
        /// <param name="inheritability">One of the enumeration values that determines whether the underlying handle will be inheritable by child processes.</param>
        public PipeClient(string pipeName, PipeOptions options, TokenImpersonationLevel impersonationLevel, HandleInheritability inheritability)
            : this(".", pipeName, options, impersonationLevel, inheritability)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PipeClient"/> class.
        /// </summary>
        /// <param name="serverName">The name of the server to connect to.</param>
        /// <param name="pipeName">The name of the pipe.</param>
        public PipeClient(string serverName, string pipeName)
        {
            this.pipeName = pipeName;
            this.serverName = serverName;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PipeClient"/> class.
        /// </summary>
        /// <param name="serverName">The name of the server to connect to.</param>
        /// <param name="pipeName">The name of the pipe.</param>
        /// <param name="options">One of the enumeration values that determines how to open or create the pipe.</param>
        /// <param name="impersonationLevel">One of the enumeration values that determines the security impersonation level.</param>
        /// <param name="inheritability">One of the enumeration values that determines whether the underlying handle will be inheritable by child processes.</param>
        public PipeClient(string serverName, string pipeName, PipeOptions options, TokenImpersonationLevel impersonationLevel, HandleInheritability inheritability)
        {
            this.pipeName = pipeName;
            this.serverName = serverName;
            this.options = options;
            this.impersonationLevel = impersonationLevel;
            this.inheritability = inheritability;
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

        /// <summary>
        /// Connects the pipe to the server.
        /// </summary>
        /// <param name="cancellationToken">A token to cancel the request.</param>
        /// <exception cref="IOException">Thrown when the connection fails.</exception>
        public async Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            if (this.State != PipeState.NotOpened)
            {
                throw new InvalidOperationException("Can only call ConnectAsync once");
            }

            //this.logger.Log(() => $"Connecting to named pipe '{this.pipeName}' on machine '{this.serverName}'");

            if (this.options != null)
            {
                this.rawPipeStream = new NamedPipeClientStream(this.serverName, this.pipeName, PipeDirection.InOut, this.options.Value | PipeOptions.Asynchronous, this.impersonationLevel.Value, this.inheritability.Value);
            }
            else
            {
                this.rawPipeStream = new NamedPipeClientStream(this.serverName, this.pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            }

            await this.rawPipeStream.ConnectAsync(cancellationToken).ConfigureAwait(false);
            //this.logger.Log(() => "Connected.");

            this.rawPipeStream.ReadMode = PipeTransmissionMode.Byte;

            this.wrappedPipeStream = new PipeStreamWrapper(this.rawPipeStream, this.logger);
            this.Invoker = new MethodInvoker<TRequesting>(this.wrappedPipeStream, this.messageProcessor);

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
            return this.messageProcessor.WaitForRemotePipeCloseAsync(cancellationToken);
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
