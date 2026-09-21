using System;

namespace Yanagisawa.DataLayoutCalibrator.Batch
{
    /// <summary>Immutable caller-selected configuration, NOT a calibrated or deployment
    /// profile. Periodic exports occur after positive multiples of ExportEverySteps.
    /// A final request exports the current version at most once, including at step zero.</summary>
    public sealed class ResidentBatchPlan
    {
        public PointStorageLayout Layout { get; }
        public int ExportEverySteps { get; }
        public bool ExportOnFinalRequest { get; }

        public ResidentBatchPlan(PointStorageLayout layout = PointStorageLayout.AoS,
            int exportEverySteps = 0, bool exportOnFinalRequest = true)
        {
            ResidentPointBatch.ValidateLayout(layout);
            if (exportEverySteps < 0) throw new ArgumentOutOfRangeException(nameof(exportEverySteps));
            Layout = layout; ExportEverySteps = exportEverySteps; ExportOnFinalRequest = exportOnFinalRequest;
        }

        public bool ShouldExport(int completedSteps, bool finalRequest = false)
        {
            if (completedSteps < 0) throw new ArgumentOutOfRangeException(nameof(completedSteps));
            return (ExportEverySteps > 0 && completedSteps > 0 && completedSteps % ExportEverySteps == 0)
                || (finalRequest && ExportOnFinalRequest);
        }
    }

    /// <summary>Fixed pipeline: transform current coordinates, reduce point bounds,
    /// then let the caller request a due export. No timers, search, worker pool or
    /// hidden export buffer. Not thread-safe; the caller owns the input/output buffers.</summary>
    public sealed class ResidentTransformPipeline : IDisposable
    {
        private readonly ResidentPointBatch storage;
        private int completedSteps;
        private long lastExportVersion = -1;
        public ResidentBatchPlan Plan { get; }
        public int Count => storage.Count;
        public int Capacity => storage.Capacity;
        public int CompletedSteps { get { CheckAlive(); return completedSteps; } }
        public long OwnedCoordinatePayloadBytes => storage.OwnedCoordinatePayloadBytes;
        public bool IsDisposed => storage.IsDisposed;

        public ResidentTransformPipeline(int capacity, ResidentBatchPlan plan)
        {
            Plan = plan ?? throw new ArgumentNullException(nameof(plan));
            storage = new ResidentPointBatch(capacity, plan.Layout);
        }

        public void Reserve(int minimumCapacity) => storage.Reserve(minimumCapacity);

        /// <summary>Successful ingress starts a new sequence. Failed ingress preserves
        /// both the resident data and the prior cadence/export state.</summary>
        public void Load(BatchPoint3[] input, int offset, int count)
        {
            storage.Load(input, offset, count);
            completedSteps = 0; lastExportVersion = -1;
        }

        public BatchBounds3 Step(AffineTransform3 transform)
        {
            CheckAlive();
            int nextStep = checked(completedSteps + 1);
            storage.Transform(transform);
            var bounds = storage.ReduceBounds();
            completedSteps = nextStep;
            return bounds;
        }

        public BatchBounds3 ReduceBounds() => storage.ReduceBounds();

        /// <summary>Call after each step to observe periodic output. Missed output events
        /// are not queued or replayed. finalRequest requests a terminal snapshot but does
        /// not close the pipeline. A failed copy does not consume an export opportunity.
        /// When nothing is due, destination is not inspected or written.</summary>
        public bool ExportIfDue(BatchPoint3[] destination, int offset = 0, bool finalRequest = false)
        {
            CheckAlive();
            if (!Plan.ShouldExport(completedSteps, finalRequest) || lastExportVersion == storage.Version) return false;
            storage.CopyTo(destination, offset);
            lastExportVersion = storage.Version;
            return true;
        }

        private void CheckAlive()
        {
            if (storage.IsDisposed) throw new ObjectDisposedException(nameof(ResidentTransformPipeline));
        }

        public void Dispose() => storage.Dispose();
    }
}
