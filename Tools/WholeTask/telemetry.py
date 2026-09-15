"""Low-frequency platform CPU accounting around each complete application task.

No process settings change. No raw command lines are read. Monitoring work and
the original tick timing remain in the observed run; no overhead is subtracted.
"""
import ctypes
from ctypes import wintypes
import os
from pathlib import Path
import threading
import time


class WindowsCpuTelemetry:
    def __init__(self):
        self.kernel = ctypes.WinDLL("kernel32", use_last_error=True)
        self.kernel.GetSystemTimes.argtypes = [ctypes.POINTER(wintypes.FILETIME)] * 3
        self.kernel.GetProcessTimes.argtypes = [wintypes.HANDLE] + [ctypes.POINTER(wintypes.FILETIME)] * 4
        self.kernel.GetCurrentProcess.restype = wintypes.HANDLE
        self.current = self.kernel.GetCurrentProcess()
        self.child_handle = None
        self.samples = []
        self.errors = []
        self.event = threading.Event()
        self.thread = None

    @staticmethod
    def ticks(ft):
        return (ft.dwHighDateTime << 32) | ft.dwLowDateTime

    def process_cpu(self, handle):
        created, exited, kernel, user = [wintypes.FILETIME() for _ in range(4)]
        if not self.kernel.GetProcessTimes(handle, ctypes.byref(created), ctypes.byref(exited), ctypes.byref(kernel), ctypes.byref(user)):
            raise ctypes.WinError(ctypes.get_last_error())
        return self.ticks(kernel) + self.ticks(user)

    def read(self):
        idle, kernel, user = [wintypes.FILETIME() for _ in range(3)]
        if not self.kernel.GetSystemTimes(ctypes.byref(idle), ctypes.byref(kernel), ctypes.byref(user)):
            raise ctypes.WinError(ctypes.get_last_error())
        return dict(time=time.perf_counter(), idle=self.ticks(idle),
                    total=self.ticks(kernel)+self.ticks(user),
                    controller=self.process_cpu(self.current),
                    child=self.process_cpu(self.child_handle) if self.child_handle is not None else 0)

    @staticmethod
    def percent(a, b, exclude_owned=False):
        total = b["total"]-a["total"]
        if total <= 0:
            return None
        busy = total-(b["idle"]-a["idle"])
        if exclude_owned:
            busy -= (b["controller"]-a["controller"]) + (b["child"]-a["child"])
        return max(0.0, min(100.0, 100.0*busy/total))

    def preflight(self):
        snapshots = [self.read()]
        for _ in range(3):
            time.sleep(.25)
            snapshots.append(self.read())
        values = [self.percent(a, b) for a, b in zip(snapshots, snapshots[1:])]
        return dict(samples=snapshots, totalCpuPercent=values,
                    passed=all(x is not None and x <= 25 for x in values) and sum(values)/len(values) <= 15)

    def start(self):
        self.samples.append(self.read())
        def collect():
            while not self.event.wait(1.0):
                try:
                    self.samples.append(self.read())
                except Exception as error:
                    self.errors.append(str(error))
        self.thread = threading.Thread(target=collect, name="task-cpu-accounting", daemon=True)
        self.thread.start()

    def attach(self, child):
        # Popen owns the lifetime of this handle. A process handle remains valid
        # for CPU accounting after that process exits, until Popen closes it.
        self.child_handle = wintypes.HANDLE(int(child._handle))

    def finish(self):
        self.event.set()
        self.thread.join()
        try:
            self.samples.append(self.read())
        except Exception as error:
            self.errors.append(str(error))
        values = [self.percent(a, b, True) for a, b in zip(self.samples, self.samples[1:])]
        aggregate = self.percent(self.samples[0], self.samples[-1], True)
        return dict(samples=self.samples, errors=self.errors, backgroundCpuPercent=values,
                    backgroundMeanCpuPercent=aggregate,
                    passed=not self.errors and aggregate is not None and aggregate <= 15 and
                           all(x is not None and x <= 25 for x in values),
                    note="GetSystemTimes minus owned native and Python process CPU ticks; no subtraction from task elapsed time")


