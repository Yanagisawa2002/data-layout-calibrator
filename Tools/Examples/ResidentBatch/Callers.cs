using System;
using Yanagisawa.DataLayoutCalibrator.Batch;

namespace Yanagisawa.DataLayoutCalibrator.Examples.ResidentBatch
{
    /// <summary>A small functional receipt, not a timing or allocation measurement.</summary>
    public sealed class CallerReceipt
    {
        public string Caller { get; }
        public int ExportCount { get; }
        public int ConsumedPointCount { get; }
        public int CompletedSteps { get; }
        public int Capacity { get; }
        public BatchBounds3 Bounds { get; }
        public BatchPoint3[] FinalPoints { get; }
        public double ConsumerChecksum { get; }
        internal CallerReceipt(string caller, int exports, int consumed, ResidentTransformPipeline pipeline,
            BatchPoint3[] output, double checksum)
        {
            Caller = caller; ExportCount = exports; ConsumedPointCount = consumed;
            CompletedSteps = pipeline.CompletedSteps; Capacity = pipeline.Capacity; Bounds = pipeline.ReduceBounds();
            // Receipt creation is outside the pipeline and deliberately allocates a retained copy.
            FinalPoints = new BatchPoint3[pipeline.Count];
            Array.Copy(output, FinalPoints, pipeline.Count);
            ConsumerChecksum = checksum;
        }
    }

    public static class Callers
    {
        public static CallerReceipt SceneAnchors(PointStorageLayout layout)
        {
            var input = new[] {
                new BatchPoint3(-2, 0, 4), new BatchPoint3(1, 2, -3), new BatchPoint3(0, -1, 2),
                new BatchPoint3(6, 5, 0), new BatchPoint3(-4, 3, 1)
            };
            var output = new BatchPoint3[input.Length];
            var plan = new ResidentBatchPlan(layout, exportEverySteps: 2);
            using (var pipeline = new ResidentTransformPipeline(8, plan))
            {
                pipeline.Load(input, 0, input.Length);
                int exports = 0, consumed = 0; double checksum = 0;
                for (int step = 0; step < 3; step++)
                {
                    pipeline.Step(AffineTransform3.Translate(1, -2, 0.5));
                    if (pipeline.ExportIfDue(output))
                    { exports++; consumed += pipeline.Count; checksum += Consume(output, pipeline.Count); }
                }
                if (pipeline.ExportIfDue(output, finalRequest: true))
                { exports++; consumed += pipeline.Count; checksum += Consume(output, pipeline.Count); }
                return new CallerReceipt("scene-anchor coordinates", exports, consumed, pipeline, output, checksum);
            }
        }

        public static CallerReceipt PointCloudFrames(PointStorageLayout layout)
        {
            var frames = new[] {
                new[] { new BatchPoint3(-1, 2, 3), new BatchPoint3(4, -2, 1), new BatchPoint3(0, 0, 0),
                    new BatchPoint3(2, 4, -1), new BatchPoint3(-3, 1, 2) },
                new[] { new BatchPoint3(5, 1, 0), new BatchPoint3(-2, 3, 4), new BatchPoint3(1, -1, 2) }
            };
            var output = new BatchPoint3[7];
            var rotate = new AffineTransform3(new BatchPoint3(0, -1, 0), new BatchPoint3(1, 0, 0),
                new BatchPoint3(0, 0, 1), default);
            using (var pipeline = new ResidentTransformPipeline(7, new ResidentBatchPlan(layout)))
            {
                int exports = 0, consumed = 0; double checksum = 0;
                foreach (var frame in frames)
                {
                    // The second frame is shorter: do not consume the previous capacity tail.
                    pipeline.Load(frame, 0, frame.Length);
                    pipeline.Step(AffineTransform3.Translate(10, 0, -1));
                    pipeline.Step(rotate);
                    if (!pipeline.ExportIfDue(output, finalRequest: true))
                        throw new InvalidOperationException("Each loaded frame must produce a final snapshot.");
                    exports++; consumed += pipeline.Count; checksum += Consume(output, pipeline.Count);
                }
                return new CallerReceipt("point-cloud coordinate frames", exports, consumed, pipeline, output, checksum);
            }
        }

        private static double Consume(BatchPoint3[] points, int count)
        {
            double checksum = 0;
            for (int i = 0; i < count; i++) checksum += points[i].X + 2 * points[i].Y + 3 * points[i].Z;
            return checksum;
        }
    }
}
