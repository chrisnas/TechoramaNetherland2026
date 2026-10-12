# Agent Skill Improvement Findings — dotnet-memory-analysis & dotnet-threads-analysis

Analysis of the two skills in `Skills/dotnet-memory-analysis/` and `Skills/dotnet-threads-analysis/` for usage by Cursor or Claude agents.

---

## What Works Well Today

Both skills already include several agent-friendly patterns:

1. **Non-interactive SOS** — The `-c "command" -c "exit"` pattern is essential; without it, `dotnet-dump analyze` blocks agents.
2. **Symptom → command routing** — The diagnosis tables give the agent a fast decision shortcut.
3. **Audit trail** — The `SUMMARY.md` template matches how agents should reason incrementally and report back.
4. **Safety guardrails** — Production warnings and "confirm PID" rules reduce bad autonomous actions.
5. **Escalation ladders** — Live counters → lightweight snapshot → full dump is the right cost/risk progression.

The existing `dumps/2026-05-18-1800_memory_analysis_SUMMARY.md` demonstrates the workflow produces good results when followed.

---

## Gaps for Agentic Workflows

### 1. MCP Tools Exist but the Skills Ignore Them

Cursor already exposes MCP tools for the custom ClrMD-based tools:

- `mcp_dotnet-dstrings_get_duplicated_strings`
- `mcp_dotnet-pstacks_get_parallel_stacks`

Yet both skills tell the agent to run shell commands (`dotnet-dstrings`, `dotnet-pstacks`). That is slower, noisier, and harder to parse than structured MCP output.

**Improvement:** Add an explicit "prefer MCP when available" section at the top of each skill, with CLI as fallback.

### 2. Interactive Commands Are Agent-Hostile

These appear in both skills:

| Command | Problem for agents |
|---------|-------------------|
| `dotnet-counters monitor` | Long-running, requires `q` to stop |
| `dotnet-trace collect` | Long-running, needs duration control |

Agents either hang, get truncated output, or waste turns guessing when to stop.

**Improvement:** Replace with bounded, one-shot alternatives:

```bash
dotnet-counters collect -p <PID> --counters System.Runtime --format json --duration 00:00:30 -o counters.json
dotnet-trace collect -p <PID> --duration 00:00:30 -o trace.nettrace
```

### 3. Too Much Duplicated Context Across Both Skills

Shared sections (~40% overlap):

- Prerequisites
- Non-interactive SOS technique
- `dotnet-dump ps` / safety guardrails
- Full `SUMMARY.md` template

When both skills are installed, the agent pays the token cost twice.

**Improvement:** Extract a shared `dotnet-diagnostics-common.md` and keep each `SKILL.md` focused on its domain workflow.

### 4. No Upfront Routing Decision Tree

An agent receiving "my app uses too much memory and hangs" must guess which skill to use first. The skills assume you already know it is memory *or* threads.

**Improvement:** Add a short triage block at the top:

```
User has dump file?      → dump workflow
User has live process?   → live workflow
App frozen/unresponsive? → threads skill first, then memory
Heap growing over time?  → memory skill first
```

### 5. Missing Progressive Disclosure

At ~300 lines each, they are within limits, but the full `SUMMARY.md` template (~50 lines) loads on every invocation even when the user just wants "find the leak in this dump."

**Improvement (Cursor best practice):**

```
dotnet-memory-analysis/
├── SKILL.md              # routing + first 3 commands
├── dump-workflow.md      # full dump investigation
├── live-workflow.md      # gcdump comparison, counters
├── summary-template.md   # audit trail format
└── scripts/
    └── check-tools.ps1   # verify prerequisites
```

### 6. Frontmatter Not Tuned for Discovery

Compared to Cursor's create-skill guidance:

- Descriptions are good but could add more trigger terms (`OOM`, `OutOfMemoryException`, `Task.Wait`, `deadlock`, `.dmp`, `.gcdump`)
- No `disable-model-invocation` — if you want explicit invocation only, add it; if you want auto-discovery on "analyze this dump", omit it intentionally

---

## Four Improvement Strategies (Different Trade-offs)

### Strategy A — MCP-First (Highest Agent Efficiency)

Make MCP the primary interface; CLI is fallback when MCP is not configured.

**Memory skill addition:**

```markdown
## Prefer MCP tools (when configured)

If `mcp_dotnet-dstrings_get_duplicated_strings` is available:
- Call it with `dumpPath` or `pid` — do NOT shell out to dotnet-dstrings
- Default thresholds are fine; lower `countThreshold` if output is empty

If MCP unavailable → use CLI: `dotnet-dstrings <dump>`
```

**Threads skill addition:**

```markdown
## Prefer MCP tools

If `mcp_dotnet-pstacks_get_parallel_stacks` is available:
- Call with `dumpPath` or `pid`, `threadIdLimit: 4` for overview
- Use `-1` only when drilling into a specific stack group

If MCP unavailable → `dotnet-pstacks <dump>`
```

**Why this helps:** Structured JSON/text output, no shell parsing, fewer failed commands, works identically in Cursor and Claude Code if MCP is configured in `.cursor/mcp.json` / `.mcp.json`.

---

### Strategy B — Scripted "Playbooks" (Most Reliable, Lowest Token Cost)

Add small scripts the agent runs instead of improvising command chains:

