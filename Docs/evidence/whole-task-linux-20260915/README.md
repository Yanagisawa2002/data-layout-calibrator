# Whole-task Linux evidence — 2026-09-15

**Correctness passed; complete discovery and formal performance conclusions are
unavailable for this period.** See the [execution report](../../WHOLE_TASK_LINUX_EXECUTION_2026-09-15.md).

## Main evidence

- [raw-evidence.tar.gz](raw-evidence.tar.gz): all 3,787 retained source/run/output
  files, 28,853,815 compressed bytes. SHA-256
  `dbabf4cabbb9c73e8dccc3732e1bb99d6cc4e64d4d2ea87e620e3c56f36f6a1a`.
- [files.json](files.json): per-entry hashes, sizes and exclusion policy.
- [local-verification.json](local-verification.json): complete transferred-entry
  hash verification. [archive.json](archive.json) is the remote archive receipt.
- [acceptance.json](acceptance.json): final binary identities, seven all-arm
  complete-output parity groups, storage/boundary contracts and 95-test counters.
- [summary.json](summary.json): physics and managed-allocation controls, resource
  outcome and explicit unexecuted formal stages.
- [discovery-observations.json](discovery-observations.json): 24 individually
  qualified observations from the incomplete protocol, with source/raw paths.
  These are single discovery processes per cell/arm, without formal uncertainty.
- [release.json](release.json): completed stages, recorded PID references,
  survivor scan, actual flock release, disk reserve and retained dependencies.

`receipts/` contains byte-identical readable copies of the main raw receipts.
All complete input/state binaries, every VTU and consumed CSV, failures, compiler
logs, generated source diffs and process telemetry remain in the archive. The
archive also retains exact source bundles 01/02/03/04/05/07/08, including unused
03, rather than silently replacing earlier failed source. Source 06 was a local
unuploaded preparation snapshot and never ran on the server.

SDK downloads/caches and duplicated extracted source are omitted; exact source
archives, native binaries and the final selector runtime are present. Any
memory dumps are excluded. Source evidence and isolated dependencies remain
unchanged on the remote host. No unrelated project material is included.

## Reproduction and stage identity

The implemented campaign lives in [Tools/WholeTask](../../../Tools/WholeTask/README.md).
The original shell entry points and inspection/verification scripts are retained
under [commands](commands/files.json), with exact hashes. They embed the original
isolated Linux root and unique stage names. This directory is a record of the
commands, including failed and prepared-but-unused variants; it is not an
automatic restart queue. Actual executed stages and exits are listed in
[release.json](release.json).

Principal executed entry points:

| Entry point | Recorded stage and purpose |
| --- | --- |
| `environment-01-setup.sh` | Isolated Python/.NET setup with pinned archive checks |
| `build-only-linux-08.sh` | `source-linux-07` → native-build-04 / validation-04 |
| `correctness-only-linux-09.sh` | Fixed CPU2, correctness-final-01 and sanitizer-03; all timing excluded |
| `select-resource-03.py` | Single predeclared 30-second original-rule CPU/cgroup window |
| `final-discovery-linux-10.sh` | Verified source-08 reuse, seven Python tests, fixed CPU3 discovery; exit 1 |
| `collect-evidence-11.sh` | Foreground archive creation and per-entry verification |
| `verify-release-12.sh` | No survivor, actual nonblocking lock acquisition and release proof |

Every heavy command was sent to `bash -s` by the coordinator's authenticated
SSH helper with `--lock`; this held the campaign's actual `.hardware.lock` for
the complete foreground process. `linux_stage.py` independently verified the
ancestor flock descriptor and inode. The release probe ran after all heavy
stages completed. Authentication material is not part of these files.

To inspect an archive on an ordinary offline machine:

```powershell
Get-FileHash .\raw-evidence.tar.gz -Algorithm SHA256
tar -tf .\raw-evidence.tar.gz
```

The original local verification scripts in `commands/` were executed from
`Artifacts/whole-task/` in the repository and locate the repo relative to that
path. They validate/extract into fresh destinations and refuse overwrite.
Re-running application stages needs a newly coordinated hardware grant and new
output names. Do not reinterpret the retained failed discovery as a complete
input for `make_protocol.py`.
