using System;
using System.Threading.Tasks;

namespace PipeMethodCallsReproServer
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var server = new ServerClass();
            await server.ServerPipeMonitor(true);
            Task clpmTask = Task.Run(() => server.ServerPipeMonitor());

            Console.ReadKey();
        }
    }
}
