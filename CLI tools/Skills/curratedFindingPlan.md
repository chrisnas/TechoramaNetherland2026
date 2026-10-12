# Curated Findings Plan — Skill Improvements

**Date:** 2026-05-24
**Sources:** opus46-cursor, opus46-claude, composer25-cursor, gtp55
**Skills:** `dotnet-memory-analysis`, `dotnet-threads-analysis`

---

## Consensus Map

| # | Improvement | Opus4.6-Cursor | Opus4.6-Claude | Composer2.5 | GPT-5.5 | Consensus |
|---|---|---|---|---|---|---|
| 1 | MCP-first over CLI | Yes | Yes | Yes | Yes | **4/4** |
| 2 | Replace interactive commands with bounded | - | - | Yes | Yes | **2/4** |
| 3 | Decision trees replacing prose | Yes | Yes | Yes | Yes | **4/4** |
| 4 | Progressive disclosure (externalize template) | Yes | - | Yes | Yes | **3/4** |
| 5 | Input-gathering section | - | Yes | - | Yes | **2/4** |
| 6 | Explicit stop conditions | Yes | Yes | Yes | Yes | **4/4** |
| 7 | Frontmatter trigger terms | - | - | Yes | - | **1/4** |
| 8 | Output parsing hints | Yes | Yes | Yes | - | **3/4** |
| 9 | Cross-skill triggers | Yes | - | Yes | - | **2/4** |
| 10 | Large-dump guard (memory) | Yes | Yes | Yes | - | **3/4** |
| 11 | Error handling tables | Yes | - | - | - | **1/4** |

---

## Action Items per Skill

### Memory Skill (`dotnet-memory-analysis`)

| ID | Action | Priority | Effort | Impact |
|---|---|---|---|---|
| A1 | Add MCP-first tool selection table | 1 | Low | Very High |
| A2 | Replace `dotnet-counters monitor` with bounded `collect --duration` | 2 | Low | High |
| A3 | Restructure as decision tree with checkpoints | 3 | Medium | High |
| A4 | Extract summary template to `summary-template.md` | 4 | Low | Medium |
| A5 | Add input-gathering block | 5 | Low | Medium |
| A6 | Add stop conditions | 6 | Low | Medium |
| A7 | Enrich frontmatter with trigger terms | 7 | Low | Medium |
| A8 | Add output parsing hints for `dumpheap`, `gcroot`, `finalizequeue` | 8 | Medium | High |
| A9 | Add cross-skill triggers to threads skill | 9 | Low | Medium |
| A10 | Promote large-dump guard as default | 10 | Low | Medium |
| A11 | Add error handling table | 11 | Low | Medium |

### Threads Skill (`dotnet-threads-analysis`)

| ID | Action | Priority | Effort | Impact |
|---|---|---|---|---|
| B1 | Add MCP-first tool selection table | 1 | Low | Very High |
| B2 | Replace `dotnet-counters monitor` and unbounded `dotnet-trace` | 2 | Low | High |
| B3 | Restructure as decision tree with checkpoints | 3 | Medium | High |
| B4 | Extract summary template to `summary-template.md` | 4 | Low | Medium |
| B5 | Add input-gathering block | 5 | Low | Medium |
| B6 | Add stop conditions | 6 | Low | Medium |
| B7 | Enrich frontmatter with trigger terms | 7 | Low | Medium |
| B8 | Add output parsing hints for `syncblk`, `threads`, `clrstack` | 8 | Medium | High |
| B9 | Add cross-skill triggers to memory skill | 9 | Low | Medium |
| B10 | (N/A — large-dump guard is memory-specific) | - | - | - |
| B11 | Add error handling table | 11 | Low | Medium |

---

## Implementation Status

- [x] Priority 1 — MCP-first sections (A1, B1)
- [x] Priority 2 — Bounded commands (A2, B2)
- [x] Priority 3 — Decision tree restructure (A3, B3)
- [x] Priority 4 — Extract summary template (A4, B4)
- [x] Priority 5 — Input-gathering block (A5, B5)
- [x] Priority 6 — Stop conditions (A6, B6)
- [x] Priority 7 — Frontmatter trigger terms (A7, B7)
- [x] Priority 8 — Output parsing hints (A8, B8)
- [x] Priority 9 — Cross-skill triggers (A9, B9)
- [x] Priority 10 — Large-dump guard (A10)
- [x] Priority 11 — Error handling tables (A11, B11)
