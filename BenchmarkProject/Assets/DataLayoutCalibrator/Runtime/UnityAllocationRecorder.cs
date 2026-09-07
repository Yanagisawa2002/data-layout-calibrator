using System;
using Unity.Profiling;
using Unity.Profiling.LowLevel;

namespace Yanagisawa.DataLayoutCalibrator.Benchmark
{
    /// <summary>Counts actual allocation marker events in a synchronous main-thread window.
    /// Availability is established behaviorally in the running Player, not inferred from a zero.</summary>
    internal sealed class UnityAllocationRecorder : IDisposable
    {
        private ProfilerRecorder _recorder;
        private static object _smallControl;
        private static object _largeControl;
        private readonly bool _valuesAreBytes;

        internal const string Provider = "Unity.ProfilerRecorder/GC.Alloc/current-thread/unaggregated";
        internal string Unit => _recorder.UnitType.ToString();

        internal readonly struct Observation
        {
            internal readonly long Events;
            // -1 is explicitly unavailable, never an allocation-free claim.
            internal readonly long Bytes;
            internal Observation(long events, long bytes) { Events = events; Bytes = bytes; }
        }

        internal UnityAllocationRecorder()
        {
            // Deliberately omit SumAllSamplesInFrame and WrapAroundWhenCapacityReached.
            // This synchronous validator never advances a Player frame inside a measurement.
            _recorder = new ProfilerRecorder(ProfilerCategory.Internal, "GC.Alloc", 4096,
                ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
            if (!_recorder.Valid)
            {
                _recorder.Dispose();
                throw new NotSupportedException("GC.Alloc recorder is unavailable in this Player; zero allocation cannot be established.");
            }
            _valuesAreBytes = _recorder.UnitType == ProfilerMarkerDataUnit.Bytes;
        }

        internal void Begin()
        {
            _recorder.Reset();
            _recorder.Start();
            if (!_recorder.IsRunning)
                throw new NotSupportedException("GC.Alloc recorder failed to start.");
        }

        internal Observation End()
        {
            _recorder.Stop();
            int samples = _recorder.Count;
            if (_recorder.WrappedAround || samples >= _recorder.Capacity)
                throw new InvalidOperationException("GC.Alloc recorder capacity exceeded; allocation observation is incomplete.");
            long bytes = _valuesAreBytes ? 0 : -1;
            if (_valuesAreBytes)
                for (int i = 0; i < samples; i++) bytes = checked(bytes + _recorder.GetSample(i).Value);
            return new Observation(samples, bytes);
        }

        internal Observation ValidatePositiveAndEmptyControls()
        {
            // Warm the recorder control/read APIs before accepting an empty window.
            Begin();
            _smallControl = new byte[1];
            _largeControl = new byte[4096];
            End();
            Begin();
            _smallControl = new byte[1];
            _largeControl = new byte[4096];
            Observation positive = End();
            if (positive.Events < 2 || (_valuesAreBytes && positive.Bytes < 4097))
                throw new NotSupportedException("GC.Alloc failed its small-object/large-array positive control; an empty recorder is not evidence.");
            Begin();
            Observation empty = End();
            if (empty.Events != 0)
                throw new InvalidOperationException("GC.Alloc empty control is contaminated or Reset retained stale samples.");
            GC.KeepAlive(_smallControl);
            GC.KeepAlive(_largeControl);
            return positive;
        }

        public void Dispose() => _recorder.Dispose();
    }
}
