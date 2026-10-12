# Thread Investigation Summary

**Date:** YYYY-MM-DD
**Target:** <process name / dump file path>
**Symptom:** <initial problem description>

## Investigation Steps

### Step N — <brief description>

**Command:**
```
<exact command line>
```

**Result:**
<relevant output excerpt — thread counts, parallel stacks groups, sync block
owners, deadlock cycles, counter snapshots, etc.>

**Interpretation:**
<what the result means for the investigation>

**Next action:**
<what will be done next and why>

<!-- repeat for each step -->

## Commands and Tools Used

| # | Type | Exact Invocation | Purpose |
|---|------|------------------|---------|
| 1 | CLI | `dotnet-dump ps` | Identify target process |
| 2 | MCP | `get_parallel_stacks(dumpPath: "dump.dmp", threadIdLimit: 4)` | Parallel stacks overview |
| 3 | CLI | `dotnet-dump analyze dump.dmp -c "syncblk" -c "threads" -c "exit"` | Lock ownership and thread list |
| ... | ... | ... | ... |

Type is **CLI** (shell command) or **MCP** (MCP tool call with server name and parameters).
**Every command and tool call executed during the investigation MUST appear in this table — no exceptions.**

## Conclusion

**Root cause:** <identified cause or remaining candidates>
**Evidence chain:** <which steps and results led to the diagnosis>
**Recommended remediation:** <concrete fix suggestions>
