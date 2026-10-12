---
name: dotnet-memory-analysis
description: >-
  Analyze .NET application memory issues using CLI diagnostic tools. Use when
  investigating memory leaks, high memory usage, string duplication, growing
  heap, GC pressure, OOM, OutOfMemoryException, Gen2 collections, finalizer
  queue growth, or large object heap — on a live process, from a .dmp file,
  or from a .gcdump file. Works with dotnet-counters, dotnet-gcdump,
  dotnet-dump, and dotnet-dstrings.
---

# .NET Memory Analysis

Diagnose memory leaks, heap growth, string duplication, and GC pressure in .NET
applications using the standard Microsoft diagnostic CLI tools and custom
ClrMD-based tools — no code changes to the target application required.

## Tool Selection

When MCP servers are available, prefer them over CLI — they return structured
data that is easier to reason about and avoids raw text parsing.

| Goal | Prefer (MCP) | Fallback (CLI) |
|------|--------------|----------------|
| Duplicate strings | `get_duplicated_strings` (server: `user-dotnet-dstrings`) with `dumpPath` or `pid` | `dotnet-dstrings <dump>` |
| Heap stats | — | `dotnet-dump analyze -c "dumpheap -stat"` |
| GC root | — | `dotnet-dump analyze -c "gcroot <addr>"` |
| Heap overview | — | `dotnet-dump analyze -c "eeheap -gc"` |

MCP parameters for `get_duplicated_strings`:
- `dumpPath` or `pid` (mutually exclusive)
- `countThreshold` (default 128) — lower if output is empty
- `sizeThresholdKB` (default 100)
- `stringLengthLimit` (default 64)

## MANDATORY — Create Summary File First

**Before running ANY diagnostic command**, create the summary file named
`<date>-<time>_memory_analysis_SUMMARY.md` (e.g., `2026-05-18-1800_memory_analysis_SUMMARY.md`)
in the current working directory using the template in
[summary-template.md](summary-template.md). Update it after every meaningful
milestone. This is NOT optional.

## Before You Start — Collect Context

Before running any command, gather from the user:

1. **Target**: dump file path OR live process PID / name?
2. **Symptom**: what was observed? (e.g., "memory grows over time", "OOM after 2h")
3. **Production?** If yes, warn before any intrusive collection (`dotnet-dump collect`, `dotnet-gcdump collect`)
4. **Baseline available?** Has a `.gcdump` baseline already been captured?
5. **Dump size**: if a `.dmp` is provided, note file size — impacts command flags

## Prerequisites

The following tools must be installed as global .NET CLI tools:

