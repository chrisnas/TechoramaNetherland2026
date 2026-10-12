# GPT-5.5 Findings

I reviewed both `dotnet-memory-analysis/SKILL.md` and `dotnet-threads-analysis/SKILL.md`. They are already useful, but they read more like diagnostic reference docs than optimized agent workflows. The biggest improvement would be to make them more decision-driven, less interactive, and more explicit about when to stop, ask, or escalate.

## Best Improvements

1. Replace interactive commands with bounded commands.
   - `dotnet-counters monitor` and `dotnet-trace collect` are awkward for agents because they run until interrupted.
   - Prefer examples with `--duration`, explicit output paths, or shell timeouts.
   - Example direction: collect 30 seconds, summarize counters, then decide the next step.

2. Add an agent quick-start decision tree.
   - User gave a `.dmp` file: start dump workflow.
   - User gave a PID: start live workflow.
   - User only described symptoms: ask for PID, dump path, or permission to enumerate processes.
   - Production target: ask before intrusive collection.
   - No clear symptom: collect lightweight evidence first.

3. Reduce `SKILL.md` to the core workflow.
   - `SKILL.md`: trigger, safety rules, quick workflow, commands, output format.
   - `reference.md`: SOS command explanations, diagnosis patterns, remediation notes.
   - `examples.md`: sample memory leak investigation, sample deadlock investigation.

4. Make custom tools first-class.
   - For duplicate strings, prefer the structured `dotnet-dstrings` tool if available; otherwise use CLI.
   - For parallel stacks, prefer the structured `dotnet-pstacks` tool if available; otherwise use CLI.
   - This saves tokens and avoids parsing large raw command output.

5. Avoid mandatory `SUMMARY.md` file creation.
   - For short investigations, keep a running summary in the response.
   - For multi-step or user-requested investigations, create `SUMMARY.md`.
   - If creating a file, update only after meaningful milestones, not every command.

6. Add explicit stop conditions.
   - Stop after identifying a root chain shared by two or three leaking objects.
   - Stop after proving a deadlock cycle with owner/waiter evidence.
   - Stop after counters do not support the suspected issue and report alternatives.
   - Ask before collecting heavier artifacts.

7. Add output templates optimized for final answers.
   - Finding
   - Evidence
   - Confidence
   - Recommended fix
   - Commands run
   - Remaining uncertainty

## Cursor vs Claude

For Cursor, make the skills more workspace-friendly: avoid unnecessary file creation, use relative references, prefer structured tools when available, and include clear "read this extra file only if needed" links.

For Claude, make the skills more self-contained and procedural: Claude benefits from strong safety gates, bounded commands, clear escalation rules, and exact report templates.

The most valuable single change would be adding a short agent workflow section at the top of each skill that turns the current reference material into a decision tree. That would make both skills faster, safer, and easier for agents to execute without drifting into broad diagnostic exploration.
