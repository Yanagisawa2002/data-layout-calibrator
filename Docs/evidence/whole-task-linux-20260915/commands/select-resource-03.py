"""One final resource-only window. Policy is persisted before sampling; no retry."""
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import shutil
import time

ROOT = Path('/root/autodl-tmp/codex-whole-task-20260915/data-layout')
CGROUP = Path('/sys/fs/cgroup')

def utc():
    return datetime.now(timezone.utc).isoformat()

def save(path, value):
    with path.open('x') as stream:
        json.dump(value, stream, indent=2)
        stream.write('\n')

def cpus(value):
    result = []
    for item in value.strip().split(','):
        ends = list(map(int, item.split('-')))
        result.extend(range(ends[0], ends[-1]+1))
    return result

policy = dict(
    frozenUtc=utc(), scriptSha256=hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
    durationSeconds=30, intervalSeconds=1, targetMeanMaximumPercent=15,
    targetIntervalMaximumPercent=25, siblingAggregateMaximumPercent=10,
    cgroupBackgroundMaximumCores=.25, maximumNewThrottledPeriods=0,
    maximumNewThrottledMicroseconds=0, excludedPhysicalCpus=[0,1,2],
    minimumQuotaCores=1, stableQuotaAndCpusetRequired=True,
    minimumAvailableMemoryBytes=2*2**30, taskGrowthBudgetBytes=2*2**30,
    minimumFreeDiskReserveBytes=10*2**30,
    selection='First qualified physical CPU in ascending minimum-SMT-ID order; resource data only. Freeze for the whole discovery batch.',
    backgroundDefinition='Whole cgroup CPU usage divided by monotonic elapsed seconds, including this resource observer, as in the original preflight. No owned-work subtraction.',
    failureAction='Stop performance attempts for this period. No core reselection or discovery retry.')
save(ROOT/'resource-selection-03-policy.json', policy)

def snapshot():
    raw = Path('/proc/stat').read_text()
    cpu_stat = (CGROUP/'cpu.stat').read_text()
    return dict(utc=utc(), time=time.monotonic(), procStat=raw,
        cpu={p[0]:list(map(int,p[1:9])) for line in raw.splitlines()
             if (p:=line.split()) and p[0].startswith('cpu')},
        cgroupCpuStat=cpu_stat,
        cgroupCpu={k:int(v) for k,v in (line.split() for line in cpu_stat.splitlines())},
        cgroupCpuMax=(CGROUP/'cpu.max').read_text().strip(),
        cpuset=(CGROUP/'cpuset.cpus.effective').read_text().strip(),
        memoryCurrentBytes=int((CGROUP/'memory.current').read_text()),
        memoryMax=(CGROUP/'memory.max').read_text().strip(),
        memoryEvents=(CGROUP/'memory.events').read_text(),
        cpuPressure=(CGROUP/'cpu.pressure').read_text(),
        affinity=sorted(os.sched_getaffinity(0)), diskFreeBytes=shutil.disk_usage(ROOT).free)

samples = [snapshot()]
deadline = samples[0]['time']
for _ in range(policy['durationSeconds']):
    deadline += policy['intervalSeconds']
    time.sleep(max(0., deadline-time.monotonic()))
    samples.append(snapshot())

def busy(before, after, cpu):
    a,b = before['cpu'][f'cpu{cpu}'], after['cpu'][f'cpu{cpu}']
    total = sum(b)-sum(a)
    return 100*(total-(b[3]+b[4]-a[3]-a[4]))/total if total>0 else None

first,last = samples[0],samples[-1]
global_reasons = []
usage = (last['cgroupCpu']['usage_usec']-first['cgroupCpu']['usage_usec'])/1e6
background = usage/(last['time']-first['time'])
throttled_periods = last['cgroupCpu'].get('nr_throttled',0)-first['cgroupCpu'].get('nr_throttled',0)
throttled_usec = last['cgroupCpu'].get('throttled_usec',0)-first['cgroupCpu'].get('throttled_usec',0)
if background>policy['cgroupBackgroundMaximumCores']: global_reasons.append('cgroup-background')
if throttled_periods!=0 or throttled_usec!=0: global_reasons.append('new-cgroup-throttling')
for field in ('cgroupCpuMax','cpuset','memoryMax','affinity'):
    if any(s[field]!=first[field] for s in samples): global_reasons.append('changed-'+field)
