using System;
using System.Threading.Tasks;

namespace PipeMethodCallsReproClient
{
    class Program
    {
        private static string DATETIMEFORMAT = "yyyy-MM-dd HH:mm:ss.fff";

        static async Task Main(string[] args)
        {
            var client = new ClientClass();
            await client.ClientPipeMonitor(true);
            Task clpmTask = Task.Run(() => client.ClientPipeMonitor()); // Hacky, but this will monitor for any pipe disconnects

            int count = 0;
            string message = string.Empty;
            while (true)
            {
                var key = Console.ReadKey();
                if(key.KeyChar == 'a')
                {
                    Console.WriteLine($"[{key.KeyChar}] was pressed.");
                    break;
                }
                count = 0;
                message = string.Empty;
                Console.WriteLine($"Sending messages. [{key.KeyChar}] was pressed.");
                while (count < 1000)
                {
                    message = $"{GetCurrentDateTime()} This is a test message {count.ToString()} from the client.";
                    Console.WriteLine(message);
                    client.SendMessage(message);
                    await Task.Delay(1);
                    count++;
                }
            }

            Console.WriteLine("Completed sending messages");

            Console.ReadKey();
        }

        public static string GetCurrentDateTime()
        {
            return DateTime.UtcNow.ToString(DATETIMEFORMAT, System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
