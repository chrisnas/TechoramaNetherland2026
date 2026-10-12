using Microsoft.Diagnostics.Runtime;

class Program
{
    static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            ShowUsage();
            return 1;
        }

        using DataTarget dataTarget = args[0].ToLowerInvariant() switch
        {
            "-p" when int.TryParse(args[1], out int pid) => DataTarget.CreateSnapshotAndAttach(pid),
            "-d" => DataTarget.LoadDump(args[1]),
            _ => throw new ArgumentException("Invalid arguments. Use -p <pid> or -d <dump_path>.")
        };

        if (dataTarget.ClrVersions.Length == 0)
        {
            Console.WriteLine("No CLR runtime found in the target.");
            return 1;
        }

        ClrInfo clrInfo = dataTarget.ClrVersions[0];
        using ClrRuntime runtime = clrInfo.CreateRuntime();

        DisplayClrInfo(clrInfo, dataTarget);
        DisplayGcConfiguration(runtime);
        DisplayClrHeaps(runtime);
        DisplayHeapDetails(runtime);
        DisplayHandles(runtime);
        DisplayAppDomains(runtime);
        DisplayModules(runtime);
        DisplayThreads(runtime);
        DisplayThreadPool(runtime);
        DisplayJitManagers(runtime);

        return 0;
    }

    static void ShowUsage()
    {
        Console.WriteLine("ClrMDExplorer - .NET Runtime Inspector using ClrMD");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  ClrMDExplorer -p <pid>         Attach to a live process");
        Console.WriteLine("  ClrMDExplorer -d <dump_path>   Analyze a memory dump");
    }

    static void DisplayClrInfo(ClrInfo clrInfo, DataTarget dataTarget)
    {
        Console.WriteLine("=== CLR Information ===");
        Console.WriteLine($"  Flavor:        {clrInfo.Flavor}");
        Console.WriteLine($"  Version:       {clrInfo.Version}");
        Console.WriteLine($"  Module:        {clrInfo.ModuleInfo.FileName}");
        Console.WriteLine($"  Architecture:  {(dataTarget.DataReader.PointerSize == 8 ? "64-bit" : "32-bit")}");
    }

    static void DisplayGcConfiguration(ClrRuntime runtime)
    {
        Console.WriteLine();
        Console.WriteLine("=== GC Configuration ===");
        Console.WriteLine($"  Server GC:     {runtime.Heap.IsServer}");
        Console.WriteLine($"  Heap Count:    {runtime.Heap.SubHeaps.Length}");
        Console.WriteLine($"  Can Walk Heap: {runtime.Heap.CanWalkHeap}");
    }

    static void DisplayHeapDetails(ClrRuntime runtime)
    {
        var heap = runtime.Heap;
        var segments = heap.Segments;

        Console.WriteLine();
        Console.WriteLine($"=== Heap Details ({segments.Length} segments) ===");

        var segmentsByKind = segments
            .GroupBy(s => s.Kind)
            .OrderBy(g => g.Key);

        foreach (var group in segmentsByKind)
        {
            long totalCommitted = group.Sum(s => (long)s.CommittedMemory.Length);
            long totalReserved = group.Sum(s => (long)s.ReservedMemory.Length);
            Console.WriteLine($"  {group.Key,-14} {group.Count(),3} segment(s)   " +
                              $"committed: {FormatSize(totalCommitted),10}   " +
                              $"reserved: {FormatSize(totalReserved),10}");
        }

        Console.WriteLine();
        Console.WriteLine("  Segments:");
        foreach (var segment in segments)
        {
            string pinned = segment.IsPinned ? " [Pinned]" : "";
            Console.WriteLine($"    {segment.Kind,-14} 0x{segment.Start:X}-0x{segment.End:X}  " +
                              $"committed: {FormatSize((long)segment.CommittedMemory.Length),10}  " +
                              $"reserved: {FormatSize((long)segment.ReservedMemory.Length),10}{pinned}");

            DisplayGenerationRange("Gen0", segment.Generation0);
            DisplayGenerationRange("Gen1", segment.Generation1);
            DisplayGenerationRange("Gen2", segment.Generation2);
        }
    }

    static void DisplayGenerationRange(string name, MemoryRange range)
    {
        if (range.Length > 0)
            Console.WriteLine($"      {name}: 0x{range.Start:X}-0x{range.End:X}  ({FormatSize((long)range.Length)})");
    }

    static void DisplayAppDomains(ClrRuntime runtime)
    {
        Console.WriteLine();
        Console.WriteLine("=== AppDomains ===");
        if (runtime.SharedDomain is { Name: var sharedName })
            Console.WriteLine($"  Shared Domain:  {sharedName ?? "(unnamed)"}");
        if (runtime.SystemDomain is { Name: var systemName })
            Console.WriteLine($"  System Domain:  {systemName ?? "(unnamed)"}");
        foreach (var domain in runtime.AppDomains)
            Console.WriteLine($"  AppDomain [{domain.Id}]: {domain.Name}");
    }

    static void DisplayModules(ClrRuntime runtime)
    {
        var modules = runtime.EnumerateModules().OrderBy(m => m.Name).ToList();

        Console.WriteLine();
        Console.WriteLine($"=== Modules ({modules.Count}) ===");
        foreach (var module in modules)
        {
            string name = module.Name ?? "(unknown)";
            string flags = module.IsDynamic ? " [Dynamic]" : "";
            string debugMode = module.DebuggingMode != default
                ? $"  debug: {module.DebuggingMode}"
                : "";

            Console.WriteLine($"  {name}{flags}{debugMode}");

            if (module.ImageBase != 0)
                Console.WriteLine($"    Base: 0x{module.ImageBase:X}  Size: {FormatSize((long)module.Size)}");

            if (module.Pdb is { } pdb)
                Console.WriteLine($"    PDB:  {pdb.Path}");
        }
    }

    static void DisplayThreadPool(ClrRuntime runtime)
    {
        Console.WriteLine();
        Console.WriteLine("=== ThreadPool ===");

        ClrThreadPool? tp = runtime.ThreadPool;
        if (tp is null)
        {
            Console.WriteLine("  ThreadPool data is not available.");
            return;
        }

        string implementation = tp.UsingWindowsThreadPool ? "Windows"
            : tp.UsingPortableThreadPool ? "Portable (.NET)"
            : "Legacy (Desktop CLR)";
        Console.WriteLine($"  Implementation:  {implementation}");
        Console.WriteLine($"  CPU Utilization:  {tp.CpuUtilization}%");
        Console.WriteLine($"  Worker Threads:   active={tp.ActiveWorkerThreads}  idle={tp.IdleWorkerThreads}  retired={tp.RetiredWorkerThreads}");
        Console.WriteLine($"  Thread Limits:    min={tp.MinThreads}  max={tp.MaxThreads}");

        var hillClimbingLog = tp.EnumerateHillClimbingLog().ToList();
        if (hillClimbingLog.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"  HillClimbing Log ({hillClimbingLog.Count} entries):");
            Console.WriteLine($"    {"Tick",12} {"Transition",-20} {"New Count",10} {"Samples",8} {"Throughput",11}");
            foreach (var entry in hillClimbingLog)
            {
                Console.WriteLine($"    {entry.TickCount,12} {entry.StateOrTransition,-20} {entry.NewThreadCount,10} {entry.SampleCount,8} {entry.Throughput,11:F2}");
            }
        }

        var workRequests = tp.EnumerateLegacyWorkRequests().ToList();
        if (workRequests.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"  Legacy Work Requests ({workRequests.Count}):");
            foreach (var request in workRequests)
                Console.WriteLine($"    {request}");
        }
    }

    static void DisplayThreads(ClrRuntime runtime)
    {
        var threads = runtime.Threads;
        int aliveCount = threads.Count(t => t.IsAlive);

        Console.WriteLine();
        Console.WriteLine($"=== Threads ({threads.Length} total, {aliveCount} alive) ===");
        foreach (var thread in threads)
        {
            if (!thread.IsAlive)
                continue;

            var roles = new List<string>();
            if (thread.IsFinalizer) roles.Add("Finalizer");
            if (thread.IsGc) roles.Add("GC");
            string roleLabel = roles.Count > 0 ? $"  [{string.Join(", ", roles)}]" : "";

            Console.WriteLine($"  Thread OS=0x{thread.OSThreadId:X}  ManagedId={thread.ManagedThreadId}{roleLabel}");
            Console.WriteLine($"    GC Mode:    {thread.GCMode}");
            if (thread.LockCount > 0 && thread.LockCount != uint.MaxValue)
                Console.WriteLine($"    Lock Count: {thread.LockCount}");
            Console.WriteLine($"    Stack:      0x{thread.StackBase:X} - 0x{thread.StackLimit:X}");

            if (thread.CurrentException is { } ex)
                Console.WriteLine($"    Exception:  {ex.Type?.Name} - {ex.Message}");

            foreach (var frame in thread.EnumerateStackTrace(includeContext: false))
            {
                string methodName = frame.Method != null
                    ? $"{frame.Method.Type?.Name}.{frame.Method.Name}"
                    : frame.FrameName ?? "(unknown)";

                string kind = frame.Kind == ClrStackFrameKind.Runtime ? "[runtime] " : "";
                Console.WriteLine($"      {kind}{methodName}");
            }

            Console.WriteLine();
        }
    }

    static void DisplayHandles(ClrRuntime runtime)
    {
        var handles = runtime.EnumerateHandles().ToList();

        Console.WriteLine();
        Console.WriteLine($"=== GC Handles ({handles.Count}) ===");

        var byKind = handles
            .GroupBy(h => h.HandleKind)
            .OrderByDescending(g => g.Count());

        foreach (var group in byKind)
        {
            int strong = group.Count(h => h.IsStrong);
            int weak = group.Count() - strong;
            Console.WriteLine($"  {group.Key,-14} {group.Count(),6}   (strong: {strong}, weak: {weak})");
        }

        var pinned = handles.Where(h => h.HandleKind == ClrHandleKind.Pinned).ToList();
        if (pinned.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"  Pinned Handles ({pinned.Count}):");
            foreach (var h in pinned)
            {
                string typeName = h.Dependent.Type?.Name ?? h.ToString() ?? "(unknown)";
                Console.WriteLine($"    {typeName}");
            }
        }

        var refCounted = handles.Where(h => h.HandleKind == ClrHandleKind.RefCounted).ToList();
        if (refCounted.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"  RefCounted Handles ({refCounted.Count}):");
            foreach (var h in refCounted)
                Console.WriteLine($"    RefCount={h.ReferenceCount}  {(h.IsStrong ? "[Strong]" : "[Weak]")}");
        }
    }

    static void DisplayJitManagers(ClrRuntime runtime)
    {
        var managers = runtime.EnumerateJitManagers().ToList();

        Console.WriteLine();
        Console.WriteLine($"=== JIT Managers ({managers.Count}) ===");

        if (managers.Count == 0)
        {
            Console.WriteLine("  No JIT managers found.");
            return;
        }

        foreach (var manager in managers)
        {
            var heaps = manager.EnumerateNativeHeaps().ToList();
            long totalSize = heaps.Sum(h => (long)h.MemoryRange.Length);

            Console.WriteLine($"  Manager 0x{manager.Address:X}  Kind={manager.Kind}  " +
                              $"{heaps.Count} heap(s)  total: {FormatSize(totalSize)}");

            foreach (var heap in heaps)
            {
                Console.WriteLine($"    {heap.Kind,-20} 0x{heap.MemoryRange.Start:X}-0x{heap.MemoryRange.End:X}  " +
                                  $"{FormatSize((long)heap.MemoryRange.Length),10}  {heap.State}");
            }
        }
    }

    static void DisplayClrHeaps(ClrRuntime runtime)
    {
        var nativeHeaps = runtime.EnumerateClrNativeHeaps().ToList();

        Console.WriteLine();
        Console.WriteLine($"=== CLR Native Heaps ({nativeHeaps.Count}) ===");

        if (nativeHeaps.Count == 0)
        {
            Console.WriteLine("  No native heaps enumerated.");
            return;
        }

        var byKind = nativeHeaps
            .GroupBy(h => h.Kind)
            .OrderByDescending(g => g.Sum(h => (long)h.MemoryRange.Length));

        foreach (var group in byKind)
        {
            long totalSize = group.Sum(h => (long)h.MemoryRange.Length);
            Console.WriteLine($"  {group.Key,-22} {group.Count(),3} heap(s)   total: {FormatSize(totalSize),10}");

            foreach (var heap in group)
            {
                Console.WriteLine($"    0x{heap.MemoryRange.Start:X}-0x{heap.MemoryRange.End:X}  " +
                                  $"{FormatSize((long)heap.MemoryRange.Length),10}  {heap.State}");
            }
        }
    }

    static string FormatSize(long bytes)
    {
        return bytes switch
        {
            >= 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024 * 1024):F2} GB",
            >= 1024 * 1024 => $"{bytes / (1024.0 * 1024):F2} MB",
            >= 1024 => $"{bytes / 1024.0:F2} KB",
            _ => $"{bytes} B"
        };
    }
}
