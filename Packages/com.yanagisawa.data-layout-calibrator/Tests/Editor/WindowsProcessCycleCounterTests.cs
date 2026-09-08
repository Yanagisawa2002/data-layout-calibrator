using System;
using System.IO;
using NUnit.Framework;

namespace Yanagisawa.DataLayoutCalibrator.Tests
{
    [Explicit("Requires new explicit user authorization: real CPU counter acquisition; excluded from default functional collections.")]
    public sealed class WindowsProcessCycleCounterTests
    {
        [Test]
        public void NativeProviderRetainsExactEndpointsAndDoesNotInventPmuMetrics()
        {
            string directory = Path.Combine(Path.GetTempPath(), "dlc-native-cycles-" + Guid.NewGuid().ToString("N"));
            try
            {
                var provider = new WindowsProcessCycleCounterProvider(typeof(WindowsProcessCycleCounterProvider).Assembly.Location, directory);
                Assert.That(provider.Descriptor.SupportedCounterIds, Is.EqualTo(new[] { WindowsProcessCycleCounterProvider.CounterId }));
                var availability = provider.Probe();
                if (availability.Status != CounterProviderAvailabilityStatus.Available)
                {
                    Assert.That(availability.Code, Is.Not.Empty); Assert.That(availability.Reason, Is.Not.Empty);
                    Assert.Ignore("Actual OS provider unavailable: " + availability.Code + " " + availability.Reason);
                }
                using (var capture = provider.Begin(default))
                {
                    long sum = 0; for (int i = 0; i < 100000; i++) sum += i;
                    GC.KeepAlive(sum);
                    CounterProviderMeasurement measurement = capture.Complete();
                    Assert.That(measurement.Origin, Is.EqualTo(CounterEvidenceOrigin.Observed));
                    Assert.That(measurement.RawCounters.Length, Is.EqualTo(1));
                    Assert.That(measurement.RawCounters[0].Value, Is.GreaterThanOrEqualTo(0));
                    Assert.That(measurement.DerivedMetrics, Is.Empty);
                    Assert.That(File.ReadAllText(measurement.Artifacts[0].ArtifactPath), Does.Contain("start=").And.Contain("end=").And.Contain("delta="));
                    Assert.That(WindowsProcessCycleCounterProvider.HashFile(measurement.Artifacts[0].ArtifactPath), Is.EqualTo(measurement.Artifacts[0].ArtifactSha256));
                    Assert.Throws<InvalidOperationException>(() => capture.Complete());
                }
                string hash = WindowsProcessCycleCounterProvider.HashText("real-native-test-context");
                var context = new CounterCaptureContext {
                    RunId = "native-provider-test", ScenarioId = "native-test", ContractVersion = 1,
                    CandidateId = "test-action", CandidateSchemaSha256 = hash, Phase = BenchmarkPhase.Calibration,
                    ElementCount = 1, ProcessEvidenceId = "native-test-process", DeviceId = "native-test-cpu",
                    DeviceIdentitySha256 = hash, EnvironmentFingerprintSha256 = hash, SettingsFingerprintSha256 = hash };
                int executed = 0;
                CounterCaptureResult result = CounterCaptureRunner.Capture(provider, true, context, () => executed++);
                Assert.That(executed, Is.EqualTo(1));
                Assert.That(result.Status, Is.EqualTo(CounterCollectionStatus.Collected), result.StatusReason);
                Assert.That(result.InterpretationLevel, Is.EqualTo(CounterInterpretationLevel.Correlation));
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }
    }
}
