"""Prepare a task-owned Unity copy plus its signed Windows IL2CPP module.

Does not edit the installed Editor. Invoke explicitly under run_exclusive.py.
"""
import argparse
import base64
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess

if __name__ == '__main__':
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--installed-root', type=Path, required=True)
    p.add_argument('--destination', type=Path, required=True)
    p.add_argument('--module', type=Path, required=True)
    p.add_argument('--module-manifest', type=Path, required=True)
    a = p.parse_args()
    manifest = json.loads(a.module_manifest.read_text(encoding='utf-8-sig'))
    data = a.module.read_bytes()
    assert manifest['integrity'] == 'md5-' + base64.b64encode(hashlib.md5(data).digest()).decode()
    assert manifest['id'] == 'windows-il2cpp'
    assert not a.destination.exists(), 'Use a new isolated destination'
    a.destination.mkdir(parents=True)
    shutil.copytree(a.installed_root/'Editor', a.destination/'Editor',
                    ignore=shutil.ignore_patterns('Documentation'))
    env = dict(os.environ, __COMPAT_LAYER='RunAsInvoker')
    command = [str(a.module.resolve()), '/S', '/D=' + str(a.destination.resolve())]
    result = subprocess.run(command, env=env, creationflags=subprocess.CREATE_NO_WINDOW, timeout=600)
    receipt = dict(installedSource=str(a.installed_root), destination=str(a.destination),
                   command=command, exitCode=result.returncode, moduleSha256=hashlib.sha256(data).hexdigest())
    (a.destination/'module-install.json').write_text(json.dumps(receipt, indent=2))
    print(json.dumps(receipt), flush=True)
    result.check_returncode()