```bash
dotnet tool install -g dotnet-dump
dotnet tool install -g dotnet-counters
dotnet tool install -g dotnet-gcdump
dotnet tool install -g dotnet-dstrings
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

**For dump files:**

```bash
dotnet-dump analyze <dump> -c "eeheap -gc" -c "exit"
```

This gives the managed/native memory split. Always run before `dumpheap -stat`.

Then run heap stats (use `-min 1024` for dumps > 500 MB):

```bash
dotnet-dump analyze <dump> -c "dumpheap -stat" -c "exit"
```

**For live processes:**

```bash
dotnet-counters collect -p <PID> --counters System.Runtime --format json --duration 00:00:30 -o counters.json
```

### Checkpoint 2 — Route by Finding

| Finding | Next Step |
|---------|-----------|
| One application type dominates heap by count or size | `dumpheap -mt <MT>` then `gcroot` on 2-3 instances → **Find Leak Roots** |
| `System.String` dominates | Use MCP `get_duplicated_strings` (or `dotnet-dstrings`) → **Duplicate Strings** |
| `System.Byte[]` dominates | Check buffer pool / stream usage via `gcroot` |
| Growth spread across many unrelated types | Look for `List<T>`, `Dictionary<K,V>`, `ConcurrentDictionary<K,V>` as holding collection → `gcroot` on the collection |
| Large finalizer queue | `finalizequeue` → check "Ready for finalization" count |
| Heap is small but process memory is large | Native leak — `eeheap` shows gap. Use WinDbg `!address -summary` if available |
| Gen2 GCs frequent (live) | Long-lived object pressure → escalate to GC dump comparison |

---

## Live Process Investigation

### Identify the Target

```bash
dotnet-dump ps
```

Lists running .NET processes with PID and name. Confirm the target PID.

### Collect Memory Counters

```bash
dotnet-counters collect -p <PID> --counters System.Runtime --format json --duration 00:00:30 -o counters.json
```

Key counters to check in the output:
- `gc-heap-size` — growing steadily = possible leak
- `gen-0-gc-count` / `gen-1-gc-count` / `gen-2-gc-count` — frequent Gen2 GCs
  indicate long-lived object pressure
- `exception-count` — unexpected exceptions

### Detect Leaks with GC Dump Comparison

`dotnet-gcdump` captures a lightweight snapshot of the managed heap (type names,
counts, and sizes — no field values or raw memory) by triggering a GC then
walking the heap via EventPipe. Much cheaper than a full `dotnet-dump collect`
and does **not** freeze the process for an extended time.

**Step 1 — Capture a baseline snapshot:**

```bash
dotnet-gcdump collect -p <PID> -o baseline.gcdump
```

**Step 2 — Reproduce the suspected leak** (ask the user to run the scenario; wait ~30s or as user directs).

**Step 3 — Capture a second snapshot:**

```bash
dotnet-gcdump collect -p <PID> -o after.gcdump
```

**Step 4 — Compare the two snapshots:**

```bash
dotnet-gcdump report after.gcdump -b baseline.gcdump
```

IF top delta type is an application type → escalate to full dump, then `gcroot`.
IF top delta is `System.String` → run `dotnet-dstrings` (or MCP).
IF growth is spread across many types → look for a holding collection.
IF no significant delta → leak may be native, not managed.

Confirm findings by repeating the capture/compare cycle; genuine leaks grow
every time.

### Escalate to Full Dump

If GC dump comparison identifies suspicious types but you need object-level
detail (field values, GC root chains), capture a full dump:

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

### Heap Overview (eeheap)

Always start here to understand the native/managed memory split:

```bash
dotnet-dump analyze <dump> -c "eeheap -gc" -c "exit"
```

If WinDbg bridge is available, also run `!address -summary` for native details.

### Heap Analysis

**Get heap summary by type** — for dumps > 500 MB, always use `-min 1024`:

```bash
dotnet-dump analyze <dump> -c "dumpheap -stat" -c "exit"
dotnet-dump analyze <dump> -c "dumpheap -stat -min 1024" -c "exit"
```

**Parsing `dumpheap -stat` output:**

```
              MT    Count    TotalSize Class Name
00007ff8a1234560       42      134,400 MyApp.Models.Customer    <-- app type: investigate
00007ff89abcdef0   12,847    1,234,567 System.String            <-- check dstrings if >10% of heap
00007ff89abc1230    1,203      456,789 System.Byte[]            <-- check buffers if large
```

- Sort by `TotalSize` (rightmost numeric column) descending
- The `MT` value (first column) is needed for `dumpheap -mt <MT>`
- Prioritize application-namespace types (not `System.*` or `Microsoft.*`) in the top 20
- Ignore types with `Count < 10` unless `TotalSize` is very large

**List instances of a suspicious type:**

```bash
dotnet-dump analyze <dump> -c "dumpheap -mt <MethodTable>" -c "exit"
```

Pick one or two object addresses from the output for root analysis.

**Inspect a specific object:**

```bash
dotnet-dump analyze <dump> -c "dumpobj <address>" -c "exit"
```

**Check the finalizer queue:**

```bash
dotnet-dump analyze <dump> -c "finalizequeue" -c "exit"
```

**Parsing `finalizequeue` output:**
- Look for "Ready for finalization" count — a large number means objects with
  finalizers are piling up and not being disposed
- IF the finalizer thread appears blocked → pivot to `dotnet-threads-analysis`
  skill to investigate the finalizer thread's call stack

### Find Leak Roots

Once you have a suspicious object address:

```bash
dotnet-dump analyze <dump> -c "gcroot <address>" -c "exit"
```

**Parsing `gcroot` output:**
The output shows chains: `root -> ... -> target`. Look for:
- **Static field roots** (`HNDTYPE_STRONG` / static): a static collection holding references indefinitely
- **Event handler roots**: subscribers never unsubscribed
- **Timer/Task roots**: background work keeping objects alive
- **Pinned handles** (`HNDTYPE_PINNED`): objects pinned in memory

```
Thread abc1:
    0000abcd1234 -> 0000abcd5678 MyApp.Cache._items (static) -> ... -> target
                     ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
                     This static field is the root cause
