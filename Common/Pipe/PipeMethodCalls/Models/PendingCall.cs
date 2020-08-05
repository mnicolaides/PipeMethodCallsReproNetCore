﻿using System.Threading.Tasks;

namespace Pipe.PipeMethodCalls
{
    /// <summary>
    /// Represents a pending call executing on the remote endpoint.
    /// </summary>
    internal class PendingCall
    {
        /// <summary>
        /// The task completion source for the call.
        /// </summary>
        public TaskCompletionSource<PipeResponse> TaskCompletionSource { get; } = new TaskCompletionSource<PipeResponse>();
    }
}
