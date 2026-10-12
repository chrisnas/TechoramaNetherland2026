using System.Diagnostics;

var cache = new Dictionary<int, CacheEntry>();
int id = 0;

Console.WriteLine($"LeakyApp started — PID {Process.GetCurrentProcess().Id}");
Console.WriteLine("Leaking memory... press Enter to stop.\n");

var timer = new Timer(_ =>
{
    for (int i = 0; i < 500; i++)
    {
        var entry = new CacheEntry
        {
            Id = id,
            Category = $"Category-{id % 12}",
            Region   = $"Region-{id % 8}",
            Status   = id % 2 == 0 ? "Active" : "Inactive",
            Payload  = new byte[256]
        };
        cache[id] = entry;
        id++;
    }

    long mb = Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024);
    Console.Write($"\r  Entries: {cache.Count:N0}   Working set: {mb} MB   ");
}, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(200));

Console.ReadLine();
timer.Dispose();

class CacheEntry
{
    public int Id { get; set; }
    public string Category { get; set; } = "";
    public string Region { get; set; } = "";
    public string Status { get; set; } = "";
    public byte[] Payload { get; set; } = [];
}
