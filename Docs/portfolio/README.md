# Portfolio figure

Memory-layout schematics explain the design choices; the chart reads each formal run’s recorded final decision and interval.

## Reproduce

From the repository root:

```bash
python -m pip install -r Docs/portfolio/requirements.txt
python Docs/portfolio/render.py
```

The renderer verifies source SHA-256 hashes (CRLF normalized to LF) before plotting the reviewed values in `figure.json`. If a source changes, review and refresh the snapshot before regenerating. It writes SVG and PNG with matching content.

## Sources

- [Docs/evidence/formal-il2cpp-2026-09-02/run-01-calibration-suite.json](../../Docs/evidence/formal-il2cpp-2026-09-02/run-01-calibration-suite.json)
- [Docs/evidence/formal-il2cpp-2026-09-02/run-02-calibration-suite.json](../../Docs/evidence/formal-il2cpp-2026-09-02/run-02-calibration-suite.json)
- [Docs/evidence/formal-il2cpp-2026-09-02/run-03-calibration-suite.json](../../Docs/evidence/formal-il2cpp-2026-09-02/run-03-calibration-suite.json)
- [Docs/evidence/formal-il2cpp-2026-09-02/run-04-calibration-suite.json](../../Docs/evidence/formal-il2cpp-2026-09-02/run-04-calibration-suite.json)
- [Docs/evidence/formal-il2cpp-2026-09-02/run-05-calibration-suite.json](../../Docs/evidence/formal-il2cpp-2026-09-02/run-05-calibration-suite.json)

The flow/memory/timing illustrations are schematics. Only explicitly labeled measurements represent recorded experiments. Confidence intervals are copied from source reports, not recomputed.
