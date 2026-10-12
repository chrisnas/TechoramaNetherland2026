using System.Diagnostics;
using Microsoft.Diagnostics.NETCore.Client;

internal class DotNetProcessLister
{
    private const int PidWidth = 7;
    private const int NameWidth = 20;
    private const int MemWidth = 11;
    private const string Indent = "           ";

    private readonly int _currentPid = Environment.ProcessId;

    public int Run()
    {
        var pids = DiagnosticsClient.GetPublishedProcesses()
            .Where(pid => pid != _currentPid)
            .Order()
            .ToList();

        if (pids.Count == 0)
        {
            Console.WriteLine("No .NET processes found.");
            return 0;
        }

        PrintHeader();

        int count = 0;
        foreach (int pid in pids)
        {
            if (PrintProcess(pid))
                count++;
        }

        Console.WriteLine($"{count} .NET process(es)");
        return 0;
    }

    private static void PrintHeader()
    {
        Console.WriteLine(
            $"{"PID",PidWidth}  {"Name".PadRight(NameWidth)}  {"Working Set",MemWidth}  Path");
        Console.WriteLine(new string('-', PidWidth + NameWidth + MemWidth + 60));
    }

    private static bool PrintProcess(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            string name = process.ProcessName;
            string mem = FormatSize(process.WorkingSet64);
            string? path = null;

            try
            {
                path = process.MainModule?.FileName;
            }
            catch (Exception)
            {
                // Access denied for elevated processes
            }

            Console.WriteLine(
                $"{pid,PidWidth}  {name.PadRight(NameWidth)}  {mem,MemWidth}  " +
                $"{path ?? "(access denied)"}");

            if (path == null)
            {
                Console.WriteLine($"{Indent}(unable to access process details)");
                Console.WriteLine();
                return true;
            }

            // don't want to wait
            if (name == "backgroundTaskHost")
            {
                Console.WriteLine($"{Indent}(can't wait)");
                Console.WriteLine();
                return true;
            }

            Dictionary<string, string>? envVars = null;
            try
            {
                var client = new DiagnosticsClient(pid);
                envVars = client.GetProcessEnvironment();
            }
            catch (Exception)
            {
                // Runtime may not respond (e.g. during startup or shutdown)
            }

            if (envVars is { Count: > 0 })
            {
                foreach (var kvp in envVars.OrderBy(v => v.Key, StringComparer.OrdinalIgnoreCase))
                {
                    // Don't want to leak my Datadog API key on stage   ;^)
                    if (kvp.Key.Contains("DATADOG_") || kvp.Key.Contains("DD_"))
                        continue;

                    Console.WriteLine($"{Indent}{kvp.Key}={kvp.Value}");
                }
            }
            else
            {
                Console.WriteLine($"{Indent}(environment variables unavailable)");
            }

            Console.WriteLine();
            return true;
        }
        catch (ArgumentException)
        {
            // Process exited between enumeration and lookup
            return false;
        }
    }

    private static string FormatSize(long bytes)
    {
        return bytes switch
        {
            >= 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024 * 1024):F2} GB",
            >= 1024 * 1024        => $"{bytes / (1024.0 * 1024):F1} MB",
            >= 1024               => $"{bytes / 1024.0:F1} KB",
            _                     => $"{bytes} B"
        };
    }
}
