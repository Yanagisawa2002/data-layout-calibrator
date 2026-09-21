using System;
using Yanagisawa.DataLayoutCalibrator.Batch;

namespace Yanagisawa.DataLayoutCalibrator.Examples.ResidentBatch
{
    public static class Program
    {
        public static int Main()
        {
            Console.WriteLine("Functional resident-batch examples; synthetic coordinates, no benchmark or allocation measurement.");
            foreach (PointStorageLayout layout in new[] { PointStorageLayout.AoS, PointStorageLayout.SoA })
            {
                Print(layout, Callers.SceneAnchors(layout));
                Print(layout, Callers.PointCloudFrames(layout));
            }
            return 0;
        }

        private static void Print(PointStorageLayout layout, CallerReceipt receipt)
        {
            Console.WriteLine($"{layout}: {receipt.Caller}; exports={receipt.ExportCount}; consumed={receipt.ConsumedPointCount}; final-count={receipt.FinalPoints.Length}; retained-capacity={receipt.Capacity}");
        }
    }
}
