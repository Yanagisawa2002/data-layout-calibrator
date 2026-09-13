using System;

namespace Yanagisawa.DataLayoutCalibrator.Benchmark
{
    /// <summary>Adapts behaviorally validated Unity allocation events to the portable
    /// engine. Zero events establish zero bytes; unknown positive byte counts fail closed.</summary>
    internal sealed class UnityManagedAllocationCounter : IAllocationCounterCapabilities, IDisposable
    {
        private readonly UnityAllocationRecorder _recorder = new UnityAllocationRecorder();
        public string Identity => _recorder.Identity + "; unit=" + _recorder.Unit +
            "; positive and empty controls; zero bytes inferred only from zero events";
        private AllocationCounterCapability _capability = new AllocationCounterCapability();
        public AllocationCounterCapability Capability => _capability.Snapshot();
        public void Validate()
        {
            _capability = new AllocationCounterCapability { Provider = Identity,
                Scope = AllocationScope.CurrentThreadManaged, Unit = "allocation-events-zero-only",
                Availability = MeasurementAvailability.Unavailable };
            var positive = _recorder.ValidatePositiveAndEmptyControls();
            _capability.ObservedPositiveBytes = positive.Bytes;
            _capability.PositiveControlPassed = _capability.EmptyControlPassed = true;
            _capability.Availability = MeasurementAvailability.Available;
            _capability.Diagnostic = "Current-thread managed allocation events only. Native profiler implementation is not native allocation coverage.";
        }
        public void Begin() => _recorder.Begin();
        public long End()
        {
            var observation = _recorder.End();
            if (observation.Events == 0) return 0;
            if (observation.Events < 0 || observation.Bytes <= 0)
                throw new InvalidOperationException("Managed allocation observed but its byte count is unavailable; candidate cannot pass.");
            return observation.Bytes;
        }
        public void Dispose() => _recorder.Dispose();
    }
}
