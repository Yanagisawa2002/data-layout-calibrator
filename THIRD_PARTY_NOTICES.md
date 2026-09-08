# Third-party notices

This repository does not vendor Unity, Burst, Collections, Mathematics, Test Framework, or their binaries.

The UPM package declares dependencies on Unity packages. Those packages are obtained from the Unity installation or Unity Package Manager and remain governed by their respective licenses and terms. Generated local Player builds are excluded from this repository.

The Source Generator build uses the Microsoft.CodeAnalysis.CSharp NuGet package as a private build-time dependency; it is not bundled into the distributed analyzer DLL. Its standalone tests use NUnit and the Microsoft .NET test SDK. The optional fixed-result renderer requires Pillow at execution time. These dependencies are restored by their package managers and are not vendored in this repository.


## Pinned external workload sources and derived files (2026-09-08)

External files keep their upstream licenses; the repository's general limited
reproduction license does not replace these file-level permissions and obligations.
The exact URL/commit/content hashes are recorded in the
[source lock](Packages/com.yanagisawa.data-layout-calibrator/Samples/ExternalWorkloads/Upstream~/upstream-lock.json).

- **BabelStream**: University of Bristol HPC copyright and custom license/run rules
  are retained in the
  [original license](Packages/com.yanagisawa.data-layout-calibrator/Samples/ExternalWorkloads/Upstream~/BabelStream/LICENSE).
  `BabelStreamPort.cs` is a C#/Burst variant; small contract fixtures are not native
  BabelStream results. Modified-source results require the upstream variant label.
- **STREAM**: the official 5.10 source includes its copyright, permission and run
  rules in [stream.c](Packages/com.yanagisawa.data-layout-calibrator/Samples/ExternalWorkloads/Upstream~/STREAM/stream.c).
  The site provides no Git commit: source identity is its retained content SHA-256.
- **LLAMA**: upstream sources and
  [license](Packages/com.yanagisawa.data-layout-calibrator/Samples/ExternalWorkloads/Upstream~/llama/LICENSE)
  remain MPL-2.0. The derived
  [LlamaNBodyPort.cs](Packages/com.yanagisawa.data-layout-calibrator/Samples/ExternalWorkloads/Runtime/LlamaNBodyPort.cs)
  and [input-only preparer](Tools/KernelContracts/llama_input_fixture.cpp) retain
  MPL-2.0 file-level headers and are not relicensed under the root limited permission.
  The code_comp example is a named library-native workload, not a standard suite score.
- **HeCBench stencil3d-omp**: retain both the collection notice and the workload's
  [local MIT license](Packages/com.yanagisawa.data-layout-calibrator/Samples/ExternalWorkloads/Upstream~/HeCBench/src/stencil3d-omp/LICENSE),
  copyright Lawrence Livermore National Security, LLC. This workload is prepared
  as original source; no Burst port or measured result is claimed.

The [external contract](Packages/com.yanagisawa.data-layout-calibrator/Samples/ExternalWorkloads/CONTRACT.md)
lists upstream defaults, output checks, port differences and unexecuted build entries.
No upstream benchmark driver ran in this delivery.
