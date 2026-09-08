using System;

namespace Yanagisawa.DataLayoutCalibrator
{
    [Flags]
    public enum AllocationScope
    {
        None = 0,
        CurrentThreadManaged = 1,
        WorkerThreadsManaged = 2,
        Native = 4,
    }

    public enum MeasurementAvailability { Unknown = 0, Available = 1, Unavailable = 2 }

    /// <summary>A positive control proves only the named scope and unit. A native
    /// profiler reporting managed object sizes does not measure native allocations.</summary>
    [Serializable]
    public sealed class AllocationCounterCapability
    {
        public int SchemaVersion = 1;
        public MeasurementAvailability Availability;
        public AllocationScope Scope;
        public string Provider;
        public string Unit;
        public bool PositiveControlPassed;
        public bool EmptyControlPassed;
        public long PositiveControlMinimumBytes;
        public long ObservedPositiveBytes = -1;
        public string Diagnostic;

        public bool Covers(AllocationScope required) => SchemaVersion == 1 &&
            Availability == MeasurementAvailability.Available && required != AllocationScope.None &&
            (required & ~(AllocationScope.CurrentThreadManaged | AllocationScope.WorkerThreadsManaged | AllocationScope.Native)) == 0 &&
            (Scope & ~(AllocationScope.CurrentThreadManaged | AllocationScope.WorkerThreadsManaged | AllocationScope.Native)) == 0 &&
            (Scope & required) == required && PositiveControlPassed && EmptyControlPassed &&
            !string.IsNullOrWhiteSpace(Provider) &&
            ((Unit == "bytes" && PositiveControlMinimumBytes > 0 && ObservedPositiveBytes >= PositiveControlMinimumBytes) ||
             Unit == "allocation-events-zero-only");

        public AllocationCounterCapability Snapshot() => (AllocationCounterCapability)MemberwiseClone();
    }

    /// <summary>Older providers remain source compatible but cannot establish eligibility
    /// until they supply explicit behaviorally validated capabilities.</summary>
    public interface IAllocationCounterCapabilities : IManagedAllocationCounter
    {
        AllocationCounterCapability Capability { get; }
    }

    public static class AllocationMeasurementGate
    {
        public static AllocationCounterCapability ValidateAndSnapshot(
            IManagedAllocationCounter counter, AllocationScope required)
        {
            if (!(counter is IAllocationCounterCapabilities provider))
                throw new NotSupportedException("Allocation coverage is unknown; inject a capability-aware provider.");
            provider.Validate();
            AllocationCounterCapability capability = provider.Capability;
            if (capability == null || !capability.Covers(required))
                throw new NotSupportedException("Allocation counter unavailable or insufficient scope; zero cannot establish eligibility.");
            return capability.Snapshot();
        }

        public static bool IsZeroAllocationEstablished(LayoutBenchmarkResult result) =>
            result != null && result.AllocationCapability != null &&
            result.AllocationCapability.Covers(result.RequiredAllocationScope) &&
            result.AllocationWindowsComplete && result.HotPathManagedAllocationBytes == 0 &&
            result.BoundaryManagedAllocationBytes == 0;
    }
}
