using NUnit.Framework;

namespace Yanagisawa.DataLayoutCalibrator.Samples.ParticleIntegrate.Tests
{
    public sealed class ParticleIntegrateScenarioTests
    {
        [Test]
        public void ProtocolCandidates_PreserveParityAcrossAwkwardTailCount()
        {
            var factory = new ParticleIntegrateScenarioFactory();
            using (ICalibrationScenario scenario = factory.Create(
                       4099,
                       ParticleDataSet.CalibrationSeed,
                       new[]
                       {
                           new CandidateDescriptor(LayoutKind.AoS, 64),
                           new CandidateDescriptor(LayoutKind.AoSoA8, 64),
                       }))
            {
                ICalibrationCandidate reference = scenario.GetCandidate(0);
                ICalibrationCandidate candidate = scenario.GetCandidate(1);
                reference.BoundaryCost.Ingress();
                candidate.BoundaryCost.Ingress();
                reference.Execute(32, 1f / 60f);
                candidate.Execute(32, 1f / 60f);

                ParityReport parity = scenario.ParityValidator.Validate(reference, candidate, 1e-5f);

                Assert.That(parity.Passed, Is.True, parity.Reason);
                Assert.That(parity.ComparedElementCount, Is.EqualTo(4099));
                Assert.That(parity.ReferenceStateHash, Is.EqualTo(parity.CandidateStateHash));
            }
        }
    }
}
