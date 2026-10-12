# Skills Improvement Analysis for Agentic Workflows

**Date:** 2026-05-24
**Analyzed skills:** `dotnet-memory-analysis`, `dotnet-threads-analysis`
**Associated MCP servers:** `user-dotnet-dstrings`, `user-dotnet-pstacks`

---

## 1. Bridge the Gap Between Skills and MCP Servers

The biggest improvement opportunity. MCP servers exist (`user-dotnet-dstrings` with `get_duplicated_strings`, `user-dotnet-pstacks` with `get_parallel_stacks`) alongside MCP prompts (`find_deadlocks`, `analyze_thread_pool_health`, `diagnose_threading_issues`, `analyze_string_duplication`, `suggest_string_optimizations`), but **neither skill mentions them**.

Both skills tell the agent to run `dotnet-dstrings` and `dotnet-pstacks` as shell commands instead of using the structured MCP tools.

**Recommendation:** Add a section to each skill:

```markdown
## Preferred Tool Access

When an MCP server is available, PREFER it over shell commands:
- Use the `get_duplicated_strings` MCP tool (server: `user-dotnet-dstrings`)
  instead of `dotnet-dstrings <dump>`. It returns structured data.
- Use the `get_parallel_stacks` MCP tool (server: `user-dotnet-pstacks`)
  instead of `dotnet-pstacks <dump>`. It returns structured data.
- Fall back to shell commands only when the MCP server is not available.
```

MCP tools return structured data the agent can reason about directly, whereas shell output requires parsing raw text.

---

## 2. Add Explicit Decision Trees Instead of Prose

Both skills describe workflows as linear narratives. Agents work better with explicit branching logic.

**Recommendation:** Replace interpretation prose with decision trees:

```markdown
## Decision: After GC dump comparison

IF top delta type is an application type:
  -> Run `dumpheap -mt <MT>` then `gcroot` on 2-3 instances
  -> GOTO "Find Leak Roots"

IF top delta is System.String or System.Byte[]:
  -> Run `dotnet-dstrings` (or MCP `get_duplicated_strings`) first
  -> IF DupSize% > 10%: likely duplication problem -> GOTO "Duplicate Strings"
  -> ELSE: strings held by a leaking collection -> look for container types
     in dumpheap that hold strings -> GOTO "Find Leak Roots" on the container

IF growth is spread across many unrelated types:
  -> Look for `List<T>`, `Dictionary<K,V>`, `ConcurrentDictionary<K,V>`
     in dumpheap -stat -> the collection IS the root cause
  -> GOTO "Find Leak Roots" on the collection

IF no significant delta:
  -> The leak may be native, not managed -> suggest native memory profiling
```

This eliminates ambiguity about what the agent should do next at each step.

---

## 3. Add Output Parsing Patterns

The skills tell the agent to run commands but don't explain how to parse the output. `dumpheap -stat`, `syncblk`, `threads`, and `gcroot` all produce columnar text that LLMs can struggle with.

**Recommendation:** Add parsing hints with annotated examples:

```markdown
### Example: dumpheap -stat output (annotated)

              MT    Count    TotalSize Class Name
00007ff8a1234560       42      134,400 MyApp.Models.Customer    <-- investigate
00007ff89abcdef0   12,847    1,234,567 System.String            <-- check dstrings
00007ff89abc1230    1,203      456,789 System.Byte[]            <-- if large, check buffers

- Sort by TotalSize (last numeric column) descending
- The MT value (first column) is needed for `dumpheap -mt <MT>`
- Ignore types with Count < 10 unless TotalSize is very large
- Flag any application-namespace types (not System.* or Microsoft.*)
  in the top 20 by size
```

Even brief examples dramatically improve the agent's ability to identify relevant lines in real output.

---

## 4. Add Cross-Skill Triggers

Memory and threading issues often intersect, but the skills are completely siloed.

**Recommendation:** Add pivot conditions in each skill.

In the memory skill:
```markdown
## Cross-Skill Triggers
- IF `finalizequeue` shows a large backlog AND finalizer thread appears blocked
  -> pivot to the dotnet-threads-analysis skill to investigate the
     finalizer thread's call stack
- IF heap contains many `Task<T>` or `CancellationTokenSource` objects
  -> possible async leak; pivot to threads skill for sync-over-async check
```

In the threads skill:
```markdown
## Cross-Skill Triggers
- IF thread pool starvation is confirmed AND memory is also growing
  -> pivot to dotnet-memory-analysis skill; blocked threads may be
     accumulating objects
- IF deadlock involves a finalizer thread
  -> pivot to memory skill; finalizer queue may be growing
```

