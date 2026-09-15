set -euo pipefail
task_root=/root/autodl-tmp/codex-whole-task-20260915/data-layout
"$task_root/env/python/bin/python3" - <<'PY'
from datetime import datetime,timezone
import fcntl,json,os,pathlib,shutil
root=pathlib.Path('/root/autodl-tmp/codex-whole-task-20260915/data-layout')
out=root/'evidence-closeout-01'
manifest=json.load(open(out/'files.json'))
references=[]
stages=[]
for name in manifest['files']:
    path=root/name
    if path.suffix=='.pid':
        references.append(dict(pid=int(path.read_text().strip()),kind='recorded-owned-pid',evidence=name))
    elif path.name=='result.json':
        value=json.load(open(path))
        if 'processIdentity' in value:
            references.append(dict(pid=int(value['processIdentity'].split(':')[0]),kind='native-application',
                processIdentity=value['processIdentity'],completed=value['completed'],evidence=name))
    elif path.name=='stage.json':
        value=json.load(open(path))
        stages.append(dict(evidence=name,status=value.get('status'),exitCode=value.get('exitCode'),
            startedUtc=value.get('startedUtc'),endedUtc=value.get('endedUtc')))
        for key in ('pid','childPid'):
            if value.get(key):references.append(dict(pid=value[key],kind=key,evidence=name))
        if value.get('flock',{}).get('pid'):references.append(dict(pid=value['flock']['pid'],kind='flock',evidence=name))
for stage in sorted(root.iterdir()):
    if not stage.is_dir():continue
    exitfile=stage/'exit-code.txt'
    if exitfile.exists():
        value=dict(stage=stage.name,exitCode=int(exitfile.read_text().strip()))
        for name in ('started.txt','ended.txt'):
            if (stage/name).exists():value[name]=(stage/name).read_text().strip()
        stages.append(value)
        if (stage/'shell.pid').exists():references.append(dict(pid=int((stage/'shell.pid').read_text().strip()),kind='stage-shell',evidence=str((stage/'shell.pid').relative_to(root))))
observer=os.getpid()
ancestors=[]
pid=observer
while pid>1:
    ancestors.append(pid)
    pid=int(pathlib.Path(f'/proc/{pid}/stat').read_text().rsplit(') ',1)[1].split()[1])
running=[]
boot_id=pathlib.Path('/proc/sys/kernel/random/boot_id').read_text().strip()
for proc in pathlib.Path('/proc').iterdir():
    if not proc.name.isdigit() or int(proc.name) in ancestors:continue
    try:
        exe=os.readlink(proc/'exe'); cwd=os.readlink(proc/'cwd')
        args=(proc/'cmdline').read_bytes().replace(b'\0',b' ').decode(errors='replace')
        owns=(cwd==str(root) or cwd.startswith(str(root)+'/') or exe.startswith(str(root)+'/') or str(root) in args)
        if owns:
            stat=(proc/'stat').read_text(); fields=stat.rsplit(') ',1)[1].split()
            running.append(dict(pid=int(proc.name),comm=stat.split('(',1)[1].rsplit(')',1)[0],
                executable=exe,cwd=cwd,startTicks=int(fields[19]),bootId=boot_id))
    except (FileNotFoundError,ProcessLookupError,PermissionError):pass
for ref in references:
    proc=pathlib.Path(f"/proc/{ref['pid']}")
    ref['pidCurrentlyExists']=proc.exists()
    if proc.exists():
        try:
            stat=(proc/'stat').read_text(); fields=stat.rsplit(') ',1)[1].split()
            ref['currentIdentity']=dict(comm=stat.split('(',1)[1].rsplit(')',1)[0],startTicks=int(fields[19]),bootId=boot_id)
        except FileNotFoundError:ref['pidCurrentlyExists']=False
lockpath=root.parent/'.hardware.lock'
lock=dict(path=str(lockpath),inode=lockpath.stat().st_ino,nonblockingAcquireSucceeded=False,releasedAfterProbe=False)
with lockpath.open('a') as stream:
    try:
        fcntl.flock(stream,fcntl.LOCK_EX|fcntl.LOCK_NB)
        lock['nonblockingAcquireSucceeded']=True
        fcntl.flock(stream,fcntl.LOCK_UN)
        lock['releasedAfterProbe']=True
    except BlockingIOError:pass
receipt=dict(verifiedUtc=datetime.now(timezone.utc).isoformat(),observerPid=observer,observerAncestorsExcluded=ancestors,
    bootId=boot_id,ownedRunningProcesses=running,recordedProcessReferences=references,stageCompletions=stages,
    hardwareLock=lock,diskFreeBytes=shutil.disk_usage(root).free,
    retainedReadOnlyDependencies=dict(python=str(root/'env/python/bin/python3'),pythonVersion='3.12.11',
        dotnet=str(root/'env/dotnet-10.0.401/dotnet'),dotnetSdk='10.0.401',dotnetRuntime='10.0.12',
        gcc='/usr/bin/g++',gccVersion='11.4',source=str(root/'source-linux-08'),nativeBuild=str(root/'native-build-04')),
    nativeBuildChildPidCoverage='Individual historical compiler and contract subprocess PIDs were not separately persisted. Their synchronous command/exit records and enclosing completed stages are retained; the current task-root/executable/command ownership scan checks for survivors.',
    hardwareReleased=not running and lock['nonblockingAcquireSucceeded'] and lock['releasedAfterProbe'],
    serverShutdown=False,artifactsDeleted=False)
with (out/'release.json').open('x') as stream:json.dump(receipt,stream,indent=2)
print(json.dumps(dict(hardwareReleased=receipt['hardwareReleased'],ownedRunningProcesses=running,
    recordedProcessReferences=len(references),recordedPidsStillPresent=sum(r['pidCurrentlyExists'] for r in references),
    hardwareLock=lock,diskFreeBytes=receipt['diskFreeBytes'],stageCompletions=stages),indent=2))
if not receipt['hardwareReleased']:raise RuntimeError('Release not proved; retain lease')
PY
