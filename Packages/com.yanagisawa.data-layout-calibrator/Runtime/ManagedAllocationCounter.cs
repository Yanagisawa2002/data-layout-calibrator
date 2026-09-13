using System;
using System.Runtime.CompilerServices;

namespace Yanagisawa.DataLayoutCalibrator
{
    /// <summary>Validated synchronous main-thread allocation windows. Instrumentation setup
    /// is outside the timed action. Unknown observations must throw, never become zero.</summary>
    public interface IManagedAllocationCounter
    {
        string Identity { get; }
        void Validate();
        void Begin();
        long End();
    }

    /// <summary>The .NET counter is accepted only after a real positive control.
    /// Unity runtimes that do not implement this API must inject a supported counter.</summary>
    public sealed class ThreadManagedAllocationCounter : IAllocationCounterCapabilities
    {
        private const int ControlBytes = 1_048_576;
        private readonly Func<long> _read;
        private readonly Action _positiveControl;
        private static object _escapedControl;
        private long _start;
        private int _threadId;
        private bool _active;
        private AllocationCounterCapability _capability;
        public string Identity => _capability.Provider + "; current-thread managed bytes; worker/native coverage unknown";
        public AllocationCounterCapability Capability => _capability.Snapshot();

        public ThreadManagedAllocationCounter() : this(GC.GetAllocatedBytesForCurrentThread, AllocateControl,
            "GC.GetAllocatedBytesForCurrentThread") { }

        // Injection permits deterministic capability tests without reading a runtime counter.
        public ThreadManagedAllocationCounter(Func<long> read, Action positiveControl, string provider)
        {
            _read = read ?? throw new ArgumentNullException(nameof(read));
            _positiveControl = positiveControl ?? throw new ArgumentNullException(nameof(positiveControl));
            _capability = new AllocationCounterCapability { Provider = provider, Unit = "bytes",
                Scope = AllocationScope.CurrentThreadManaged, PositiveControlMinimumBytes = ControlBytes };
        }

        public void Begin()
        {
            if (_active) throw new InvalidOperationException("Allocation windows cannot nest.");
            _threadId = Environment.CurrentManagedThreadId;
            _start = _read();
            if (_start < 0) throw new InvalidOperationException("Allocation observation unavailable.");
            _active = true;
        }
        public long End()
        {
            if (!_active) throw new InvalidOperationException("No active allocation window.");
            _active = false;
            if (_threadId != Environment.CurrentManagedThreadId)
                throw new InvalidOperationException("Current-thread counter window crossed a thread boundary.");
            long end = _read();
            if (end < _start) throw new InvalidOperationException("Allocation counter moved backwards or became unavailable.");
            return checked(end - _start);
        }
        public void Validate()
        {
            _capability.Availability = MeasurementAvailability.Unavailable;
            _capability.PositiveControlPassed = _capability.EmptyControlPassed = false;
            try
            {
                // Warm the exact precompiled escaping callsite before evaluating it.
                _positiveControl();
                Begin(); _positiveControl(); long positive = End();
                _capability.ObservedPositiveBytes = positive;
                if (positive < ControlBytes)
                    throw new NotSupportedException("Allocation counter failed 1 MiB positive control; a returned zero is not evidence.");
                _capability.PositiveControlPassed = true;
                Begin(); long empty = End();
                if (empty != 0) throw new NotSupportedException("Allocation counter failed empty control.");
                _capability.EmptyControlPassed = true;
                _capability.Availability = MeasurementAvailability.Available;
                _capability.Diagnostic = "Current-thread managed allocations only; workers/native not observed.";
            }
            catch (Exception exception)
            {
                _active = false;
                _capability.Diagnostic = exception.Message;
                throw;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void AllocateControl()
        {
            _escapedControl = new byte[ControlBytes];
            GC.KeepAlive(_escapedControl);
        }
    }
}