quota,period = first['cgroupCpuMax'].split()
quota_cores = None if quota=='max' else int(quota)/int(period)
if quota_cores is None or quota_cores<policy['minimumQuotaCores']: global_reasons.append('unqualified-quota')
if first['memoryMax']=='max' or any(int(s['memoryMax'])-s['memoryCurrentBytes']<policy['minimumAvailableMemoryBytes'] for s in samples):
    global_reasons.append('memory-budget-unavailable')
if any(s['diskFreeBytes']<policy['taskGrowthBudgetBytes']+policy['minimumFreeDiskReserveBytes'] for s in samples):
    global_reasons.append('disk-budget-unavailable')
if not set(first['affinity'])<=set(cpus(first['cpuset'])): global_reasons.append('affinity-outside-cpuset')
intervals = []
for a,b in zip(samples,samples[1:]):
    intervals.append(dict(seconds=b['time']-a['time'],
        cgroupBackgroundCores=(b['cgroupCpu']['usage_usec']-a['cgroupCpu']['usage_usec'])/1e6/(b['time']-a['time']),
        throttledPeriods=b['cgroupCpu'].get('nr_throttled',0)-a['cgroupCpu'].get('nr_throttled',0),
        throttledMicroseconds=b['cgroupCpu'].get('throttled_usec',0)-a['cgroupCpu'].get('throttled_usec',0)))
candidates=[]
selected=None
for cpu in first['affinity']:
    siblings=cpus(Path(f'/sys/devices/system/cpu/cpu{cpu}/topology/thread_siblings_list').read_text())
    if cpu!=min(siblings) or cpu in policy['excludedPhysicalCpus']: continue
    means={s:busy(first,last,s) for s in siblings}
    values=[busy(a,b,cpu) for a,b in zip(samples,samples[1:])]
    reasons=list(global_reasons)
    if means[cpu] is None or means[cpu]>policy['targetMeanMaximumPercent']: reasons.append('target-mean')
    violations=[dict(interval=i,percent=v) for i,v in enumerate(values) if v is None or v>policy['targetIntervalMaximumPercent']]
    if violations: reasons.append('target-interval')
    if any(v is None or v>policy['siblingAggregateMaximumPercent'] for s,v in means.items() if s!=cpu): reasons.append('sibling-aggregate')
    candidate=dict(physicalCpu=cpu,siblings=siblings,aggregatePercent=means,targetIntervalPercent=values,
        intervalViolations=violations,qualified=not reasons,reasons=reasons)
    candidates.append(candidate)
    if selected is None and not reasons: selected=dict(cpu=cpu,siblings=siblings)
receipt=dict(policy=policy,selected=selected,candidates=candidates,samples=samples,
    intervals=intervals,globalReasons=global_reasons,quotaCores=quota_cores,
    cgroupBackgroundCores=background,newThrottledPeriods=throttled_periods,
    newThrottledMicroseconds=throttled_usec,finishedUtc=utc(),performanceEligible=False,
    note='Resource qualification only; no application timing or algorithm conclusion.')
save(ROOT/'resource-selection-03.json',receipt)
print(json.dumps(dict(selected=selected,qualified=sum(c['qualified'] for c in candidates),
    candidateCount=len(candidates),globalReasons=global_reasons,cgroupBackgroundCores=background)),flush=True)
if selected is None: raise RuntimeError('Final original-rule window failed: no qualified performance evidence for this period')
selected.update(resourceSelectionEvidence=str(ROOT/'resource-selection-03.json'),
    policySha256=hashlib.sha256((ROOT/'resource-selection-03-policy.json').read_bytes()).hexdigest())
save(ROOT/'cpu-selection-03.json',selected)
