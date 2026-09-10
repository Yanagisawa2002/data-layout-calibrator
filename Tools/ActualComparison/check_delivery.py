"""Small offline integration checks for source identity, report links and statistics."""
from pathlib import Path
import re
import subprocess
import sys

ROOT=Path(__file__).resolve().parents[2]

if __name__=='__main__':
    from run_phase import verify_freeze
    from summarize import paired
    verify_freeze()
    subprocess.run([sys.executable,ROOT/'Tools/KernelContracts/external_sources.py'],cwd=ROOT,check=True)
    assert paired([10,12,14],[12,14,16])['difference95CI']==[2,2]
    assert paired([10,12,14],[12,14,16])['status']=='regression'
    assert paired([10,10,10],[10.05,10.05,10.05])['status']=='tie'
    assert paired([10,10,10],[9,10,11])['status']=='inconclusive'
    assert paired([10,12,14],[8,10,12])['status']=='improvement'
    for path in ('Docs/ACTUAL_COMPARISON_REPORT_2026-09-10.md',
                 'Docs/ACTUAL_COMPARISON_PROTOCOL_2026-09-10.md','Tools/ActualComparison/README.md'):
        p=ROOT/path
        for target in re.findall(r'\]\(([^)]+)\)',p.read_text(encoding='utf-8')):
            if '://' in target or target.startswith('#'): continue
            assert (p.parent/target.split('#')[0]).resolve().exists(), (path,target)
    subprocess.run(['git','diff','--check'],cwd=ROOT,check=True)
    print('PASS: unchanged frozen build/source/input identities, upstream locks, 5 paired-statistic cases and new report/entry links.')
