﻿namespace Pipe.PipeMethodCalls
{
    /// <summary>
    /// Handles a response message received from a remote endpoint.
    /// </summary>
    internal interface IResponseHandler
    {
        /// <summary>
        /// Handles a response message received from a remote endpoint.
        /// </summary>
        /// <param name="response">The response message to handle.</param>
        void HandleResponse(PipeResponse response);
    }
}
