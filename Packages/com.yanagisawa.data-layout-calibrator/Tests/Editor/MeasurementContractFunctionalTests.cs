using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Yanagisawa.DataLayoutCalibrator.Tests
{
    internal static class FunctionalEvidenceFixtures
    {
        internal static AllocationCounterCapability ThreadCapability() => new AllocationCounterCapability
        {
            Availability = MeasurementAvailability.Available, Scope = AllocationScope.CurrentThreadManaged,
            Provider = "synthetic-fixture-only", Unit = "bytes", PositiveControlPassed = true, EmptyControlPassed = true,
            PositiveControlMinimumBytes = 1048576, ObservedPositiveBytes = 1048576,
        };
    }

    public sealed class MeasurementContractFunctionalTests
    {
        [Test]
        public void PositiveControl_ZeroForOneMiBIsUnavailable()
        {
            var counter = new ThreadManagedAllocationCounter(() => 0, () => { }, "mock-zero");
            Assert.Throws<NotSupportedException>(() => counter.Validate());
            Assert.That(counter.Capability.Availability, Is.EqualTo(MeasurementAvailability.Unavailable));
            Assert.That(counter.Capability.Covers(AllocationScope.CurrentThreadManaged), Is.False);
        }

        [Test]
        public void PositiveControl_InjectedBytesAndEmptyWindowAreValidated()
        {
            long simulated = 0;
            var counter = new ThreadManagedAllocationCounter(() => simulated, () => simulated += 1048576, "mock-bytes");
            var snapshot = AllocationMeasurementGate.ValidateAndSnapshot(counter, AllocationScope.CurrentThreadManaged);
            Assert.That(snapshot.Covers(AllocationScope.CurrentThreadManaged), Is.True);
            snapshot.Availability = MeasurementAvailability.Unavailable;
            Assert.That(counter.Capability.Availability, Is.EqualTo(MeasurementAvailability.Available));
            Assert.Throws<NotSupportedException>(() => AllocationMeasurementGate.ValidateAndSnapshot(counter, AllocationScope.WorkerThreadsManaged));
            Assert.Throws<NotSupportedException>(() => AllocationMeasurementGate.ValidateAndSnapshot(counter, AllocationScope.Native));
        }

        [Test]
        public void Counter_RejectsBackwardAndContaminatedEmptyWindows()
        {
            long value = 9;
            var backward = new ThreadManagedAllocationCounter(() => value, () => { }, "mock");
            backward.Begin(); value = 1;
            Assert.Throws<InvalidOperationException>(() => backward.End());
            long count = 0;
            var noisy = new ThreadManagedAllocationCounter(() => ++count, () => count += 1048576, "mock-noisy");
            Assert.Throws<NotSupportedException>(() => noisy.Validate());
            Assert.That(noisy.Capability.EmptyControlPassed, Is.False);
        }

        [TestCase(AllocationScope.None)]
        [TestCase(AllocationScope.WorkerThreadsManaged)]
        [TestCase(AllocationScope.Native)]
        [TestCase((AllocationScope)8)]
        public void NumericZeroWithoutRequiredCoverageCannotBeEligible(AllocationScope required)
        {
            var result = Result(true, 10);
            result.RequiredAllocationScope = required;
            Assert.That(LayoutSelector.IsEligible(result), Is.False);
        }

        [Test]
        public void MissingAndIncompleteAllocationEvidenceFailClosed()
        {
            var result = Result(true, 10);
            Assert.That(LayoutSelector.IsEligible(result), Is.True);
            result.AllocationWindowsComplete = false;
            Assert.That(LayoutSelector.IsEligible(result), Is.False);
            result.AllocationWindowsComplete = true; result.AllocationCapability = null;
            Assert.That(LayoutSelector.IsEligible(result), Is.False);
        }

        [Test]
        public void TimingMigrationPreservesEveryHistoricalNumberAndDecision()
        {
            var result = Result(true, 10); result.AllocationCapability = null; result.TimingContract = null;
            var profile = new ScenarioCalibrationProfile { SchemaVersion = 2, CalibrationResults = new[] { result },
                FinalDecision = new LayoutSelectionDecision { ImprovementPercent = 12.345, Status = LayoutSelectionStatus.Optimized } };
            var latency = result.AmortizedLatency;
            CalibrationProfileMigration.DescribeTimingInMemory(profile);
            Assert.That(result.AmortizedLatency, Is.EqualTo(latency));
            Assert.That(profile.FinalDecision.ImprovementPercent, Is.EqualTo(12.345));
            Assert.That(profile.FinalDecision.Status, Is.EqualTo(LayoutSelectionStatus.Optimized));
            Assert.That(result.AllocationCapability, Is.Null);
            Assert.That(profile.TimingContract.HistoricalSemantics, Is.True);
            Assert.That(profile.TimingContract.IndividualTicksAvailable, Is.False);
        }

        [Test]
        public void PerTickP95IsNotBlockMeanP95OrComponentScore()
        {
            var ticks = new[] { 0d, 100d };
            var perTick = BenchmarkStatistics.Calculate(ticks, 2, new double[2]);
            var blocks = BenchmarkStatistics.Calculate(new[] { 50d, 50d }, 2, new double[2]);
            Assert.That(perTick.P95Milliseconds, Is.EqualTo(95));
            Assert.That(blocks.P95Milliseconds, Is.EqualTo(50));
            Assert.That(BenchmarkStatistics.CalculateAmortizedP95MillisecondsPerTick(new[] { 50d, 50d }, new[] { 10d, 10d }, new[] { 30d, 30d }, 2), Is.EqualTo(70));
        }

        [Test]
        public void VirtualClockCollectsWholeWindowAndTicksWithAssociation()
        {
            var observation = Observation();
            var candidate = new FixtureCandidate();
            LifecycleCollector.Collect(candidate, 1, observation, new FixtureClock(0, 1, 11, 12, 32, 33, 63, 64, 104, 110));
            Assert.That(candidate.ExecutedTicks, Is.EqualTo(2));
            Assert.That(observation.TickMilliseconds(), Is.EqualTo(new[] { 20d, 30d }));
            Assert.That(observation.CompleteDuration, Is.EqualTo(110));
            Assert.That(observation.Origin, Is.EqualTo(TimingObservationOrigin.SyntheticFixture));
            Assert.That(LifecycleStatistics.CompleteMilliseconds(new[] { observation }).P95Milliseconds, Is.EqualTo(110));
        }

        [Test]
        public void OwnedLifecycleIncludesConstructionAndDisposalInCompleteWindow()
        {
            var observation = Observation();
            LifecycleCollector.CollectOwned(() => new FixtureCandidate(), 1, observation,
                new FixtureClock(0, 10, 11, 12, 22, 23, 43, 44, 74, 75, 115, 120, 121, 130, 140));
            Assert.That(observation.Scope, Is.EqualTo(LifecycleMeasurementScope.ConstructionIngressResidentExportDisposal));
            Assert.That(observation.ConstructionDuration, Is.EqualTo(10));
            Assert.That(observation.DisposalDuration, Is.EqualTo(9));
            Assert.That(observation.CompleteDuration, Is.EqualTo(140));
            Assert.That(observation.Completed, Is.True);
        }

        [Test]
        public void InterruptedCollectorCannotPublishZeroAsComplete()
        {
            var observation = Observation();
            Assert.Throws<InvalidOperationException>(() => LifecycleCollector.Collect(new FixtureCandidate(), 1,
                observation, new FixtureClock(0, 1)));
            Assert.That(observation.Completed, Is.False);
            Assert.Throws<ArgumentException>(() => LifecycleStatistics.CompleteMilliseconds(new[] { observation }));
        }

        [Test]
        public void LifecycleRejectsMissingEnclosureDuplicateAndMixedSource()
        {
            var a = Observation(); a.TickDurations = new long[] { 10, 20 }; a.CompleteDuration = 29;
            Assert.Throws<ArgumentException>(() => a.Validate()); a.CompleteDuration = 30;
            Assert.Throws<ArgumentException>(() => LifecycleStatistics.CompleteMilliseconds(new[] { a, a }));
            var b = Observation(); b.LifecycleId = "2"; b.SourceFingerprint = "different";
            Assert.Throws<ArgumentException>(() => LifecycleStatistics.CompleteMilliseconds(new[] { a, b }));
        }

        [Test]
        public void HoldoutRejectsReusedPartitionAndDataset()
        {
            var baseline = Result(true, 10); var candidate = Result(false, 5);
            var decision = LayoutSelector.SelectCalibration(new[] { baseline, candidate }, 2, bootstrapIterations: 100);
            Assert.That(decision.Status, Is.EqualTo(LayoutSelectionStatus.Optimized));
            baseline.Phase = candidate.Phase = BenchmarkPhase.Holdout;
            Assert.That(LayoutSelector.ConfirmHoldout(decision, baseline, candidate, bootstrapIterations: 100).Status, Is.EqualTo(LayoutSelectionStatus.Inconclusive));
            baseline.EvidencePartitionId = candidate.EvidencePartitionId = "holdout";
            baseline.DatasetHash = candidate.DatasetHash = "fresh"; baseline.DatasetSeed = candidate.DatasetSeed = 2;
            Assert.That(LayoutSelector.ConfirmHoldout(decision, baseline, candidate, bootstrapIterations: 100).Status, Is.EqualTo(LayoutSelectionStatus.Optimized));
        }

        [Test]
        public void PairedInferenceRejectsMixedBuildAndPartition()
        {
            var baseline = Result(true, 10); var candidate = Result(false, 5);
            candidate.SourceFingerprint = new string('E', 64);
            Assert.Throws<ArgumentException>(() => BenchmarkStatistics.BootstrapAmortizedP95Improvement(baseline, candidate, 100));
            candidate.SourceFingerprint = baseline.SourceFingerprint; candidate.EvidencePartitionId = "other";
            Assert.Throws<ArgumentException>(() => BenchmarkStatistics.BootstrapAmortizedP95Improvement(baseline, candidate, 100));
        }

        [TestCase(5, LayoutSelectionStatus.Optimized)]
        [TestCase(10, LayoutSelectionStatus.StatisticalTie)]
        [TestCase(12, LayoutSelectionStatus.Regression)]
        [TestCase(9.5, LayoutSelectionStatus.Inconclusive)]
        public void FrozenHoldoutStatesAreDistinct(double cost, LayoutSelectionStatus expected)
        {
            var decision = LayoutSelector.SelectCalibration(new[] { Result(true, 10), Result(false, 5) }, 2, bootstrapIterations: 100);
            var baseline = Result(true, 10, true); var selected = Result(false, cost, true);
            var final = LayoutSelector.ConfirmHoldout(decision, baseline, selected, bootstrapIterations: 100);
            Assert.That(final.Status, Is.EqualTo(expected));
            if (expected != LayoutSelectionStatus.Optimized) Assert.That(final.SelectedCandidate.IsBaseline, Is.True);
        }

        [Test]
        public void LifetimeBreakEvenIncludesSetupAndPracticalThreshold()
        {
            var rows = Costs(10, 5, 100);
            var estimate = ConservativeLifetimeEnvelope.Estimate(rows, 10, iterations: 100);
            Assert.That(estimate.Status, Is.EqualTo(LifetimeEnvelopeStatus.FiniteSustainedBreakEven));
            Assert.That(estimate.LowerLifetimeTicks, Is.EqualTo(25));
            Assert.That(estimate.UpperLifetimeTicks, Is.EqualTo(25));
            Assert.That(estimate.Origin, Is.EqualTo(TimingObservationOrigin.SyntheticFixture));
        }

        [Test]
        public void LifetimeUnknownForMissingCostsSourcesProcessesAndZeroSaving()
        {
            Assert.That(ConservativeLifetimeEnvelope.Estimate(new LifetimeProcessCosts[0], iterations: 100).Status, Is.EqualTo(LifetimeEnvelopeStatus.Unknown));
            var rows = Costs(10, 5, 100); rows[1].IncludesConstructionIngressExportDisposal = false;
            Assert.That(ConservativeLifetimeEnvelope.Estimate(rows, iterations: 100).Status, Is.EqualTo(LifetimeEnvelopeStatus.Unknown));
            rows = Costs(10, 5, 100); rows[1].SourceFingerprint = new string('E', 64);
            Assert.That(ConservativeLifetimeEnvelope.Estimate(rows, iterations: 100).Status, Is.EqualTo(LifetimeEnvelopeStatus.Unknown));
            rows = Costs(10, 5, 100); rows[0].CandidateResidentScoreMillisecondsPerTick = 15;
            Assert.That(ConservativeLifetimeEnvelope.Estimate(rows, iterations: 100).Status, Is.EqualTo(LifetimeEnvelopeStatus.Unknown));
        }

        private static LayoutBenchmarkResult Result(bool baseline, double cost, bool holdout = false)
        {
            var values = new[] { cost, cost, cost, cost, cost };
            var zero = new double[5]; var ids = new[] { 0, 1, 2, 3, 4 };
            var result = new LayoutBenchmarkResult { ScenarioId = "fixture", ScenarioContractVersion = 1,
                Phase = holdout ? BenchmarkPhase.Holdout : BenchmarkPhase.Calibration,
                Candidate = new CandidateDescriptor(baseline ? "AoS" : "SoA", 64, baseline), ElementCount = 5, StepsPerSample = 2,
                Completed = true, ParityPassed = true, AllocationCapability = FunctionalEvidenceFixtures.ThreadCapability(),
                RequiredAllocationScope = AllocationScope.CurrentThreadManaged, AllocationWindowsComplete = true,
                TimingContract = new TimingMeasurementContract(), EvidencePartitionId = holdout ? "holdout" : "calibration",
                DatasetHash = holdout ? "fresh" : "training", DatasetSeed = holdout ? 2u : 1u, SourceFingerprint = new string('F', 64),
                ResidentSamplesMillisecondsPerTick = values, IngressSamplesMilliseconds = zero, ExportSamplesMilliseconds = zero,
                ResidentBlockIds = ids, IngressBlockIds = ids, ExportBlockIds = ids, ResidentOrderPositions = new int[5],
                IngressOrderPositions = new int[5], ExportOrderPositions = new int[5], BoundaryCost = new BoundaryCostSummary { LifetimeTicks = 10 },
                Latency = BenchmarkStatistics.Calculate(values, 5, new double[5]) };
            result.AmortizedLatency = BenchmarkStatistics.CalculateAmortizedLatency(values, zero, zero, 10, new double[5], new double[5]);
            return result;
        }
        private static LifecycleObservation Observation() => new LifecycleObservation { Completed = true, Origin = TimingObservationOrigin.SyntheticFixture,
            ProcessId = "mock-process", PartitionId = "mock-partition", LifecycleId = "1", CandidateId = "AoS-b64", DatasetHash = "fixture",
            SourceFingerprint = "fixture", TimestampFrequency = 1000, TickDurations = new long[2] };
        private sealed class FixtureClock : IMeasurementClock
        {
            private readonly Queue<long> _values;
            public FixtureClock(params long[] values) { _values = new Queue<long>(values); }
            public TimingObservationOrigin Origin => TimingObservationOrigin.SyntheticFixture;
            public long Frequency => 1000;
            public long ReadTimestamp() => _values.Dequeue();
        }
        private sealed class FixtureCandidate : ICalibrationCandidate, IBoundaryCost
        {
            public int ExecutedTicks;
            public CandidateDescriptor Descriptor => new CandidateDescriptor("AoS", 64, true);
            public int ElementCount => 1;
            public long ResidentBytes => 0;
            public IBoundaryCost BoundaryCost => this;
            public string ExportedStateHash => "fixture";
            BoundaryCostDescriptor IBoundaryCost.Descriptor => new BoundaryCostDescriptor("fixture ingress", "fixture export");
            public void Execute(int ticks, float dt) { ExecutedTicks += ticks; }
            public void Ingress() { }
            public void Export() { }
            public void Dispose() { }
        }
        private static LifetimeProcessCosts[] Costs(double baseline, double selected, double setup)
        {
            var rows = new LifetimeProcessCosts[3];
            for (int i = 0; i < rows.Length; i++) rows[i] = new LifetimeProcessCosts { ProcessId = "fixture-" + i,
                SourceFingerprint = new string('F', 64), Origin = TimingObservationOrigin.SyntheticFixture,
                BaselineCandidateId = "aos", CandidateId = "soa", IncludesConstructionIngressExportDisposal = true,
                BaselineResidentScoreMillisecondsPerTick = baseline, CandidateResidentScoreMillisecondsPerTick = selected,
                CandidateOneTimeMilliseconds = setup };
            return rows;
        }
    }
}