class LinuxCpuTelemetry:
    """cgroup v2 accounting plus the actual affinity CPU, never host core count
    as a substitute for the container quota. A formal run needs one pinned CPU.
    getrusage includes reaped children so short native processes are observed.
    """
    def __init__(self):
        import resource
        self.resource = resource
        self.affinity = sorted(os.sched_getaffinity(0))
        self.siblings = []
        if len(self.affinity) == 1:
            data = Path(f"/sys/devices/system/cpu/cpu{self.affinity[0]}/topology/thread_siblings_list").read_text().strip()
            self.siblings = [int(x) for x in data.split(",") if int(x) not in self.affinity]
        self.monitored_names = {f"cpu{i}" for i in self.affinity+self.siblings} | {"cpu"}
        self.clock_ticks = os.sysconf("SC_CLK_TCK")
        self.cgroup = Path("/sys/fs/cgroup")
        self.child = None
        self.samples, self.errors = [], []
        self.event = threading.Event()
        self.thread = None

    def read(self):
        cpu = {}
        for line in Path("/proc/stat").read_text().splitlines():
            fields = line.split()
            if fields and fields[0] in self.monitored_names:
                values = list(map(int, fields[1:9]))
                cpu[fields[0]] = dict(total=sum(values), idle=values[3]+values[4])
        statistics = {k: int(v) for k, v in (line.split() for line in (self.cgroup/"cpu.stat").read_text().splitlines())}
        quota, period = (self.cgroup/"cpu.max").read_text().split()
        running = self.child is not None and self.child.poll() is None
        own = self.resource.getrusage(self.resource.RUSAGE_SELF)
        children = self.resource.getrusage(self.resource.RUSAGE_CHILDREN)
        own_seconds = own.ru_utime + own.ru_stime + children.ru_utime + children.ru_stime
        if running:
            try:
                fields = Path(f"/proc/{self.child.pid}/stat").read_text().rsplit(") ", 1)[1].split()
                own_seconds += (int(fields[11])+int(fields[12]))/self.clock_ticks
            except FileNotFoundError:
                # wait/poll reaps the exit; refresh the completed-child count.
                self.child.poll()
                children = self.resource.getrusage(self.resource.RUSAGE_CHILDREN)
                own_seconds = own.ru_utime + own.ru_stime + children.ru_utime + children.ru_stime
        return dict(time=time.perf_counter(), cpu=cpu, ownedSeconds=own_seconds, cgroupCpu=statistics,
                    quotaCores=None if quota == "max" else int(quota)/int(period), affinity=self.affinity,
                    memoryCurrentBytes=int((self.cgroup/"memory.current").read_text()),
                    memoryMax=(self.cgroup/"memory.max").read_text().strip(),
                    cpuPressure=(self.cgroup/"cpu.pressure").read_text().strip())

    def interval(self, a, b, exclude_owned):
        wall = b["time"]-a["time"]
        own = max(0.0, b["ownedSeconds"]-a["ownedSeconds"]) if exclude_owned else 0.0
        total = sum(b["cpu"][f"cpu{i}"]["total"]-a["cpu"][f"cpu{i}"]["total"] for i in self.affinity)
        idle = sum(b["cpu"][f"cpu{i}"]["idle"]-a["cpu"][f"cpu{i}"]["idle"] for i in self.affinity)
        percent = max(0.0, 100*(total-idle-own*self.clock_ticks)/total) if total > 0 else None
        usage = (b["cgroupCpu"]["usage_usec"]-a["cgroupCpu"]["usage_usec"])/1e6
        quota = b["quotaCores"]
        sibling_loads = []
        for cpu in self.siblings:
            before, after = a["cpu"][f"cpu{cpu}"], b["cpu"][f"cpu{cpu}"]
            interval_total = after["total"]-before["total"]
            sibling_loads.append(100*(interval_total-(after["idle"]-before["idle"]))/interval_total if interval_total else 100)
        return dict(affinityBackgroundCpuPercent=percent, ownedCpuSeconds=own,
                    siblingCpuPercent=sibling_loads,
                    cgroupBackgroundCores=max(0.0, (usage-own)/wall),
                    cgroupUsagePercentOfQuota=100*usage/(wall*quota) if quota else None,
                    throttledUsec=b["cgroupCpu"].get("throttled_usec", 0)-a["cgroupCpu"].get("throttled_usec", 0),
                    throttledPeriods=b["cgroupCpu"].get("nr_throttled", 0)-a["cgroupCpu"].get("nr_throttled", 0))

    def qualify(self, snapshots, exclude_owned):
        values = [self.interval(a, b, exclude_owned) for a, b in zip(snapshots, snapshots[1:])]
        aggregate = self.interval(snapshots[0], snapshots[-1], exclude_owned)
        mean = aggregate["affinityBackgroundCpuPercent"]
        passed = (len(self.affinity) == 1 and mean is not None and mean <= 15 and
                  aggregate["cgroupBackgroundCores"] <= .25 and aggregate["throttledPeriods"] == 0 and
                  all(x <= 10 for x in aggregate["siblingCpuPercent"]) and
                  all(v["affinityBackgroundCpuPercent"] is not None and v["affinityBackgroundCpuPercent"] <= 25 for v in values))
        return dict(samples=snapshots, intervals=values, aggregate=aggregate, passed=passed,
                    affinity=self.affinity, note="One pinned logical CPU; mean background <=15%, interval <=25%, container background <=0.25 core, no cgroup throttling. No overhead subtraction.")

    def preflight(self):
        snapshots = [self.read()]
        for _ in range(3):
            time.sleep(.25)
            snapshots.append(self.read())
        return self.qualify(snapshots, False)

    def start(self):
        self.samples.append(self.read())
        def collect():
            while not self.event.wait(1.0):
                try:
                    self.samples.append(self.read())
                except Exception as error:
                    self.errors.append(str(error))
        self.thread = threading.Thread(target=collect, name="task-cgroup-accounting", daemon=True)
        self.thread.start()

    def attach(self, child):
        self.child = child

    def finish(self):
        self.event.set()
        self.thread.join()
        # /proc/stat uses scheduler ticks. Keep a fixed minimum final observation
        # interval instead of classifying a zero-tick short task as idle. This
        # post-task context is outside completeTaskMs and its wall cost remains
        # in the real calibration caller's total charge.
        context_delay = max(0.0, .25-(time.perf_counter()-self.samples[-1]["time"]))
        time.sleep(context_delay)
        try:
            self.samples.append(self.read())
            result = self.qualify(self.samples, True)
        except Exception as error:
            self.errors.append(str(error))
            result = dict(samples=self.samples, passed=False)
        result["errors"] = self.errors
        result["postTaskObservationSeconds"] = context_delay
        result["passed"] = result["passed"] and not self.errors
        return result


CpuTelemetry = WindowsCpuTelemetry if os.name == "nt" else LinuxCpuTelemetry