```

If multiple objects of the same type are leaking, check 2-3 addresses to
confirm they share a common root pattern.

When a culprit is found, use `dumpmt`, `dumpclass`, and `dumpil` to inspect the
implementation. Alternatively, dump the assembly with `.writemem` on the module
and decompile with `ilspycmd` (`dotnet tool install ilspycmd -g` if needed).

### Duplicate Strings

**Prefer MCP** if `user-dotnet-dstrings` server is available:
Call `get_duplicated_strings` with `dumpPath` parameter.

**CLI fallback:**

```bash
dotnet-dstrings <dump>
```

Outputs per-generation heap statistics (DupSize%, HeapSize%) followed by a list
of duplicated strings sorted by total wasted size. Default thresholds:
count >= 128, cumulated size >= 100 KB, display length <= 64 chars.

Adjust thresholds if duplication is low:

```bash
dotnet-dstrings <dump> --dup -c 32 -s 10
```

Use `--gen` for generation stats only, `--dup` for duplicates only.

**Interpretation:**
- High DupSize% in Gen2 → long-lived duplicate strings (caching / config issue)
- High DupSize% in Gen0/Gen1 → transient duplicates (allocation pattern issue)
- Remediation: `string.Intern()` for known-finite sets, deduplication at
  ingestion, `StringPool`, or caching layers

---

## Diagnosis Patterns

| Symptom | Likely Cause | Key Command |
|---------|-------------|-------------|
| Heap growing steadily (live) | Memory leak | `dotnet-gcdump` compare |
| Heap dominated by one type (dump) | Memory leak / missing disposal | `dumpheap -stat` then `gcroot` |
| Massive System.String count | String duplication | MCP `get_duplicated_strings` or `dotnet-dstrings` |
| Large finalizer queue | Missing Dispose calls | `finalizequeue` |
| Frequent Gen2 GCs | Long-lived object pressure | `dotnet-counters` + `dumpheap -stat` |
| Process memory >> managed heap | Native memory leak | `eeheap` + WinDbg `!address -summary` |

## Cross-Skill Triggers

Pivot to **dotnet-threads-analysis** when:
- `finalizequeue` shows a large backlog AND the finalizer thread appears blocked
- Heap contains many `Task<T>` or `CancellationTokenSource` objects → possible sync-over-async leak
- Thread stacks are consuming significant native memory (visible in `eeheap` gap)

Pivot to **windbg-bridge** skill when:
- Native memory breakdown is needed (`!address -summary`, `!heap`)
- `eeheap` shows large gap between process memory and managed heap

## When to Stop

The investigation is COMPLETE when any of these is true:
1. A root cause is identified with a clear `gcroot` chain from leaked objects to a specific static field, event handler, or timer
2. The user confirms the finding explains their observed symptoms
3. Two full capture/compare cycles show no growth (no leak confirmed — report this)
4. The investigation hits a native memory boundary that requires WinDbg/LLDB (escalate to user)
5. All diagnostic paths have been exhausted and findings are in the summary file

Always write the Conclusion section of the summary file before stopping.

## Common Errors

| Error Pattern | Cause | Resolution |
|---|---|---|
| "Access denied" or `E_ACCESSDENIED` | Insufficient permissions | Run terminal as Administrator |
| "DAC" or "mscordacwks" version mismatch | Dump from a different runtime version | Install the matching .NET runtime version |
| "Not a valid managed dump" | Native-only dump or minidump without heap | Need a full dump (`dotnet-dump collect` or Task Manager → Create dump file) |
| "Cannot attach to process" | Security software or elevation issue | Ask user to disable AV or run elevated; fall back to dump-based workflow |
| MCP tool returns empty results | Thresholds too high for this dump | Lower `countThreshold` to 32 and `sizeThresholdKB` to 10, retry |
| `dumpheap -stat` output is huge (>5000 lines) | Very large dump | Use `dumpheap -stat -min 1024` to filter small objects |

## Safety Guardrails

- **Always create the summary file** before the first diagnostic command — see MANDATORY section above
- **Never kill a process** without explicit user consent
- **Warn before `dotnet-dump collect`** on production — it freezes the process
- `dotnet-gcdump collect` is lightweight but still triggers a GC; warn on
  latency-sensitive production systems
- **Prefer GC dump comparison** over full dump for initial live investigation
- **Do not attach to system-critical processes** (PID 0, PID 4, services)
- If unsure about a process identity, run `dotnet-dump ps` and confirm with the
  user before proceeding
