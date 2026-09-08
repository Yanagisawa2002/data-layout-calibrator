// Functional model only. No native Unity allocator, workers, Burst, clocks or counters.
// The production sources are also compiled separately against actual Unity assemblies.
using System;
using System.Runtime.InteropServices;

namespace Unity.Burst
{
    public enum OptimizeFor { Performance }
    public enum FloatMode { Strict }
    [AttributeUsage(AttributeTargets.Struct)] public sealed class BurstCompileAttribute : Attribute
    { public OptimizeFor OptimizeFor; public FloatMode FloatMode; }
}
namespace Unity.Collections
{
    public enum Allocator { Temp, TempJob, Persistent }
    public enum NativeArrayOptions { UninitializedMemory, ClearMemory }
    [AttributeUsage(AttributeTargets.Field)] public sealed class ReadOnlyAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Field)] public sealed class WriteOnlyAttribute : Attribute { }
    public struct NativeArray<T> : IDisposable where T : struct
    {
        private sealed class Owner { public T[] Items; public bool Disposed; public int Reads, Writes; }
        private Owner _owner;
        public NativeArray(int length, Allocator allocator, NativeArrayOptions options = NativeArrayOptions.ClearMemory)
        { _owner = new Owner { Items = new T[length] }; }
        public int Length => _owner?.Items.Length ?? 0;
        public bool IsCreated => _owner != null && !_owner.Disposed;
        public int ModelReads => _owner.Reads;
        public int ModelWrites => _owner.Writes;
        public void ResetModelAccesses() { _owner.Reads = 0; _owner.Writes = 0; }
        public T this[int index]
        {
            get { Check(); _owner.Reads++; return _owner.Items[index]; }
            set { Check(); _owner.Writes++; _owner.Items[index] = value; }
        }
        private void Check() { if (!IsCreated) throw new ObjectDisposedException("NativeArray model"); }
        public void CopyFrom(NativeArray<T> source)
        {
            Check(); source.Check();
            if (Length != source.Length) throw new ArgumentException("Length mismatch");
            Array.Copy(source._owner.Items, _owner.Items, Length);
        }
        public void Dispose() { Check(); _owner.Disposed = true; }
    }
}
namespace Unity.Collections.LowLevel.Unsafe
{
    [AttributeUsage(AttributeTargets.Field)] public sealed class NativeDisableParallelForRestrictionAttribute : Attribute { }
    public static class UnsafeUtility { public static int SizeOf<T>() where T : struct => Marshal.SizeOf<T>(); }
}
namespace Unity.Jobs
{
    public interface IJobParallelFor { void Execute(int index); }
    public interface IJob { void Execute(); }
    public struct JobHandle
    {
        internal sealed class Node { public Action Body; public JobHandle Dependency; public bool Done; }
        internal Node Work;
        public void Complete()
        {
            ModelScheduler.Completes++;
            Finish();
        }
        private void Finish()
        {
            if (Work == null || Work.Done) return;
            Work.Dependency.Finish(); Work.Body(); Work.Done = true;
        }
    }
    public static class ModelScheduler
    {
        public static int Schedules, Completes;
        public static bool Reverse;
        public static void Reset(bool reverse) { Schedules = 0; Completes = 0; Reverse = reverse; }
        public static JobHandle Schedule<T>(this T job, int length, int batch, JobHandle dependency = default) where T : struct, IJobParallelFor
        {
            if (length < 0 || batch < 1) throw new ArgumentOutOfRangeException();
            bool reverse = Reverse;
            Schedules++;
            return new JobHandle { Work = new JobHandle.Node { Dependency = dependency, Body = () =>
            { for (int i = 0; i < length; i++) job.Execute(reverse ? length - i - 1 : i); } } };
        }
    }
}
