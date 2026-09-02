# Compatibility matrix

| Component | Declared minimum | Published v0.3 verification |
|---|---:|---:|
| Unity | 6000.0 | 6000.5.3f1 |
| Burst | 1.8.29 | 1.8.29 |
| Collections | 2.6.3 | 6.5.0 built-in |
| Mathematics | 1.3.2 | 1.4.0 built-in |
| Platform | Windows x64 | Windows 11 x64 |
| Scripting backend | Mono or IL2CPP | Both verified for both Samples in non-Development Release Players |
| Burst AOT | Required | 1.8.29 library and required entrypoint manifest verified for Mono and IL2CPP |
| Source Generator target | .NET Standard 2.0 | Unity 6000.5.3f1 Roslyn host and standalone Roslyn 4.0.1 tests |
| Fixed-result renderer | Python 3.10+ and Pillow | Python 3.12.10 and Pillow 12.3.0 |

The verification column describes immutable `v0.3.0-preview.1` evidence only.
Other compatible versions remain hypotheses until their Player tests pass.

## Unreleased vNext tree

The vNext integration uses the same pinned Unity and package versions and has
passed its merged EditMode, generator, renderer, and Evidence Lab suites. A
non-Development Windows x64 Mono build produced the required Burst library and
entrypoint manifest; a tiny opt-in behavioral Player audit exited 0 with schema
3, parity, allocation, and generated-storage/profile gates satisfied. Its
timings are not performance evidence.

The merged-tree IL2CPP attempt failed before compilation because the installed
Unity Editor lacks the Windows IL2CPP scripting backend. Therefore vNext
IL2CPP/Burst AOT remains unverified. Exact outcomes are recorded in
[`VNEXT_DELIVERY_REPORT.md`](VNEXT_DELIVERY_REPORT.md); an unavailable backend
is a blocker, never an inferred pass.
