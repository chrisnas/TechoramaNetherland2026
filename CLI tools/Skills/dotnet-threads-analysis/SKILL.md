---
name: dotnet-threads-analysis
description: >-
  Analyze .NET application threading issues using CLI diagnostic tools. Use when
  investigating thread contention, deadlocks, thread pool starvation,
  sync-over-async patterns, frozen or unresponsive applications, hangs,
  Task.Wait, GetAwaiter().GetResult(), or high thread counts — on a live
  process, from a .dmp file, or from a memory dump. Works with dotnet-counters,
  dotnet-trace, dotnet-dump, and dotnet-pstacks.
---

# .NET Thread Analysis

Diagnose thread contention, deadlocks, thread pool starvation, and
sync-over-async issues in .NET applications using the standard Microsoft
diagnostic CLI tools and custom ClrMD-based tools — no code changes to the
target application required.

## Tool Selection

When MCP servers are available, prefer them over CLI — they return structured
data that is easier to reason about and avoids raw text parsing.

| Goal | Prefer (MCP) | Fallback (CLI) |
|------|--------------|----------------|
| Parallel stacks | `get_parallel_stacks` (server: `user-dotnet-pstacks`) with `dumpPath` or `pid` | `dotnet-pstacks <dump>` |
| Thread listing | — | `dotnet-dump analyze -c "threads"` |
| Lock ownership | — | `dotnet-dump analyze -c "syncblk"` |
| Full stacks | — | `dotnet-dump analyze -c "clrstack -all"` |

MCP parameters for `get_parallel_stacks`:
- `dumpPath` or `pid` (mutually exclusive)
- `threadIdLimit` (default 4) — use `-1` to show all thread IDs when drilling into a specific group

## MANDATORY — Create Summary File First

**Before running ANY diagnostic command**, create the summary file named
`<date>-<time>_thread_analysis_SUMMARY.md` (e.g., `2026-05-18-1800_thread_analysis_SUMMARY.md`)
in the current working directory using the template in
[summary-template.md](summary-template.md). Update it after every meaningful
milestone. This is NOT optional.

## Before You Start — Collect Context

Before running any command, gather from the user:

1. **Target**: dump file path OR live process PID / name?
2. **Symptom**: what was observed? (e.g., "app froze", "requests timing out", "CPU at 100%")
3. **Production?** If yes, warn before any intrusive collection (`dotnet-dump collect`)
4. **Timeframe**: when did the issue start? Is it intermittent or persistent?
5. **Thread count**: if known, how many threads does the process have? (impacts output size)

## Prerequisites

The following tools must be installed as global .NET CLI tools:

```bash
dotnet tool install -g dotnet-dump
dotnet tool install -g dotnet-counters
dotnet tool install -g dotnet-trace
dotnet tool install -g dotnet-pstacks
```

Verify with `dotnet tool list -g`. A .NET runtime (6.0+) is required on the
machine; an SDK is not needed to *run* the tools.

## Key Technique: Non-Interactive SOS Commands

`dotnet-dump analyze` is interactive by default, which does not work well with
agents. Always use the `-c` flag to run SOS commands non-interactively:

```bash
dotnet-dump analyze <dump-path> -c "<command1>" -c "<command2>" -c "exit"
```

Multiple `-c` flags are executed in sequence. Always end with `-c "exit"`.

---

## Agent Workflow — Decision Tree

### Checkpoint 0 — Inputs

- [ ] Target confirmed (dump path or PID)
- [ ] Symptom recorded
- [ ] Summary file created (see [summary-template.md](summary-template.md))

### Checkpoint 1 — Triage (always run first)

**For dump files — run these two in parallel** (they read the same file independently):

Shell 1:
```bash
dotnet-dump analyze <dump> -c "threads" -c "syncblk" -c "exit"
```

Shell 2 (parallel) — prefer MCP `get_parallel_stacks`, fall back to:
```bash
dotnet-pstacks <dump>
```

**For live processes:**

```bash
dotnet-counters collect -p <PID> --counters System.Runtime --format json --duration 00:00:30 -o counters.json
```

### Checkpoint 2 — Route by Finding

| Finding | Next Step |
|---------|-----------|
| Many threads blocked at the same lock frame (pstacks) | Lock contention → `syncblk` to find owner → `clrstack` on owner thread |
| Two groups stuck at opposite lock acquisitions (pstacks) | Deadlock → **Deadlock Detection** workflow |
| `threadpool-thread-count` climbing steadily (live) | Sync-over-async → escalate to dump, look for `Task.Result` / `.GetAwaiter().GetResult()` in stacks |
| `threadpool-queue-length` high, completions flat (live) | Thread pool exhaustion → escalate to dump |
| Many threads blocked on `SemaphoreSlim.Wait` | Async throttle saturation → check semaphore count and callers |
| `syncblk` shows one thread owning many locks | Single-thread bottleneck → `clrstack` on that thread |
| Thread count normal, no contention | Issue may not be thread-related → consider memory skill |

