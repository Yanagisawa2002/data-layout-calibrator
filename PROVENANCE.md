# Clean-room-style provenance

This repository was created as a new personal project on 2026-09-01.

This is a practical provenance boundary, not a claim that a legal two-team clean-room process was performed.

## Boundary

- No source code, assets, scenes, datasets, tests, benchmark captures, or documentation were copied from any employer-owned or client repository.
- The feasibility workload is synthetic and deterministic.
- The design relies on public Unity Burst, Collections, Mathematics, Jobs, and Test Framework APIs.
- Vendor profilers and compilers may be integrated later through adapters; they will not be reimplemented here.

The public repository is intended to be
`https://github.com/Yanagisawa2002/data-layout-calibrator`. Public visibility is
provided for portfolio review and authorship verification; it does not change
the rights reserved in [`LICENSE`](LICENSE).

## Authorship trail

Architecture decisions, benchmark contracts, raw result schemas, release tags, and material changes are retained in this repository so that design and implementation provenance remain reviewable.

The initial public release is required to use a cryptographically signed,
annotated tag. `CITATION.cff` and [`AUTHORS.md`](AUTHORS.md) identify the author
without importing attribution from any product repository.