---

## 5. Add Prerequisite Verification as a Mandatory First Step

Both skills mention verifying tools but don't enforce it as step 1. An agent that skips this wastes time on cryptic errors.

**Recommendation:** Make verification the explicit entry point:

```markdown
## Step 0 — Verify Environment (MANDATORY, run before anything else)

dotnet tool list -g

Check that the output contains ALL required tools.

IF any tool is missing:
  -> Install it: `dotnet tool install -g <tool-name>`
  -> IF install fails (no SDK): STOP and tell the user which tools are missing

Also verify MCP availability:
  -> Check if `user-dotnet-dstrings` / `user-dotnet-pstacks` MCP servers
     are accessible; prefer MCP tools when available.
```

---

## 6. Add Error Handling Guidance

Neither skill mentions what to do when commands fail. Common failures include access denied, DAC version mismatch, corrupted dumps, and 32-bit vs 64-bit mismatch.

**Recommendation:**

```markdown
## Common Errors

| Error Pattern                    | Cause                           | Resolution                                      |
|----------------------------------|---------------------------------|-------------------------------------------------|
| "Access denied" / E_ACCESSDENIED | Insufficient permissions        | Run terminal as Administrator                   |
| "DAC" or "mscordacwks" errors    | Dump from different runtime ver | Install matching runtime or use `--conversion`  |
| "Not a managed dump"             | Native-only or wrong format     | Need a full dump, not a minidump                |
| "Cannot attach to process"       | Security software blocking      | Ask user to disable or use dump-based workflow   |
```

This prevents the agent from retrying the same failing command in a loop.

---

## 7. Reduce Token Cost with Lazy Loading

Both skills embed the entire summary file template inline (~30 lines each). For an agent that loads the full SKILL.md into context, this is expensive.

**Recommendation:**
- **Option A:** Move the summary template to a separate file (e.g., `SUMMARY_TEMPLATE.md`) next to `SKILL.md`, and instruct the agent to read it only when creating the summary file.
- **Option B:** Compress the template into a terse instruction: "Create summary using the template in `./SUMMARY_TEMPLATE.md`".

This frees up context window space for actual investigation output.

---

## 8. Add Explicit Stop Conditions

The skills describe investigation steps but don't tell the agent when it's done. An agentic workflow needs clear termination criteria.

**Recommendation:**

```markdown
## When to Stop

The investigation is COMPLETE when any of these is true:
1. A root cause is identified with a clear gcroot chain from leaked object
   to a specific static field, event handler, or timer
2. The user confirms the finding explains their observed symptoms
3. Two full capture/compare cycles show no growth (no leak confirmed)
4. The investigation hits a native memory boundary that requires WinDbg/LLDB
   (escalate to user)

Always write the Conclusion section of the summary file before stopping.
```

---

## 9. Consider a Unified Dispatcher Skill

Today, the user (or agent) must know which skill to invoke. A lightweight dispatcher at the top of a combined skill or a separate triage skill could auto-route based on symptoms:

```markdown
## Triage — Which workflow?

| Symptom keywords                             | Workflow                                    |
|----------------------------------------------|---------------------------------------------|
| "memory", "leak", "heap", "OOM", "strings"   | -> Memory Analysis                          |
| "hang", "frozen", "deadlock", "slow", "threads" | -> Thread Analysis                       |
| "high CPU" + "growing memory"                 | -> Start with Threads, then pivot to Memory |
| "unresponsive"                                | -> Start with Threads (deadlock check)      |
```

---

## Impact Summary

| Improvement                  | Effort | Impact on Agent Accuracy |
|------------------------------|--------|--------------------------|
| MCP integration in skills    | Low    | **Very High**            |
| Decision trees               | Medium | **High**                 |
| Output parsing patterns      | Medium | **High**                 |
| Cross-skill triggers         | Low    | **Medium**               |
| Prerequisite verification    | Low    | **Medium**               |
| Error handling               | Low    | **Medium**               |
| Token cost reduction         | Low    | **Low-Medium**           |
| Example outputs              | Medium | **High**                 |
| Stop conditions              | Low    | **Medium**               |
| Unified dispatcher           | Low    | **Medium**               |

The highest-leverage changes are **(1) bridging MCP tools into the skills**, **(2) decision trees**, and **(3) output parsing guidance**. Those three alone would significantly improve how reliably an agent can execute these investigation workflows end-to-end.