---

## Live Process Investigation

### Identify the Target

```bash
dotnet-dump ps
```

Lists running .NET processes with PID and name. Confirm the target PID.

### Collect Thread Counters

```bash
dotnet-counters collect -p <PID> --counters System.Runtime --format json --duration 00:00:30 -o counters.json
```

Key counters to check in the output:
- `threadpool-thread-count` — climbing steadily = possible starvation or
  sync-over-async pattern
- `threadpool-queue-length` — high values = work items waiting for threads
- `monitor-lock-contention-count` — spikes = lock contention
- `threadpool-completed-items-count` — compare with queue length to gauge
  throughput

### Collect an EventPipe Trace

```bash
dotnet-trace collect -p <PID> --providers Microsoft-Windows-DotNETRuntime --duration 00:00:30 -o trace.nettrace
```

Produces a `.nettrace` file for offline analysis. Add
`Microsoft-DotNETCore-SampleProfiler` for CPU profiling to identify hot threads.

Skip trace collection if you already have a dump file — traces add little value
when full call stacks are available.

### Escalate to Full Dump

If counters reveal contention or starvation but you need to see exact call
stacks and lock ownership, capture a full dump:

```bash
dotnet-dump collect -p <PID>
```

**Warning:** This briefly freezes the process. Confirm with the user before
running on a production system. Then continue with the **Dump-Based
Investigation** workflow below.

---

## Dump-Based Investigation

Use this workflow when analyzing a `.dmp` file — either provided by the user or
captured via `dotnet-dump collect`.

### Parallel Stacks Overview

**Prefer MCP** if `user-dotnet-pstacks` server is available:
Call `get_parallel_stacks` with `dumpPath` parameter, `threadIdLimit: 4`.

**CLI fallback:**

```bash
dotnet-pstacks <dump>
```

Merges threads with identical call stacks. Quickly reveals:
- Many threads blocked at the same lock frame → contention
- Two groups stuck in opposite lock acquisition → potential deadlock

**Parsing parallel stacks output:**
```
==> 12 threads <==                           <-- 12 threads share this stack
  System.Threading.Monitor.Enter(Object)     <-- blocked here = contention
  MyApp.Services.CacheService.GetValue()
  ...

==> 3 threads <==
  System.Threading.Tasks.Task.Wait()         <-- sync-over-async pattern
  MyApp.Controllers.ApiController.Get()
```

- Groups with high thread counts at lock frames = contention hotspot
- `Task.Wait()`, `Task.Result`, `.GetAwaiter().GetResult()` = sync-over-async

### List All Managed Threads

```bash
dotnet-dump analyze <dump> -c "threads" -c "exit"
```

**Parsing `threads` output:**
```
   ID   OSID  ThreadOBJ    State  GC Mode    Domain           Lock Count  Apt  Exception
    1   abc1  00007FF8...  28220  Preemptive 00007FF8...       0          STA
    6   abc6  00007FF8...  a]020  Preemptive 00007FF8...       2          MTA  <-- Lock Count > 0: holds locks
```

- `Lock Count > 0` = thread is holding locks (cross-reference with `syncblk`)
- `State` column encodes thread status (check for `GC Suspend`, `Dead`, etc.)

### Full Stack Trace for All Threads

```bash
dotnet-dump analyze <dump> -c "clrstack -all" -c "exit"
```

For processes with > 100 threads, redirect to file and read selectively:

```bash
dotnet-dump analyze <dump> -c "clrstack -all" -c "exit" > stacks.txt
```

Look for `Monitor.Enter`, `Monitor.Wait`, `SemaphoreSlim.Wait`, or
`ManualResetEventSlim.Wait` frames to identify where threads are blocked.

### Sync Block Table (Lock Ownership)

```bash
dotnet-dump analyze <dump> -c "syncblk" -c "exit"
```

**Parsing `syncblk` output:**
```
Index  SyncBlock  MonitorHeld  Owning Thread Info   SyncBlock Owner
    5  0000abcd          3      abc1  6   00007FF8  00007FF8abcd1234 MyApp.LockObj
                         ^      ^^^^                ^^^^^^^^^^^^^^^^
                  held count   owner thread ID      the object being locked on
```

- `MonitorHeld > 0` = lock is actively held
- `Owning Thread Info` = the thread ID that holds the lock — cross-reference with `clrstack` for that thread
- `SyncBlock Owner` = the object being used as the lock target

