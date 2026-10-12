# Memory Investigation Summary

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
<relevant output excerpt — top types, GC dump deltas, root chains, etc.>

**Interpretation:**
<what the result means for the investigation>

**Next action:**
<what will be done next and why>

<!-- repeat for each step -->

## Commands and Tools Used

| # | Type | Exact Invocation | Purpose |
|---|------|------------------|---------|
| 1 | CLI | `dotnet-dump ps` | Identify target process |
| 2 | CLI | `dotnet-gcdump collect -p 1234 -o baseline.gcdump` | Baseline heap snapshot |
| 3 | MCP | `get_duplicated_strings(dumpPath: "leaky.dmp", countThreshold: 128)` | Analyze string duplication |
| ... | ... | ... | ... |

Type is **CLI** (shell command) or **MCP** (MCP tool call with server name and parameters).
**Every command and tool call executed during the investigation MUST appear in this table — no exceptions.**

## Conclusion

**Root cause:** <identified cause or remaining candidates>
**Evidence chain:** <which steps and results led to the diagnosis>
**Recommended remediation:** <concrete fix suggestions>
