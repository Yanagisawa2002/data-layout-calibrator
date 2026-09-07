using System;
using NUnit.Framework;
using UnityEngine;

namespace Yanagisawa.DataLayoutCalibrator.Tests
{
    public sealed class EnvelopeMeasurementGuardTests
    {
        [TestCase(39, 20, 4000)]
        [TestCase(40, 19, 4000)]
        [TestCase(40, 20, 3999)]
        public void InadequateEvidence_IsRejectedBeforeFactoryExecution(int resident, int boundary, int bootstrap)
        {
            var settings = Settings();
            settings.SamplesPerCandidate = resident;
            settings.BoundarySamplesPerCandidate = boundary;
            settings.BootstrapIterations = bootstrap;
            Assert.Throws<ArgumentException>(() => Run(settings));
        }

        [Test]
        public void ReusedSeedAndDifferentHoldoutDimensions_AreRejected()
        {
            var settings = Settings();
            settings.HoldoutSeed = settings.CalibrationSeed;
            Assert.Throws<ArgumentException>(() => Run(settings));
            settings = Settings();
            settings.HoldoutElementCount++;
            Assert.Throws<ArgumentException>(() => Run(settings));
        }

        [Test]
        public void RuntimeAllocationProviderSurvivesSerializedSettingsFreeze()
        {
            var settings = Settings();
            settings.AllocationCounter = new UnavailableAllocationCounter();
            var error = Assert.Throws<NotSupportedException>(() => Run(settings));
            Assert.That(error.Message, Is.EqualTo("injected allocation unavailable"));
        }

        private sealed class UnavailableAllocationCounter : IManagedAllocationCounter
        {
            public string Identity => "synthetic unavailable counter";
            public void Validate() => throw new NotSupportedException("injected allocation unavailable");
            public void Begin() => Assert.Fail("No allocation window should start.");
            public long End() => throw new InvalidOperationException();
        }

        private static CalibrationRunSettings Settings() => new CalibrationRunSettings
            { ElementCount = 17, HoldoutElementCount = 17, LifetimeTicks = 16 };

        private static void Run(CalibrationRunSettings settings) => ScenarioCalibrationEngine.RunEnvelopeCell(
            new UnreachableFactory(), settings, new AdvantageEnvelopeAxis(17, 16, 1.4, 1, "FrameFaithful"),
            new string('A', 64), "guard-test", new Codec(), (name, json) => Assert.Fail("No evidence should be written."));

        private sealed class Codec : IEnvelopeArtifactCodec
        {
            public string Serialize<T>(T value, bool pretty = false) => JsonUtility.ToJson(value, pretty);
            public T Deserialize<T>(string json) => JsonUtility.FromJson<T>(json);
        }

        private sealed class UnreachableFactory : ICalibrationScenarioFactory
        {
            public ScenarioDescriptor Descriptor => throw new InvalidOperationException("Factory must not execute.");
            public ICalibrationScenario Create(int count, uint seed, CandidateDescriptor[] candidates = null) =>
                throw new InvalidOperationException("Factory must not execute.");
        }
    }
}
