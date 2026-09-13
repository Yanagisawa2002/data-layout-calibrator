using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Yanagisawa.DataLayoutCalibrator
{
    /// <summary>Additive schema: old fields retain their exact recorded values.
    /// None of these three estimands can be substituted for another.</summary>
    [Serializable]
    public sealed class TimingMeasurementContract
    {
        public int SchemaVersion = 1;
        public string ResidentMetric = "p95-of-block-mean-ms-per-tick";
        public string SelectionMetric = "sum-of-component-p95-amortized-score-ms-per-tick";
        public string QuantileConvention = "linear-interpolation-at-(n-1)*p";
        public bool HistoricalSemantics;
        public bool IndividualTicksAvailable;
        public bool CompleteLifecyclesAvailable;
        public static TimingMeasurementContract Historical() => new TimingMeasurementContract { HistoricalSemantics = true };

        public void Validate()
        {
            if (SchemaVersion != 1 || ResidentMetric != "p95-of-block-mean-ms-per-tick" ||
                SelectionMetric != "sum-of-component-p95-amortized-score-ms-per-tick" ||
                QuantileConvention != "linear-interpolation-at-(n-1)*p")
                throw new ArgumentException("Unknown timing semantics; never reinterpret historical values.");
        }
    }

    public enum TimingObservationOrigin { Unknown = 0, SyntheticFixture = 1, Observed = 2 }
    public enum LifecycleMeasurementScope { IngressResidentExport = 0, ConstructionIngressResidentExportDisposal = 1 }

    public interface IMeasurementClock
    {
        TimingObservationOrigin Origin { get; }
        long Frequency { get; }
        long ReadTimestamp();
    }

    public sealed class StopwatchMeasurementClock : IMeasurementClock
    {
        public TimingObservationOrigin Origin => TimingObservationOrigin.Observed;
        public long Frequency => Stopwatch.Frequency;
        public long ReadTimestamp() => Stopwatch.GetTimestamp();
    }

    [Serializable]
    public sealed class LifecycleObservation
    {
        public int SchemaVersion = 1;
        public TimingObservationOrigin Origin;
        public LifecycleMeasurementScope Scope;
        public bool Completed;
        public string ProcessId;
        public string PartitionId;
        public string LifecycleId;
        public string CandidateId;
        public string DatasetHash;
        public string SourceFingerprint;
        public long TimestampFrequency;
        public long IngressDuration;
        public long ConstructionDuration;
        public long DisposalDuration;
        public long[] TickDurations;
        public long ExportDuration;
        public long CompleteDuration;
        public double[] TickMilliseconds()
        {
            Validate();
            var values = new double[TickDurations.Length];
            for (int i = 0; i < values.Length; i++) values[i] = TickDurations[i] * (1000d / TimestampFrequency);
            return values;
        }
        public void Validate(bool requireComplete = true)
        {
            if ((requireComplete && !Completed) || SchemaVersion != 1 || (Origin != TimingObservationOrigin.SyntheticFixture && Origin != TimingObservationOrigin.Observed) ||
                TimestampFrequency <= 0 || TickDurations == null || TickDurations.Length == 0 ||
                string.IsNullOrWhiteSpace(ProcessId) || string.IsNullOrWhiteSpace(PartitionId) ||
                string.IsNullOrWhiteSpace(LifecycleId) || string.IsNullOrWhiteSpace(CandidateId) ||
                string.IsNullOrWhiteSpace(DatasetHash) || string.IsNullOrWhiteSpace(SourceFingerprint) ||
                IngressDuration < 0 || ExportDuration < 0 || CompleteDuration < 0 ||
                ConstructionDuration < 0 || DisposalDuration < 0 ||
                (Scope != LifecycleMeasurementScope.IngressResidentExport && Scope != LifecycleMeasurementScope.ConstructionIngressResidentExportDisposal) ||
                (Scope == LifecycleMeasurementScope.IngressResidentExport && (ConstructionDuration != 0 || DisposalDuration != 0)))
                throw new ArgumentException("Incomplete lifecycle identity, units or observations.");
            long sum = checked(IngressDuration + ExportDuration + ConstructionDuration + DisposalDuration);
            foreach (long tick in TickDurations)
            {
                if (tick < 0) throw new ArgumentException("Timestamp moved backwards.");
                sum = checked(sum + tick);
            }
            if (CompleteDuration < sum) throw new ArgumentException("Complete window does not enclose every component.");
        }
    }

    /// <summary>Preallocates outside the window. Execute(1) is timed separately;
    /// CompleteDuration encloses ingress, every tick, export, and instrumentation.
    /// Storage creation/destruction is excluded and must enter the separate setup cost model.</summary>
    public static class LifecycleCollector
    {
        public static void Collect(ICalibrationCandidate candidate, float deltaTime,
            LifecycleObservation destination, IMeasurementClock clock)
        {
            if (candidate == null || destination == null || clock == null) throw new ArgumentNullException();
            if (!(deltaTime > 0) || float.IsInfinity(deltaTime) || destination.TickDurations == null ||
                destination.TickDurations.Length == 0 || clock.Frequency <= 0 ||
                destination.CandidateId != candidate.Descriptor.CandidateId)
                throw new ArgumentException("Invalid collector buffer, candidate or clock.");
            destination.Completed = false;
            destination.Origin = clock.Origin;
            destination.Scope = LifecycleMeasurementScope.IngressResidentExport;
            destination.TimestampFrequency = clock.Frequency;
            destination.ConstructionDuration = destination.DisposalDuration = 0;
            destination.IngressDuration = destination.ExportDuration = destination.CompleteDuration = 0;
            Array.Clear(destination.TickDurations, 0, destination.TickDurations.Length);
            destination.Validate(false);
            long start = clock.ReadTimestamp();
            long component = clock.ReadTimestamp();
            candidate.BoundaryCost.Ingress();
            destination.IngressDuration = checked(clock.ReadTimestamp() - component);
            for (int i = 0; i < destination.TickDurations.Length; i++)
            {
                component = clock.ReadTimestamp();
                candidate.Execute(1, deltaTime);
                destination.TickDurations[i] = checked(clock.ReadTimestamp() - component);
            }
            component = clock.ReadTimestamp();
            candidate.BoundaryCost.Export();
            destination.ExportDuration = checked(clock.ReadTimestamp() - component);
            destination.CompleteDuration = checked(clock.ReadTimestamp() - start);
            destination.Validate(false);
            destination.Completed = true;
        }

        /// <summary>The factory constructs one candidate from frozen canonical input.
        /// CompleteDuration includes construction, ingress, ticks, export and disposal.
        /// The caller owns the preallocated destination and identity; this is an explicit collection call.</summary>
        public static void CollectOwned(Func<ICalibrationCandidate> factory, float deltaTime,
            LifecycleObservation destination, IMeasurementClock clock)
        {
            if (factory == null || destination == null || clock == null) throw new ArgumentNullException();
            destination.Completed = false;
            long start = clock.ReadTimestamp();
            ICalibrationCandidate candidate = factory();
            long constructed = clock.ReadTimestamp();
            long disposal = 0;
            try { Collect(candidate, deltaTime, destination, clock); }
            finally
            {
                destination.Completed = false;
                long disposeStart = clock.ReadTimestamp();
                candidate?.Dispose();
                disposal = checked(clock.ReadTimestamp() - disposeStart);
            }
            destination.Scope = LifecycleMeasurementScope.ConstructionIngressResidentExportDisposal;
            destination.ConstructionDuration = checked(constructed - start);
            destination.DisposalDuration = disposal;
            destination.CompleteDuration = checked(clock.ReadTimestamp() - start);
            destination.Validate(false);
            destination.Completed = true;
        }
    }

    public static class LifecycleStatistics
    {
        // Functional aggregation only. Repeated lifecycles stay paired by identity;
        // individual ticks are not independent process replications.
        public static LatencySummary CompleteMilliseconds(LifecycleObservation[] observations)
        {
            if (observations == null || observations.Length == 0) throw new ArgumentException("No complete lifecycles.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var values = new double[observations.Length];
            LifecycleObservation first = observations[0];
            if (first == null) throw new ArgumentException("Null lifecycle.");
            for (int i = 0; i < values.Length; i++)
            {
                LifecycleObservation item = observations[i] ?? throw new ArgumentException("Null lifecycle.");
                item.Validate();
                if (!ids.Add(item.ProcessId + "\n" + item.PartitionId + "\n" + item.LifecycleId) ||
                    item.CandidateId != first.CandidateId || item.DatasetHash != first.DatasetHash ||
                    item.SourceFingerprint != first.SourceFingerprint || item.Origin != first.Origin || item.Scope != first.Scope ||
                    item.TickDurations.Length != first.TickDurations.Length)
                    throw new ArgumentException("Duplicate lifecycle or incompatible workload/source/origin.");
                values[i] = item.CompleteDuration * (1000d / item.TimestampFrequency);
            }
            return BenchmarkStatistics.Calculate(values, values.Length, new double[values.Length]);
        }
    }
}
