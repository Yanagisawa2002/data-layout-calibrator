using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Jobs;

namespace Yanagisawa.DataLayoutCalibrator.Benchmark
{
    [BurstCompile(CompileSynchronously = true)]
    internal struct EnvelopeBurstIsaProbe : IJob
    {
        public NativeArray<int> Result;
        public void Execute()
        {
            if (X86.Avx2.IsAvx2Supported) Result[0] = 3;
            else if (X86.Sse2.IsSse2Supported) Result[0] = 1;
            else Result[0] = 0;
        }

        public static int Capture()
        {
            using (var result = new NativeArray<int>(1, Allocator.TempJob))
            {
                new EnvelopeBurstIsaProbe { Result = result }.Schedule().Complete();
                return result[0];
            }
        }
    }
}
