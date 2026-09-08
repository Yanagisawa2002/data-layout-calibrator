using System;

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
    public sealed class ThreadManagedAllocationCounter : IManagedAllocationCounter
    {
        private long _start;
        public string Identity => "GC.GetAllocatedBytesForCurrentThread; validated positive and empty controls; main-thread bytes";
        public void Begin() { _start = GC.GetAllocatedBytesForCurrentThread(); }
        public long End()
        {
            long bytes = GC.GetAllocatedBytesForCurrentThread() - _start;
            if (bytes < 0) throw new InvalidOperationException("Allocation counter moved backwards.");
            return bytes;
        }
        public void Validate()
        {
            Begin();
            GC.KeepAlive(new byte[4096]);
            if (End() < 4096) throw new InvalidOperationException("Managed allocation counter failed positive control; inject a supported counter.");
            Begin();
            if (End() != 0) throw new InvalidOperationException("Managed allocation counter failed empty control.");
        }
    }
}
