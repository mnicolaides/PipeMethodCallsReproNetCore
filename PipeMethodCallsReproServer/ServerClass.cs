using System;
using System.Threading;
using System.Threading.Tasks;
using Common;
using Pipe.PipeMethodCalls;

namespace PipeMethodCallsReproServer
{
    public class ServerClass
    {
        private PipeServerWithCallback<IClientMethods, IServerMethods> ServerPipe = new PipeServerWithCallback<IClientMethods, IServerMethods>("Pipename", () => new ServerClientImpl());
        // This monitors the server pipe in case it's disconnected.
        // It's started in Program.Main()
        public async Task ServerPipeMonitor(bool justStartIt = false)
        {
            while (true)
            {
                if (ServerPipe.State != PipeState.Connected)
                {
                    Console.WriteLine("Connecting...");
                    ServerPipe.Dispose();
                    ServerPipe = null;
                    ServerPipe = new PipeServerWithCallback<IClientMethods, IServerMethods>("Pipename", () => new ServerClientImpl());
                    await ServerPipe.WaitForConnectionAsync();
                    Console.WriteLine("Connected.");
                    if (justStartIt) break;  // Just let it start and break out of the loop.
                }
                await Task.Run(() => Thread.Sleep(100));
            }
        }
    }


    public class ServerClientImpl : IServerMethods
    {
        // This will be triggered by Process 1's "ClientPipe.InvokeAsync(cmd => cmd.WriteMessage(message))"
        public bool WriteMessage(string message)
        {
            Console.WriteLine(message);
            return true;
        }
    }
}
