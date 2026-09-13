# Explicit external comparison entries

These are the finite, local Windows entries used by the
[September 10 protocol](../../Docs/ACTUAL_COMPARISON_PROTOCOL_2026-09-10.md).
No argument starts a workload implicitly. PR CI remains build/CPU-function only.
The ordinary external kernel APIs have no chat-authorization checks.

`run_exclusive.py` owns `Local\CodexR9700VNextUnityGpu` for exactly one explicit
child. It rejects occupied locks, active Unity/compiler/workload processes, reused
attempt directories and disk budgets that leave less than 20 GiB. It preserves
stdout/stderr/arguments/environment/exit status even on failure. It creates no
visible window or scheduled queue. Only its own timed-out child tree can be stopped.

`build_msvc.py --target babel|llama --output <new-dir>` is build-only. Use it under
`run_exclusive.py --phase build --output <new-attempt> -- ...`. Versions and flags
are explicit in the script and the output `build-command.json`. Babel uses real
Windows aligned allocation around unchanged OpenMP source; LLAMA includes unchanged
source with namespace/main isolation and real `::move` forwarding. Licenses remain
in the pinned upstream directories. No replacement algorithms or dummy APIs.

The Unity build entry is
`Yanagisawa.DataLayoutCalibrator.Benchmark.Editor.DataLayoutCalibratorBuild.BuildWindowsIl2CppFormal`.
Pass `-batchmode -nographics -quit -projectPath <BenchmarkProject> -dla-build-output
<new-dir> -logFile <new-attempt>/unity.log` under the same wrapper, with a 12 GiB
growth budget. The build requires actual AOT symbols for Babel and LLAMA jobs and
persists its source manifest. Source/compile errors fail the build.

The Player requires `-dla-external babel|llama -dla-external-output <new-prefix>`;
LLAMA additionally requires `-dla-external-input <canonical-file>`. Use headless
`-batchmode -nographics -job-worker-count 7 -logFile <new-attempt>/player.log`.
Every job call is real; no synthetic replacement is run. LLAMA's native preparer
generates the complete default upstream input with `--prepare <new-file> 1|2`.

`run_phase.py` records the exact September 10 output paths and fixed serial order:

```text
python Tools/ActualComparison/run_phase.py --phase prepare
python Tools/ActualComparison/run_phase.py --phase freeze
python Tools/ActualComparison/run_phase.py --phase correctness --workload babel
python Tools/ActualComparison/run_phase.py --phase measurement --workload babel
python Tools/ActualComparison/run_phase.py --phase correctness --workload llama
python Tools/ActualComparison/run_phase.py --phase measurement --workload llama
```

This orchestrator invokes the mutex wrapper per child; **do not wrap the
orchestrator in the same mutex**. Existing paths intentionally refuse a second
run. To reproduce, use a fresh isolated checkout with the recorded native/Player
build output locations (`native-babel-01`, `native-llama-02`, `player-il2cpp-02`),
or change paths for a new protocol/source identity; retain this run's evidence.
The freeze hashes all shipped Player files, source inputs, native executables,
protocol and canonical input bytes. Every measured process rechecks that freeze.

`verify_output.py` reads complete output files; `summarize.py` performs only offline
paired statistics. They never launch workloads. Babel raw output is three contiguous
little-endian double arrays; LLAMA output is 65,536 seven-float little-endian records.
Every attempt's exact command and output hash is bound by the tracked evidence index.
Storage lifecycle timing excludes caller-owned buffers, file I/O and process startup;
see the protocol for all boundaries. Allocation capability is checked in the Player,
but incomplete allocation coverage remains Unknown and no deployment profile is issued.
