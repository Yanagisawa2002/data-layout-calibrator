"""Retain and byte-verify every integration attempt without rewriting raw files."""
import argparse
import hashlib
import json
import zipfile
from pathlib import Path


def digest(data):
    return hashlib.sha256(data).hexdigest().upper()


def archive(source, destination):
    source = source.resolve()
    destination.mkdir(parents=True, exist_ok=True)
    target = destination / 'integration-raw.zip'
    manifest_path = destination / 'archive-manifest.json'
    if target.exists() or manifest_path.exists():
        raise ValueError('Existing retained archive cannot be overwritten.')
    entries = []
    paths = sorted(p for p in source.rglob('*') if p.is_file())
    with zipfile.ZipFile(target, 'x', compression=zipfile.ZIP_DEFLATED, compresslevel=6) as output:
        for path in paths:
            if not path.resolve().is_relative_to(source):
                raise ValueError('Source path escapes the evidence directory.')
            name = path.relative_to(source).as_posix()
            data = path.read_bytes()
            output.writestr(name, data)
            entries.append({'path': name, 'bytes': len(data), 'sha256': digest(data)})
    with zipfile.ZipFile(target) as retained:
        if len(retained.namelist()) != len(entries) or retained.testzip() is not None:
            raise ValueError('Archive entry count or CRC verification failed.')
        for item in entries:
            data = retained.read(item['path'])
            if len(data) != item['bytes'] or digest(data) != item['sha256']:
                raise ValueError('Archived bytes differ: ' + item['path'])
    identity = json.loads((source/'il2cpp-prepare-attempt-02/build-identity.json').read_text(encoding='utf-8-sig'))
    manifest = {'schemaVersion': 1, 'archive': target.name, 'archiveSha256': digest(target.read_bytes()),
        'sourceCommitForFinalFormalEvidence': identity['sourceCommit'], 'verifiedEntryCount': len(entries),
        'rawBytes': sum(e['bytes'] for e in entries), 'archiveBytes': target.stat().st_size,
        'scope': 'Every retained integration attempt, including superseded builds and failures; final accepted evidence is identified in README and report.',
        'files': entries}
    manifest_path.write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8', newline='\n')
    print(json.dumps({k: v for k, v in manifest.items() if k != 'files'}, indent=2))


def verify(manifest_path):
    manifest = json.loads(manifest_path.read_text(encoding='utf-8-sig'))
    path = manifest_path.parent / manifest['archive']
    if digest(path.read_bytes()) != manifest['archiveSha256']:
        raise ValueError('Archive SHA-256 mismatch.')
    with zipfile.ZipFile(path) as retained:
        names = retained.namelist()
        if len(names) != len(set(names)) or set(names) != {e['path'] for e in manifest['files']}:
            raise ValueError('Archive entry set mismatch.')
        for item in manifest['files']:
            data = retained.read(item['path'])
            if len(data) != item['bytes'] or digest(data) != item['sha256']:
                raise ValueError('Archived bytes differ: ' + item['path'])
    print(f"Verified {len(names)} retained files and archive SHA-256.")


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source', type=Path, nargs='?')
    parser.add_argument('destination', type=Path, nargs='?')
    parser.add_argument('--verify', type=Path)
    args = parser.parse_args()
    if args.verify:
        verify(args.verify)
    elif args.source and args.destination:
        archive(args.source, args.destination)
    else:
        parser.error('Supply source and destination, or --verify manifest.json.')
