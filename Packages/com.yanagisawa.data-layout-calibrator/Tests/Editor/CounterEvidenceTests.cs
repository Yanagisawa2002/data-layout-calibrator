using System;
using NUnit.Framework;
using UnityEngine;

namespace Yanagisawa.DataLayoutCalibrator.Tests
{
    public sealed class CounterEvidenceTests
    {
        [Test]
        public void Capture_WithNoProvider_RunsWorkloadAndReportsUnavailable()
        {
            int calls = 0;

            CounterCaptureResult result = CounterCaptureRunner.Capture(
                null,
                true,
                CreateContext(),
                () => calls++);

            Assert.That(calls, Is.EqualTo(1));
            Assert.That(result.Status, Is.EqualTo(CounterCollectionStatus.Unavailable));
            Assert.That(result.StatusCode, Is.EqualTo("provider-not-configured"));
            Assert.That(result.Origin, Is.EqualTo(CounterEvidenceOrigin.None));
            Assert.That(result.InterpretationLevel, Is.EqualTo(CounterInterpretationLevel.None));
        }

        [Test]
        public void Capture_WhenDisabled_DoesNotProbeProvider()
        {
            var provider = FakeProvider.Available();
            int calls = 0;

            CounterCaptureResult result = CounterCaptureRunner.Capture(
                provider,
                false,
                CreateContext(),
                () => calls++);

            Assert.That(calls, Is.EqualTo(1));
            Assert.That(provider.DescriptorCalls, Is.Zero);
            Assert.That(provider.ProbeCalls, Is.Zero);
            Assert.That(provider.BeginCalls, Is.Zero);
            Assert.That(result.Status, Is.EqualTo(CounterCollectionStatus.Disabled));
        }

        [Test]
        public void Capture_WithDefaultAvailability_FailsClosedWithoutBeginning()
        {
            var provider = FakeProvider.WithAvailability(default);
            int calls = 0;

            CounterCaptureResult result = CounterCaptureRunner.Capture(
                provider,
                true,
                CreateContext(),
                () => calls++);

            Assert.That(calls, Is.EqualTo(1));
            Assert.That(provider.ProbeCalls, Is.EqualTo(1));
            Assert.That(provider.BeginCalls, Is.Zero);
            Assert.That(result.Status, Is.EqualTo(CounterCollectionStatus.Failed));
            Assert.That(result.StatusCode, Is.EqualTo("probe-not-available"));
        }

        [TestCase("identity")]
        [TestCase("version")]
        [TestCase("hash")]
        public void Capture_WithInvalidContext_FailsBeforeProviderAccess(string defect)
        {
            var provider = FakeProvider.Available();
            CounterCaptureContext context = CreateContext();
            if (defect == "identity")
                context.ProcessEvidenceId = null;
            else if (defect == "version")
                context.ContractVersion = 0;
            else
                context.DeviceIdentitySha256 = new string('f', 64);
            int calls = 0;

            CounterCaptureResult result = CounterCaptureRunner.Capture(
                provider,
                true,
                context,
                () => calls++);

            Assert.That(calls, Is.EqualTo(1));
            Assert.That(provider.DescriptorCalls, Is.Zero);
            Assert.That(provider.ProbeCalls, Is.Zero);
            Assert.That(provider.BeginCalls, Is.Zero);
            Assert.That(result.Status, Is.EqualTo(CounterCollectionStatus.Failed));
            Assert.That(result.StatusCode, Is.EqualTo("context-invalid"));
        }

        [Test]
        public void Capture_WithUnavailableProvider_RunsWorkloadAndPreservesReason()
        {
            var provider = FakeProvider.WithAvailability(
                CounterProviderAvailability.Unavailable(
                    "permission-denied",
                    "The process lacks counter access."));
            int calls = 0;

            CounterCaptureResult result = CounterCaptureRunner.Capture(
                provider,
                true,
                CreateContext(),
                () => calls++);

            Assert.That(calls, Is.EqualTo(1));
            Assert.That(provider.BeginCalls, Is.Zero);
            Assert.That(result.Status, Is.EqualTo(CounterCollectionStatus.Unavailable));
            Assert.That(result.StatusCode, Is.EqualTo("permission-denied"));
            Assert.That(result.Provider.ProviderId, Is.EqualTo("fixture-provider"));
        }

