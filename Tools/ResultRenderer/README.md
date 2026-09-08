# Renderer semantic correction (2026-09-08)

New displays label `AmortizedLatency.P95Milliseconds` as **component-P95 selection
score**, and `Latency.P95Milliseconds` as **P95 of block means**. Neither is a true
individual-tick/lifecycle percentile. Keys, numbers, recorded decisions and
historical input hashes remain unchanged. Additive `TimingContract` schema 1 is
validated; missing contracts are described as historical, never remeasured.
Allocation status is Unknown when capabilities, required scope or complete windows
are missing. Numeric zeros in historical files are preserved without certifying them.

Functional-only model tests:

```powershell
python -m unittest discover -s Tools/ResultRenderer/tests -p test_measurement_contract.py -v
```

This round did not regenerate historical PNG/GIF artifacts. Old renderer instructions
below describe optional fixed-artifact rendering, not measurement authorization.

# Fixed-result renderer

This standalone tool turns a schema-2 or schema-3 `calibration-suite.json` into a PNG heatmap, an animated before/after GIF, and a provenance manifest. It is intentionally outside the Unity project and contains no selection algorithm.

## Chart contract

- Analytical question: how did each concrete layout/batch candidate measure, and which candidate did the already-completed calibration select?
- Takeaway: reveal a real non-AoS win and an AoS negative control without letting presentation code optimize for a prettier story.
- Forms: a matrix heatmap for the complete candidate grid, plus an animated latency bar comparison from tuned AoS to the frozen decision.
- Grain: one heatmap cell per calibration-phase candidate; one GIF card per scenario. Exact amortized-P95 values, record count, lifetime, backend, and build type remain visible.
- Palette: one blue root plus neutral context; gold marks fallback/context. Selection also uses labels and keylines, so meaning does not depend on color.
- Decision rule: `BaselineCandidate`, `SelectedCandidate`, `BestMeasuredCandidate`, improvement, and confidence interval are copied verbatim from `FinalDecision`. Measurements may affect labels and color intensity only.

## Run

```powershell
python -m pip install -r Tools/ResultRenderer/requirements.txt

python Tools/ResultRenderer/render_results.py `
  Docs/evidence/il2cpp-release-calibration-suite.json `
  Docs/assets

python -m unittest discover Tools/ResultRenderer/tests -v
```

The manifest records the input SHA-256 and the exact frozen decision fields used
by both visuals. For schema 3 it also records the bootstrap estimator kind and
whether the source interval contains a realized log-ratio estimate; inconsistent
provenance is rejected. An optional external advantage-envelope reference is
recomputed against the canonical candidate-set bytes and measurement-schema hash,
then copied into the manifest without reading envelope cells or changing
`FinalDecision`.
