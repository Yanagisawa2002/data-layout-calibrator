using System;

namespace Yanagisawa.DataLayoutCalibrator.Benchmark
{
    /// <summary>Adapts behaviorally validated Unity allocation events to the portable
    /// engine. Zero events establish zero bytes; unknown positive byte counts fail closed.</summary>
    internal sealed class UnityManagedAllocationCounter : IManagedAllocationCounter, IDisposable
    {
        private readonly UnityAllocationRecorder _recorder = new UnityAllocationRecorder();
        public string Identity => _recorder.Identity + "; unit=" + _recorder.Unit +
            "; positive and empty controls; zero bytes inferred only from zero events";
        public void Validate() => _recorder.ValidatePositiveAndEmptyControls();
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