        [TestCase("provider-id")]
        [TestCase("provider-version")]
        [TestCase("mechanism")]
        [TestCase("artifact-hash")]
        public void Capture_WithInvalidAvailableDescriptor_FailsBeforeBeginning(string defect)
        {
            var provider = FakeProvider.Available();
            if (defect == "provider-id")
                provider.DescriptorValue.ProviderId = null;
            else if (defect == "provider-version")
                provider.DescriptorValue.ProviderVersion = null;
            else if (defect == "mechanism")
                provider.DescriptorValue.CollectionMechanism = null;
            else
                provider.DescriptorValue.ProviderArtifactSha256 = new string('c', 64);
            int calls = 0;

            CounterCaptureResult result = CounterCaptureRunner.Capture(
                provider,
                true,
                CreateContext(),
                () => calls++);

            Assert.That(calls, Is.EqualTo(1));
            Assert.That(provider.BeginCalls, Is.Zero);
            Assert.That(result.Status, Is.EqualTo(CounterCollectionStatus.Failed));
            Assert.That(result.StatusCode, Is.EqualTo("descriptor-invalid"));
        }

        [Test]
        public void Capture_WhenProviderBeginFails_DoesNotBreakWorkload()
        {
            var provider = FakeProvider.Available();
            provider.ThrowOnBegin = true;
            int calls = 0;

            CounterCaptureResult result = CounterCaptureRunner.Capture(
                provider,
                true,
                CreateContext(),
                () => calls++);

            Assert.That(calls, Is.EqualTo(1));
            Assert.That(result.Status, Is.EqualTo(CounterCollectionStatus.Failed));
            Assert.That(result.StatusCode, Is.EqualTo("begin-failed"));
        }

        [Test]
        public void Capture_WhenProviderProbeReportsFailure_RecordsFailureWithoutBeginning()
        {
            var provider = FakeProvider.WithAvailability(
                CounterProviderAvailability.Failed(
                    "facility-error",
                    "The synthetic facility failed to initialize."));
            int calls = 0;

            CounterCaptureResult result = CounterCaptureRunner.Capture(
                provider,
                true,
                CreateContext(),
                () => calls++);

            Assert.That(calls, Is.EqualTo(1));
            Assert.That(provider.BeginCalls, Is.Zero);
            Assert.That(result.Status, Is.EqualTo(CounterCollectionStatus.Failed));
            Assert.That(result.StatusCode, Is.EqualTo("facility-error"));
        }

        [Test]
        public void Capture_WhenProviderCompleteFails_DoesNotBreakCompletedWorkload()
        {
            var provider = FakeProvider.Available();
            provider.Capture.ThrowOnComplete = true;
            int calls = 0;

            CounterCaptureResult result = CounterCaptureRunner.Capture(
                provider,
                true,
                CreateContext(),
                () => calls++);

            Assert.That(calls, Is.EqualTo(1));
            Assert.That(provider.Capture.Disposed, Is.True);
            Assert.That(result.Status, Is.EqualTo(CounterCollectionStatus.Failed));
            Assert.That(result.StatusCode, Is.EqualTo("complete-failed"));
        }

        [Test]
        public void Capture_WhenProviderReturnsNoRawCounters_RecordsFailure()
        {
            var provider = FakeProvider.Available();
            provider.Capture.Measurement = CreateMeasurement(CounterEvidenceOrigin.SyntheticFixture);
            provider.Capture.Measurement.RawCounters = Array.Empty<RawCounterValue>();

            CounterCaptureResult result = CounterCaptureRunner.Capture(
                provider,
                true,
                CreateContext(),
                () => { });

            Assert.That(result.Status, Is.EqualTo(CounterCollectionStatus.Failed));
            Assert.That(result.StatusCode, Is.EqualTo("measurement-invalid"));
            Assert.That(result.StatusReason, Does.Contain("must contain raw counters"));
        }

        [TestCase("derived-source")]
        [TestCase("artifact-hash")]
        [TestCase("overhead-repetitions")]
        [TestCase("overhead-percent")]
        public void Capture_WithInvalidMeasurementMetadata_FailsClosed(string defect)
        {
            var provider = FakeProvider.Available();
            provider.Capture.Measurement = CreateMeasurement(
                CounterEvidenceOrigin.SyntheticFixture);
            if (defect == "derived-source")
            {
                provider.Capture.Measurement.DerivedMetrics[0].SourceCounterIds =
                    new[] { "undeclared-counter" };
            }
            else if (defect == "artifact-hash")
            {
                provider.Capture.Measurement.Artifacts[0].ArtifactSha256 =
                    new string('b', 64);
            }
            else if (defect == "overhead-repetitions")
            {
                provider.Capture.Measurement.Overhead.Repetitions = 0;
            }
            else
            {
                provider.Capture.Measurement.Overhead.EstimatedOverheadPercent = 999d;
            }
            int calls = 0;

            CounterCaptureResult result = CounterCaptureRunner.Capture(
                provider,
                true,
                CreateContext(),
                () => calls++);

            Assert.That(calls, Is.EqualTo(1));
            Assert.That(provider.Capture.Disposed, Is.True);
            Assert.That(result.Status, Is.EqualTo(CounterCollectionStatus.Failed));
            Assert.That(result.StatusCode, Is.EqualTo("measurement-invalid"));
            Assert.That(result.RawCounters, Is.Empty);
            Assert.That(result.InterpretationLevel, Is.EqualTo(CounterInterpretationLevel.None));
        }