```
scripts/
├── heap-triage.ps1      # eeheap + dumpheap -stat (with -min for large dumps)
├── leak-roots.ps1       # dumpheap -mt + gcroot for top type
├── thread-deadlock.ps1  # syncblk + threads + clrstack -all
└── check-prereqs.ps1    # dotnet tool list -g, exit 1 if missing
```

The skill becomes:

```markdown
## Step 1 — Run triage
powershell scripts/heap-triage.ps1 -DumpPath dumps\leaky.dmp
```

**Why this helps:** Deterministic output, fewer hallucinated SOS flags, one turn instead of three.

---

### Strategy C — Decision-Tree SKILL.md (Best for Ambiguous Requests)

Restructure the body around agent checkpoints:

```markdown
## Agent workflow

### Checkpoint 0 — Inputs
- [ ] Dump path OR PID confirmed with user
- [ ] Create SUMMARY.md (see summary-template.md)

### Checkpoint 1 — Triage (always)
Run ONE of:
- Dump: `eeheap` then `dumpheap -stat`
- Live: `dotnet-counters collect` (30s snapshot, not monitor)

### Checkpoint 2 — Route by finding
| Finding | Next |
|---------|------|
| One type dominates heap | gcroot workflow |
| System.String high | MCP dstrings |
| Threads blocked on Monitor | switch to dotnet-threads-analysis |
| Gen2 growing (live) | gcdump compare |

### Stop condition
Stop when root cause + evidence chain + remediation are in SUMMARY.md Conclusion.
```

**Why this helps:** Agents loop better with explicit stop conditions and branching than with linear documentation.

---

### Strategy D — Split + Compose (Best for Demo Repo / Conference)

Keep two domain skills but add a thin orchestrator:

```
dotnet-diagnostics/
├── SKILL.md                    # triage only, links to sub-skills
├── dotnet-memory-analysis/     # memory-specific
└── dotnet-threads-analysis/    # thread-specific
```

The parent skill handles:

- "Which sub-skill?"
- Shared prerequisites check
- When to invoke `windbg-bridge` for native memory (`eeheap` note in memory skill)

**Why this helps:** Matches how Cursor loads skills — one lightweight router, heavy content loaded only when needed.

---

## Skill-Specific Improvements

### Memory Analysis

| Current | Suggested |
|---------|-----------|
| `eeheap` note buried after SOS section | Move to **Step 0** — always run before `dumpheap -stat` |
| WinDbg mentioned vaguely | Link to `windbg-bridge` skill: "for native breakdown, use bridge + `!address -summary`" |
| `dotnet-dstrings` CLI only | MCP-first with parameter guidance |
| Large dump handling (`-min 1024`) | Promote to default for dumps > 500 MB |
| gcdump compare workflow | Add explicit "agent cannot wait for user to reproduce" note — ask user to reproduce, then continue |

### Thread Analysis

| Current | Suggested |
|---------|-----------|
| No native/thread-stack memory context | Cross-link memory skill for "high thread count + high memory" |
| Deadlock steps are manual | Provide one combined SOS command block as **the** default path |
| `dotnet-pstacks` CLI only | MCP-first |
| `dotnet-trace` without duration | Add `--duration 00:00:30` and say when to skip trace entirely |
| Missing `eeheap`/thread stack note | Optional: "many threads → check native stack memory via WinDbg" |

---

## Cursor vs Claude: Practical Differences

| Concern | Cursor | Claude Code |
|---------|--------|---------------|
| MCP config | `.cursor/mcp.json` | `.mcp.json` or `claude mcp add` |
| Skill location | `~/.cursor/skills/` or project `.cursor/skills/` | `~/.claude/skills/` |
| Long-running commands | Background shell + `Await` | Similar, but MCP avoids this |
| Skill discovery | Description in frontmatter | Same pattern |

**Recommendation:** Document MCP setup once in a shared `mcp-setup.md` snippet both skills reference, so the same skill pack works in both environments.

---

## Suggested Priority Order

If you want maximum impact with minimal rewrite:

1. **Add MCP-first sections** — immediate win, zero structural change
2. **Replace `monitor` with `collect --duration`** — stops agents from hanging
3. **Extract SUMMARY template** to `summary-template.md` — saves ~50 tokens per load
4. **Add triage decision tree** at top of each SKILL.md
5. **Add `scripts/check-prereqs.ps1`** — fail fast before a long investigation
6. **Optional:** parent `dotnet-diagnostics` router skill for combined scenarios

---

## Example: Minimal MCP-First Patch (Memory Skill)

```markdown
## Tool selection

| Goal | Prefer (MCP) | Fallback (CLI) |
|------|--------------|----------------|
| Duplicate strings | `mcp_dotnet-dstrings_get_duplicated_strings` | `dotnet-dstrings <dump>` |
| Parallel stacks | *(threads skill)* | `dotnet-pstacks <dump>` |
| Heap stats | — | `dotnet-dump analyze -c "dumpheap -stat"` |
| GC root | — | `dotnet-dump analyze -c "gcroot <addr>"` |

Always prefer MCP when the tool appears in the agent's available tools list.
```

---

## Bottom Line

The skills are already strong as diagnostic manuals. The biggest agentic gains come from:

1. Routing MCP tools already built (`dotnet-dstrings`, `dotnet-pstacks`)
2. Eliminating interactive/long-running CLI patterns
3. Restructuring around checkpoints and progressive disclosure rather than linear prose
