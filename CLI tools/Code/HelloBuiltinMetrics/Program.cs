using System.Net;

namespace HelloBuiltinMetrics
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            string[] uris = ["http://example.com", "http://httpbin.org/get", "https://example.com", "https://httpbin.org/get"];
            using HttpClient client = new()
            {
                DefaultRequestVersion = HttpVersion.Version20
            };

            Console.WriteLine("Press any key to start.");
            Console.ReadKey();

            while (!Console.KeyAvailable)
            {
                await Parallel.ForAsync(0, Random.Shared.Next(20), async (_, ct) =>
                {
                    string uri = uris[Random.Shared.Next(uris.Length)];
                    try
                    {
                        byte[] bytes = await client.GetByteArrayAsync(uri, ct);
                        await Console.Out.WriteLineAsync($"{uri} - received {bytes.Length} bytes.");
                    }
                    catch { await Console.Out.WriteLineAsync($"{uri} - failed."); }
                });
            }
        }
    }
}