        [Test]
        public void Capture_WhenWorkloadFails_RethrowsWorkloadFailureAndSuppressesDisposeFailure()
        {
            var provider = FakeProvider.Available();
            provider.Capture.ThrowOnDispose = true;

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                CounterCaptureRunner.Capture(
                    provider,
                    true,
                    CreateContext(),
                    () => throw new InvalidOperationException("workload failed")));

            Assert.That(exception.Message, Is.EqualTo("workload failed"));
            Assert.That(provider.Capture.DisposeAttempts, Is.EqualTo(1));
        }

        [Test]
        public void Capture_WithMeasurement_PreservesOriginProvenanceAndContext()
        {
            var provider = FakeProvider.Available();
            provider.Capture.Measurement = CreateMeasurement(CounterEvidenceOrigin.SyntheticFixture);

            CounterCaptureResult result = CounterCaptureRunner.Capture(
                provider,
                true,
                CreateContext(),
                () => { });

            Assert.That(result.SchemaVersion, Is.EqualTo(1));
            Assert.That(result.Status, Is.EqualTo(CounterCollectionStatus.Collected));
            Assert.That(result.Origin, Is.EqualTo(CounterEvidenceOrigin.SyntheticFixture));
            Assert.That(result.InterpretationLevel, Is.EqualTo(CounterInterpretationLevel.None));
            Assert.That(result.StatusReason, Does.Contain("no evidence interpretation level"));
            Assert.That(result.Context.ProcessEvidenceId, Is.EqualTo("process-03"));
            Assert.That(result.Context.DeviceId, Is.EqualTo("device-alpha"));
            Assert.That(result.Context.ContractVersion, Is.EqualTo(4));
            Assert.That(result.Context.CandidateSchemaSha256, Has.Length.EqualTo(64));
            Assert.That(result.RawCounters[0].CounterId, Is.EqualTo("cycles"));
            Assert.That(result.DerivedMetrics[0].Formula, Is.EqualTo("cycles / elements"));
            Assert.That(result.Artifacts[0].ArtifactSha256, Has.Length.EqualTo(64));
            Assert.That(result.Overhead.Status, Is.EqualTo(CounterOverheadStatus.Measured));
        }

        [Test]
        public void CaptureResult_RoundTripsThroughJsonUtilityWithoutPromotingFixtureOrigin()
        {
            var provider = FakeProvider.Available();
            provider.Capture.Measurement = CreateMeasurement(CounterEvidenceOrigin.SyntheticFixture);
            CounterCaptureResult result = CounterCaptureRunner.Capture(
                provider,
                true,
                CreateContext(),
                () => { });

            string json = JsonUtility.ToJson(result);
            CounterCaptureResult restored = JsonUtility.FromJson<CounterCaptureResult>(json);

            Assert.That(restored.SchemaVersion, Is.EqualTo(1));
            Assert.That(restored.Origin, Is.EqualTo(CounterEvidenceOrigin.SyntheticFixture));
            Assert.That(restored.InterpretationLevel, Is.EqualTo(CounterInterpretationLevel.None));
            Assert.That(restored.RawCounters[0].Value, Is.EqualTo(1200d));
            Assert.That(restored.Context.RoundIndex, Is.EqualTo(7));
        }

        [Test]
        public void EstimateOverhead_UsesDeterministicPairedMedians()
        {
            CounterOverheadMetadata overhead = CounterOverheadEstimator.Estimate(
                new[] { 90d, 100d, 110d, 120d },
                new[] { 100d, 120d, 130d, 140d },
                "fixture-paired-median");

            Assert.That(overhead.Status, Is.EqualTo(CounterOverheadStatus.Measured));
            Assert.That(overhead.Repetitions, Is.EqualTo(4));
            Assert.That(overhead.DisabledMedianNanoseconds, Is.EqualTo(105d));
            Assert.That(overhead.EnabledMedianNanoseconds, Is.EqualTo(125d));
            Assert.That(overhead.EstimatedAddedNanoseconds, Is.EqualTo(20d));
            Assert.That(overhead.EstimatedOverheadPercent, Is.EqualTo(20d / 105d * 100d).Within(1e-12));
        }

