import json,os,pathlib,time
root=pathlib.Path('/root/autodl-tmp/codex-whole-task-20260915/data-layout')
def snapshot():
    return {p[0]:list(map(int,p[1:9])) for line in pathlib.Path('/proc/stat').read_text().splitlines() if (p:=line.split()) and p[0].startswith('cpu')}
samples=[snapshot()]
for _ in range(30):
    time.sleep(1);samples.append(snapshot())
allowed=sorted(os.sched_getaffinity(0)); loads={}
for cpu in allowed:
    values=[]
    for a,b in zip(samples,samples[1:]):
        before,after=a[f'cpu{cpu}'],b[f'cpu{cpu}'];total=sum(after)-sum(before)
        values.append(100*(total-(after[3]+after[4]-before[3]-before[4]))/total if total else 100)
    loads[cpu]=values
selected=None; candidates=[]
for cpu in allowed:
    if cpu<=2:continue
    siblings=[int(x) for x in pathlib.Path(f'/sys/devices/system/cpu/cpu{cpu}/topology/thread_siblings_list').read_text().strip().split(',')]
    if cpu!=min(siblings):continue
    violations=[dict(logicalCpu=s,interval=i,percent=x) for s in siblings for i,x in enumerate(loads[s]) if x>10]
    candidates.append(dict(physicalCpu=cpu,siblings=siblings,qualified=not violations,violations=violations))
    if selected is None and not violations:
        selected=dict(cpu=cpu,siblings=siblings)
receipt=dict(selected=selected,candidates=candidates,perCpuPercent=loads,samples=samples,excludedPhysicalCpus=[0,1,2],
    rule='First allowed physical core >=3 with both SMT siblings <=10 percent in every one-second interval for thirty seconds. CPU2 excluded solely due to discovery-01 observed SMT interference; CPU0/1 avoided as housekeeping. No application timings enter this choice; no candidate reselection within the batch.')
(root/'resource-selection-02.json').write_text(json.dumps(receipt,indent=2))
if not selected:raise RuntimeError('No stable quiet physical core; hardware qualification unavailable')
selected['resourceSelectionEvidence']=str(root/'resource-selection-02.json')
(root/'cpu-selection-02.json').write_text(json.dumps(selected,indent=2))
print(json.dumps(selected))
