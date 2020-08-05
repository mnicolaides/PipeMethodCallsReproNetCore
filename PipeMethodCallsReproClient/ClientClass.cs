using System;
using System.Threading;
using System.Threading.Tasks;
using Common;
using Pipe.PipeMethodCalls;

namespace PipeMethodCallsReproClient
{
    public class ClientClass
    {
        private PipeClientWithCallback<IServerMethods, IClientMethods> ClientPipe = null;
        // This monitors the client pipe in case it's disconnected.
        // It's started in Program.Main()
        public async Task ClientPipeMonitor(bool justStartIt = false)
        {
            while (true)
            {
                if (ClientPipe == null ||
                    //ClientPipe.RawPipeStream.IsConnected == false || // I just added this and it works.
                    ClientPipe?.State != PipeState.Connected)
                {
                    // Now reconnect
                    Console.WriteLine("Connecting...");
                    ClientPipe?.Dispose();
                    ClientPipe = null;
                    ClientPipe = new PipeClientWithCallback<IServerMethods, IClientMethods>("Pipename", () => new PipeClientImpl());
                    await ClientPipe.ConnectAsync();
                    Console.WriteLine("Connected.");
                    if (justStartIt)    break;  // Just let it start and break out of the loop.
                }
                await Task.Run(() => Thread.Sleep(100));
            }
        }

        // This is used by some code in Process 1
        public void SendMessage(string message)
        {
            try
            {
                if (ClientPipe != null &&
                    //ClientPipe.RawPipeStream.IsConnected == true &&
                    ClientPipe.State == PipeState.Connected)
                    ClientPipe.InvokeAsync(cmd => cmd.WriteMessage(message));
            }
            catch (Exception e)
            {
                ClientPipe.Dispose();
                ClientPipe = null;
                Console.WriteLine($"ClientClass  Exception message: [{e.Message}] | stack: [{e.StackTrace}] | innerException: [{e.InnerException}].");
            }
        }
    }

    public class PipeClientImpl : IClientMethods
    {
    }
}