        [Test]
        public void EstimateOverhead_WithInvalidInput_ReportsFailureInsteadOfInventingValues()
        {
            CounterOverheadMetadata overhead = CounterOverheadEstimator.Estimate(
                new[] { 100d },
                Array.Empty<double>(),
                null);

            Assert.That(overhead.Status, Is.EqualTo(CounterOverheadStatus.Failed));
            Assert.That(overhead.Repetitions, Is.Zero);
            Assert.That(overhead.FailureReason, Does.Contain("same non-zero length"));
        }

        private static CounterCaptureContext CreateContext()
        {
            return new CounterCaptureContext
            {
                RunId = "run-fixture",
                ScenarioId = "scenario-fixture",
                ContractVersion = 4,
                CandidateId = "candidate-fixture",
                CandidateSchemaSha256 = new string('D', 64),
                Phase = BenchmarkPhase.Calibration,
                RoundIndex = 7,
                ElementCount = 1024,
                ProcessEvidenceId = "process-03",
                DeviceId = "device-alpha",
                DeviceIdentitySha256 = new string('F', 64),
                EnvironmentFingerprintSha256 = new string('A', 64),
                SettingsFingerprintSha256 = new string('E', 64),
            };
        }

        private static CounterProviderMeasurement CreateMeasurement(CounterEvidenceOrigin origin)
        {
            return new CounterProviderMeasurement
            {
                Origin = origin,
                RawCounters = new[]
                {
                    new RawCounterValue
                    {
                        CounterId = "cycles",
                        Value = 1200d,
                        Unit = "count",
                        ScaleFactor = 1d,
                    },
                },
                DerivedMetrics = new[]
                {
                    new DerivedCounterMetric
                    {
                        MetricId = "cycles-per-element",
                        Value = 1.171875d,
                        Unit = "cycles/element",
                        Formula = "cycles / elements",
                        SourceCounterIds = new[] { "cycles" },
                    },
                },
                Artifacts = new[]
                {
                    new CounterArtifactProvenance
                    {
                        ArtifactKind = "synthetic-assembly-fixture",
                        ArtifactPath = "fixture-only/not-an-observed-artifact.txt",
                        ArtifactSha256 = new string('B', 64),
                        Producer = "CounterEvidenceTests",
                        ProducerVersion = "1",
                    },
                },
                Overhead = new CounterOverheadMetadata
                {
                    Status = CounterOverheadStatus.Measured,
                    Repetitions = 3,
                    DisabledMedianNanoseconds = 100d,
                    EnabledMedianNanoseconds = 110d,
                    EstimatedAddedNanoseconds = 10d,
                    EstimatedOverheadPercent = 10d,
                    Method = "synthetic-fixture",
                },
            };
        }

        private sealed class FakeProvider : ICounterProvider
        {
            private CounterProviderAvailability _availability;

            public int DescriptorCalls;
            public int ProbeCalls;
            public int BeginCalls;
            public bool ThrowOnBegin;
            public readonly FakeCapture Capture = new FakeCapture();
            public CounterProviderDescriptor DescriptorValue;

            private FakeProvider(CounterProviderAvailability availability)
            {
                _availability = availability;
                DescriptorValue = new CounterProviderDescriptor
                {
                    ProviderId = "fixture-provider",
                    ProviderVersion = "1",
                    CollectionMechanism = "synthetic-fixture",
                    ProviderArtifactSha256 = new string('C', 64),
                    SupportedCounterIds = new[] { "cycles" },
                };
                Capture.Measurement = CreateMeasurement(CounterEvidenceOrigin.SyntheticFixture);
            }

            public CounterProviderDescriptor Descriptor
            {
                get
                {
                    DescriptorCalls++;
                    return DescriptorValue;
                }
            }

            public static FakeProvider Available()
            {
                return WithAvailability(CounterProviderAvailability.Available());
            }

            public static FakeProvider WithAvailability(CounterProviderAvailability availability)
            {
                return new FakeProvider(availability);
            }

            public CounterProviderAvailability Probe()
            {
                ProbeCalls++;
                return _availability;
            }

            public ICounterCapture Begin(CounterCaptureContext context)
            {
                BeginCalls++;
                if (ThrowOnBegin)
                    throw new InvalidOperationException("synthetic begin failure");
                return Capture;
            }
        }

        private sealed class FakeCapture : ICounterCapture
        {
            public CounterProviderMeasurement Measurement;
            public bool ThrowOnComplete;
            public bool ThrowOnDispose;
            public bool Disposed;
            public int DisposeAttempts;

            public CounterProviderMeasurement Complete()
            {
                if (ThrowOnComplete)
                    throw new InvalidOperationException("synthetic complete failure");
                return Measurement;
            }

            public void Dispose()
            {
                DisposeAttempts++;
                Disposed = true;
                if (ThrowOnDispose)
                    throw new InvalidOperationException("synthetic dispose failure");
            }
        }
    }
}
