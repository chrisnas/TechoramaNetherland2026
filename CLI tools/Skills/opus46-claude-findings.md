# Skill Improvement Findings — Claude Sonnet 4.6

Analysis of `dotnet-memory-analysis` and `dotnet-threads-analysis` skills
for agentic workflows in Cursor / Claude Code.

---

## What works well

- The `-c` flag pattern for `dotnet-dump analyze` solves the core agentic problem
  (interactive tool → non-interactive).
- SUMMARY.md discipline provides an audit trail.
- Safety guardrails prevent destructive actions without user consent.
- Diagnosis pattern tables give the agent a quick symptom-to-command mapping.

---

## Improvements by impact

### 1. Prefer MCP tools over CLI when available (High impact / Low effort)

Both skills have MCP counterparts already registered:

- `mcp__dstrings__get_duplicated_strings` → replaces `dotnet-dstrings <dump>`
- `mcp__pstacks__get_parallel_stacks` → replaces `dotnet-pstacks <dump>`

MCP tools return structured data (typed objects) instead of raw text. The agent
does not need to parse output — it gets values it can reason over directly.

**Proposed addition to each skill:**

> If MCP tools are available, prefer `mcp__dstrings__get_duplicated_strings` /
> `mcp__pstacks__get_parallel_stacks` over the CLI equivalents. Use the CLI only
> as fallback when MCP is not configured.

---

### 2. Replace narrative prose with decision trees (High impact / Medium effort)

Both skills read as documentation, not algorithms. Agents must infer branching
logic from narrative text. Example gap in the memory skill: "start broad, then
narrow" — but when exactly should the agent escalate from gcdump to full dump?

**Proposed explicit branch:**

```
IF heap is growing live AND gcdump delta shows > 10% growth on one type
  THEN escalate to dotnet-dump collect (warn user first)
ELSE IF growth is spread across many unrelated types
  THEN look for a holding collection (List<T>, Dictionary<K,V>) before escalating
```

---

### 3. Add an explicit input-gathering section (Medium impact / Low effort)

Neither skill defines what context to collect from the user before running any
command. Without this, agents guess or ask piecemeal mid-investigation.

**Proposed section (top of each skill):**

```
## Before You Start — Collect Context

Ask the user for:
1. Live process (PID or process name) OR existing dump file path?
2. Symptom observed (e.g., "memory grows over time", "app froze after 2 hours")
3. Has a baseline already been captured? (for gcdump comparison, memory skill)
4. Is this a production system? (determines whether freezing the process is acceptable)
```

---

### 4. Add output parsing hints per command (Medium impact / Medium effort)

Each command section describes purpose but not which output patterns trigger the
next step. Agents stop and ask the user instead of navigating decisively.

**Example addition for `dumpheap -stat`:**

> **Parse for:** Any type where `TotalSize > 50 MB` or `Count > 100,000`.
> Prioritize types not from `System.*`. If `System.String` dominates, defer to
> the Duplicate Strings section. If multiple unrelated types are growing equally,
> look for a holding collection first.

---

### 5. Mark parallelizable steps explicitly (Low-Medium impact / Low effort)

Several commands are independent and can run in parallel. The threads skill
deadlock sequence already batches `syncblk`, `threads`, and `clrstack -all`
into one invocation, but then says "run separately: `dotnet-pstacks`" with no
reason for the separation.

**Proposed annotation:**

> **Parallelizable:** Launch `dotnet-pstacks <dump>` in a second parallel shell
> while `dotnet-dump analyze` is running — they read the same file independently.

---

### 6. Add explicit completion criteria (Medium impact / Low effort)

Neither skill defines a "done" condition. Agents can loop indefinitely or keep
escalating tool invasiveness.

**Proposed section:**

```
## When to Stop

The investigation is complete when:
- A root cause is identified with a GC root chain or lock cycle as evidence, OR
- All diagnostic paths have been exhausted and findings are in SUMMARY.md, OR
- The user confirms they have enough information to act.

Do not collect additional dumps or run further commands beyond what is needed
to answer the question.
```

---

### 7. Guard against output size / context window saturation (Medium impact / Low effort)

For large dumps, `dumpheap -stat` and `clrstack -all` can return thousands of
lines. The `-min 1024` flag is mentioned only parenthetically in the memory skill.

**Proposed rule:**

> If the dump is > 2 GB, always use `dumpheap -stat -min 1024`.
> For `clrstack -all` on processes with > 100 threads, redirect output to a file
> and read only the top 200 lines initially, then drill into specific thread IDs.

---

## Summary table

| Improvement | Impact | Effort |
|---|---|---|
| Prefer MCP tools for structured output | High | Low |
| Decision trees replacing narrative | High | Medium |
| Input-gathering section | Medium | Low |
| Output parsing hints per command | Medium | Medium |
| Completion criteria | Medium | Low |
| Output size / context window guard | Medium | Low |
| Parallelism annotations | Low-Medium | Low |

---

## Recommended implementation order

1. Add MCP tool preference note (one sentence each, immediate win).
2. Add input-gathering and completion-criteria sections (low effort, high
   structural value).
3. Add output size guards (prevents context overflow on real investigations).
4. Refactor investigation workflows into explicit decision trees (largest
   authoring effort, highest long-term agentic reliability).
5. Add per-command output parsing hints alongside the decision trees.
