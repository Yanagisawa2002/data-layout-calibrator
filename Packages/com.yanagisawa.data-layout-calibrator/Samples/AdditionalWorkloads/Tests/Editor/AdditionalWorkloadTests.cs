using System;
using NUnit.Framework;

namespace Yanagisawa.DataLayoutCalibrator.Samples.AdditionalWorkloads.Tests
{
    public sealed class AdditionalWorkloadTests
    {
        [TestCase(true, 1)] [TestCase(true, 7)] [TestCase(true, 65)] [TestCase(true, 129)]
        [TestCase(false, 1)] [TestCase(false, 7)] [TestCase(false, 65)] [TestCase(false, 129)]
        public void GeneratedLayoutsMatchIndependentOracleAndRoundTrip(bool spatial, int count)
        {
            ICalibrationScenarioFactory factory = spatial ? (ICalibrationScenarioFactory)new SpatialNeighborhoodScenarioFactory() : new AnimationStateScenarioFactory();
            using (ICalibrationScenario scenario = factory.Create(count, AdditionalWorkloadCandidates.HoldoutSeed))
            {
                Assert.That(scenario.CandidateCount, Is.EqualTo(8));
                for (int i = 0; i < scenario.CandidateCount; i++)
                {
                    ICalibrationCandidate candidate = scenario.GetCandidate(i);
                    candidate.Execute(71, 1f / 60f);
                    Assert.That(spatial ? ((SpatialCandidate)candidate).ValidateOracle(1e-5f) :
                        ((AnimationCandidate)candidate).ValidateOracle(71, 1f / 60f, 1e-5f), Is.True);
                }
                ICalibrationCandidate baseline = scenario.GetCandidate(scenario.ReferenceCandidateIndex);
                for (int i = 0; i < scenario.CandidateCount; i++)
                {
                    var candidate = scenario.GetCandidate(i);
                    var parity = scenario.ParityValidator.Validate(baseline, candidate, 1e-5f);
                    Assert.That(parity.Passed, Is.True, parity.Reason);
                    string first = candidate.ExportedStateHash;
                    candidate.BoundaryCost.Ingress(); candidate.Execute(71, 1f / 60f); candidate.BoundaryCost.Export();
                    Assert.That(candidate.ExportedStateHash, Is.EqualTo(first));
                }
            }
        }
        [TestCase(true)] [TestCase(false)]
        public void ChangingSeedChangesActualInputAndDisposedCandidatesRejectExecution(bool spatial)
        {
            ICalibrationScenarioFactory factory = spatial ? (ICalibrationScenarioFactory)new SpatialNeighborhoodScenarioFactory() : new AnimationStateScenarioFactory();
            using (var a = factory.Create(33, 123))
            using (var b = factory.Create(33, 456)) Assert.That(a.DatasetHash, Is.Not.EqualTo(b.DatasetHash));
            var scenario = factory.Create(3, 123); var candidate = scenario.GetCandidate(0);
            scenario.Dispose(); scenario.Dispose();
            Assert.Throws<ObjectDisposedException>(() => candidate.Execute(1, 1f / 60f));
        }
        [Test]
        public void UnsupportedLayoutIsRejected()
        {
            var descriptor = new CandidateDescriptor(new LayoutPolicy("AoSoA8", 8),
                new KernelPolicy("RadiusGather", KernelControlFlow.Branched), BatchPolicy.JobBatch(64), ExecutionPolicy.FrameFaithful, isBaseline: false);
            Assert.Throws<ArgumentException>(() => new SpatialNeighborhoodScenarioFactory().Create(7, 1, new[] { descriptor }));
        }
    }
}
