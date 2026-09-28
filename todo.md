# data-layout-calibrator TODO

Updated: 2026-09-28. Repository-specific handoff for testing on another device. All tasks below are pending on that device; this documentation change runs no benchmarks or application tests.

Source baseline: `2dc4c27d0677b5f127641b70a2adffa8941e3e37` on main. Start with the [pinned README](https://github.com/Yanagisawa2002/data-layout-calibrator/blob/2dc4c27d0677b5f127641b70a2adffa8941e3e37/README.md) for prerequisites and the scope of historical evidence.

## Prepare the checkout

- [ ] Clone into a fresh directory and select the recorded source commit. If deliberately using a newer revision, record the new SHA and review the intervening changes.

```powershell
gh repo clone Yanagisawa2002/data-layout-calibrator
Set-Location data-layout-calibrator
git switch --detach 2dc4c27d0677b5f127641b70a2adffa8941e3e37
git rev-parse HEAD
```

## Functional checks first

- [ ] Install Python 3.10+ and a .NET SDK/runtime that can build and execute the `net8.0` functional projects. Run:

```powershell
python -m pip install -r Tools/ResultRenderer/requirements.txt
python Tools/CI/validate_functional.py --build-only
python Tools/CI/validate_functional.py
python Tools/CI/check_repository.py
python Tools/KernelContracts/external_sources.py
```

- [ ] Run the synthetic lifetime example only as an API/decision demonstration; it supplies no benchmark or deployment profile.
- [ ] For a real consumer, follow `Docs/LAYOUT_ADOPTION.md`: supply concrete candidates, canonical conversions and parity validation rather than expecting automatic rewriting of application structs.
- [ ] Exercise buffer reuse, ownership and conversion costs in the actual host before timed selection.
- [ ] If reproducing measurements later, select the Windows Dot or complete SPH protocol and retain independent calibration/confirmation evidence and complete-task costs.
- [ ] Keep IL2CPP worker/native allocation coverage and allocation-qualified deployment Unknown until the positive control and relevant allocation evidence succeed.
- [ ] Keep profitable automatic selection unproven until calibration cost and independent reuse are included. Review the status of the unfinished PR #10 experiment before continuing it.

## Record the new-device outcome

- [ ] Record source and binary identity, machine/OS, toolchain, hardware/driver, command/exit code, input/configuration hashes and the actual PASS / FAIL / BLOCKED / NOT RUN result.
- [ ] Keep generated binaries, captures and large assets outside source control. Link the retained evidence from the affected validation document/PR.
- [ ] Preserve unavailable metrics and negative outcomes. Separate static/functional success, runtime correctness and performance conclusions.

Create a focused `codex/` branch before implementing a fix. Existing unexecuted acceptance items remain open until the selected workflow is actually run.