### Deadlock Detection

When the application is frozen and not responding, run the full sequence:

```bash
dotnet-dump analyze <dump> -c "syncblk" -c "threads" -c "clrstack -all" -c "exit"
```

And in parallel (MCP or CLI):

```bash
dotnet-pstacks <dump>
```

Cross-reference:
1. From `syncblk`: which threads own which locks
2. From `clrstack -all`: where each lock-owning thread is blocked
3. IF Thread A owns Lock 1 AND is blocked waiting for Lock 2,
   WHILE Thread B owns Lock 2 AND is blocked waiting for Lock 1 → **deadlock**

Report the deadlock cycle clearly: "Thread X holds Lock A (object 0x...)
and waits for Lock B (object 0x...); Thread Y holds Lock B and waits for
Lock A."

### Inspect Contended Code

When a culprit is found (contended lock, deadlock participant, or sync-over-async
blocker), inspect the lock object:

```bash
dotnet-dump analyze <dump> -c "dumpobj <lock-object-address>" -c "exit"
```

Use `dumpmt`, `dumpclass`, and `dumpil` to examine the type that owns or acquires
the lock. Alternatively, dump the assembly with `.writemem` on the module and
decompile with `ilspycmd` (`dotnet tool install ilspycmd -g` if needed).

---

## Diagnosis Patterns

| Symptom | Likely Cause | Key Command |
|---------|-------------|-------------|
| Many threads at same lock frame | Lock contention | MCP `get_parallel_stacks` or `dotnet-pstacks` + `syncblk` |
| App frozen, threads waiting on locks | Deadlock (circular dependency) | `syncblk` + `clrstack -all` + `dotnet-pstacks` |
| Thread pool count climbing (live) | Sync-over-async / starvation | `dotnet-counters collect` |
| High queue length, low completion rate | Thread pool exhaustion | `dotnet-counters collect` |
| Threads blocked on `SemaphoreSlim.Wait` | Async throttle saturation | `clrstack -all` + `dotnet-pstacks` |
| Many threads blocked at `Task.Result` / `.GetAwaiter().GetResult()` | Sync-over-async | `clrstack -all` + `dotnet-pstacks` |

## Cross-Skill Triggers

Pivot to **dotnet-memory-analysis** when:
- Thread pool starvation is confirmed AND memory is also growing → blocked threads may be accumulating objects
- Deadlock involves the finalizer thread → finalizer queue may be growing
- Many threads with large stacks → native stack memory consumption (visible in `eeheap` gap)
- Heap contains many `Task<T>` or async state machine objects → async leak pattern

Pivot to **windbg-bridge** skill when:
- Native lock details are needed (`!locks`, `!runaway` for CPU time per thread)
- Thread stacks include native frames that need further investigation

## When to Stop

The investigation is COMPLETE when any of these is true:
1. A deadlock cycle is proven with owner/waiter evidence from `syncblk` + `clrstack`
2. A contention source is identified with the lock object and owning code
3. Sync-over-async pattern is found with specific `Task.Result` / `.GetAwaiter().GetResult()` call sites
4. Counters do not support the suspected threading issue (report finding and stop)
5. The user confirms they have enough information to act
6. All diagnostic paths have been exhausted and findings are in the summary file

Always write the Conclusion section of the summary file before stopping.

## Common Errors

| Error Pattern | Cause | Resolution |
|---|---|---|
| "Access denied" or `E_ACCESSDENIED` | Insufficient permissions | Run terminal as Administrator |
| "DAC" or "mscordacwks" version mismatch | Dump from a different runtime version | Install the matching .NET runtime version |
| "Not a valid managed dump" | Native-only dump or minidump without heap | Need a full dump (`dotnet-dump collect` or Task Manager → Create dump file) |
| "Cannot attach to process" | Security software or elevation issue | Ask user to disable AV or run elevated; fall back to dump-based workflow |
| MCP tool returns empty results | No threads found or wrong dump format | Verify dump is a managed dump; try CLI fallback |
| `clrstack -all` output is huge (>5000 lines) | Many threads | Redirect to file: `> stacks.txt`, then read selectively by thread ID |
| `dotnet-trace` hangs or produces 0-byte file | Process already under debugger | Detach other debuggers first; or skip trace and use dump instead |

## Safety Guardrails

- **Always create the summary file** before the first diagnostic command — see MANDATORY section above
- **Never kill a process** without explicit user consent
- **Warn before `dotnet-dump collect`** on production — it freezes the process
- **Prefer live counters** over full dump for initial investigation
- **Do not attach to system-critical processes** (PID 0, PID 4, services)
- If unsure about a process identity, run `dotnet-dump ps` and confirm with the
  user before proceeding
