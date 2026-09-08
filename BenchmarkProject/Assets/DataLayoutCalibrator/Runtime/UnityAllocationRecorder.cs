using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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
        private static object _objectControl;
        private static object _stringControl;
        private readonly bool _valuesAreBytes;
        private readonly bool _native;

        internal const string Provider = "Unity.ProfilerRecorder/GC.Alloc/current-thread/unaggregated";
        internal string Identity => _native ? "NativeRuntimeProfiler/current-thread/object-size-bytes" : Provider;
        internal string Unit => _native ? "Bytes" : _recorder.UnitType.ToString();

        internal readonly struct Observation
        {
            internal readonly long Events;
            // -1 is explicitly unavailable, never an allocation-free claim.
            internal readonly long Bytes;
            internal Observation(long events, long bytes) { Events = events; Bytes = bytes; }
        }

        internal UnityAllocationRecorder()
        {
            // Compile/run the same escaping allocation callsite BEFORE installing
            // a native profiler, so controls also detect missed already-JITted paths.
            AllocateControls();
            // Deliberately omit SumAllSamplesInFrame and WrapAroundWhenCapacityReached.
            // This synchronous validator never advances a Player frame inside a measurement.
            _recorder = new ProfilerRecorder(ProfilerCategory.Internal, "GC.Alloc", 4096,
                ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
            if (!_recorder.Valid)
            {
                _recorder.Dispose();
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
#if ENABLE_IL2CPP
                const int backend = 1;
#else
                const int backend = 0;
#endif
                int status = NativeInitialize(backend);
                if (status != 0)
                    throw new NotSupportedException("GC.Alloc unavailable and native allocation profiler initialization failed: " + status);
                _native = true;
                _valuesAreBytes = true;
                return;
#else
                throw new NotSupportedException("GC.Alloc recorder is unavailable in this Player; zero allocation cannot be established.");
#endif
            }
            _valuesAreBytes = _recorder.UnitType == ProfilerMarkerDataUnit.Bytes;
        }

        internal void Begin()
        {
            if (_native)
            {
                if (NativeBegin() != 0) throw new InvalidOperationException("Native allocation window could not begin.");
                return;
            }
            _recorder.Reset();
            _recorder.Start();
            if (!_recorder.IsRunning)
                throw new NotSupportedException("GC.Alloc recorder failed to start.");
        }

        internal Observation End()
        {
            if (_native)
            {
                if (NativeEnd(out long events, out long allocatedBytes) != 0)
                    throw new InvalidOperationException("Native allocation window is invalid or overflowed.");
                return new Observation(events, allocatedBytes);
            }
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
            AllocateControls();
            End();
            Begin();
            AllocateControls();
            Observation positive = End();
            if (positive.Events < 4 || (_valuesAreBytes && positive.Bytes < 1_048_577))
                throw new NotSupportedException("Allocation provider failed precompiled array/object/string positive controls; zero cannot be evidence.");
            Begin();
            Observation empty = End();
            if (empty.Events != 0)
                throw new InvalidOperationException("GC.Alloc empty control is contaminated or Reset retained stale samples.");
            GC.KeepAlive(_smallControl);
            GC.KeepAlive(_largeControl);
            GC.KeepAlive(_objectControl);
            GC.KeepAlive(_stringControl);
            return positive;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void AllocateControls()
        {
            _smallControl = new byte[1];
            _largeControl = new byte[1_048_576];
            _objectControl = new object();
            _stringControl = new string('x', 37);
        }

        [DllImport("DlcAllocationProfiler", EntryPoint = "dlc_allocation_initialize", CallingConvention = CallingConvention.Cdecl)]
        private static extern int NativeInitialize(int backend);
        [DllImport("DlcAllocationProfiler", EntryPoint = "dlc_allocation_begin", CallingConvention = CallingConvention.Cdecl)]
        private static extern int NativeBegin();
        [DllImport("DlcAllocationProfiler", EntryPoint = "dlc_allocation_end", CallingConvention = CallingConvention.Cdecl)]
        private static extern int NativeEnd(out long events, out long bytes);

        public void Dispose()
        {
            if (_native) NativeEnd(out _, out _); // Stop an interrupted window, retaining runtime callback lifetime.
            else _recorder.Dispose();
        }
    }
}
