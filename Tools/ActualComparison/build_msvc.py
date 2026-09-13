"""Build the isolated native LLAMA adapter with the installed, identified MSVC toolchain."""
from pathlib import Path
import argparse
import json
import os
import subprocess

ROOT = Path(__file__).resolve().parents[2]
VS = Path('C:/Program Files/Microsoft Visual Studio/18/Community')
VC = VS / 'VC/Tools/MSVC/14.51.36231'
KIT = Path('C:/Program Files (x86)/Windows Kits/10')

if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--target', choices=['llama', 'babel'], required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    OUT = args.output.resolve()
    OUT.mkdir(parents=True, exist_ok=False)
    env = dict(os.environ)
    env['INCLUDE'] = ';'.join(map(str,[VC/'include']+[KIT/'Include/10.0.26100.0'/x for x in ('ucrt','shared','um','winrt')]))
    env['LIB'] = ';'.join(map(str,[VC/'lib/x64',KIT/'Lib/10.0.26100.0/ucrt/x64',KIT/'Lib/10.0.26100.0/um/x64']))
    env['PATH'] = str(VC/'bin/Hostx64/x64')+';'+env['PATH']
    command=[str(VC/'bin/Hostx64/x64/cl.exe'),'/nologo','/O2','/fp:strict','/arch:AVX2','/std:c++20','/EHsc','/MD',
             str(ROOT/('Tools/ActualComparison/'+args.target+'_native.cpp')),'/Fo'+str(OUT/(args.target+'_native.obj')),'/Fe'+str(OUT/(args.target+'_native.exe'))]
    if args.target == 'babel':
        upstream = ROOT/'Packages/com.yanagisawa.data-layout-calibrator/Samples/ExternalWorkloads/Upstream~/BabelStream/src'
        command += ['/openmp', '/I'+str(upstream), '/I'+str(upstream/'omp')]
    (OUT/'build-command.json').write_text(json.dumps(dict(command=command, INCLUDE=env['INCLUDE'], LIB=env['LIB']), indent=2))
    subprocess.run(command,cwd=OUT,env=env,check=True)
