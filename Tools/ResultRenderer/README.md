# Fixed-result renderer

This standalone tool turns a schema-2 `calibration-suite.json` into a PNG heatmap, an animated before/after GIF, and a provenance manifest. It is intentionally outside the Unity project and contains no selection algorithm.

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

The manifest records the input SHA-256 and the exact frozen decision fields used by both visuals.
