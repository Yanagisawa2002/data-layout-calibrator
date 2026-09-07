using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;

namespace Yanagisawa.DataLayoutCalibrator.Samples.ParticleIntegrate.Tests
{
    public sealed class ParticleColdAccessTests
    {
        [TestCase(LayoutKind.AoS)]
        [TestCase(LayoutKind.SoA)]
        [TestCase(LayoutKind.AoSoA8)]
        public void ColdPass_ChangesAllColdComponentsAndPreservesHotFields(LayoutKind layout)
        {
            using (var input = ParticleDataSet.Create(17, ParticleDataSet.CalibrationSeed, Allocator.TempJob))
            using (var output = new NativeArray<ParticleRecord>(17, Allocator.TempJob))
            using (var domain = ParticleLayoutDomain.Create(layout, 64, input))
            {
                domain.ScheduleColdFields().Complete();
                domain.Export(output);
                for (int i = 0; i < input.Length; i++)
                {
                    Assert.That(output[i].Position, Is.EqualTo(input[i].Position));
                    Assert.That(output[i].Velocity, Is.EqualTo(input[i].Velocity));
                    Assert.That(output[i].Lifetime, Is.EqualTo(input[i].Lifetime));
                    Assert.That(output[i].Category, Is.EqualTo(input[i].Category + 1));
                    Assert.That(output[i].Rotation.value,
                        Is.EqualTo(input[i].Rotation.value + new float4(0.0001f, -0.0001f, 0.0002f, -0.0002f)));
                }
            }
        }

        [TestCase(1)]
        [TestCase(8)]
        public void ColdCadence_PreservesPartialCallsAndFullLayoutParity(int period)
        {
            var factory = new ParticleIntegrateScenarioFactory(period);
            using (ICalibrationScenario scenario = factory.Create(17, ParticleDataSet.CalibrationSeed,
                       new[] { new CandidateDescriptor(LayoutKind.AoS, 64),
                           new CandidateDescriptor(LayoutKind.SoA, 64), new CandidateDescriptor(LayoutKind.AoSoA8, 64) }))
            using (ICalibrationScenario control = factory.Create(17, ParticleDataSet.CalibrationSeed,
                       new[] { new CandidateDescriptor(LayoutKind.AoS, 64) }))
            {
                ICalibrationCandidate reference = control.GetCandidate(0);
                reference.BoundaryCost.Ingress();
                reference.Execute(17, 1f / 60);
                reference.BoundaryCost.Export();
                for (int i = 0; i < scenario.CandidateCount; i++)
                {
                    ICalibrationCandidate candidate = scenario.GetCandidate(i);
                    candidate.BoundaryCost.Ingress();
                    candidate.Execute(3, 1f / 60);
                    candidate.Execute(5, 1f / 60);
                    candidate.Execute(9, 1f / 60);
                    candidate.BoundaryCost.Export();
                    Assert.That(candidate.ExportedStateHash, Is.EqualTo(reference.ExportedStateHash));
                    Assert.That(scenario.ParityValidator.Validate(reference, candidate, 1e-5f).Passed, Is.True);
                    // Ingress must reset cadence as well as values.
                    candidate.BoundaryCost.Ingress();
                    candidate.Execute(17, 1f / 60);
                    candidate.BoundaryCost.Export();
                    Assert.That(candidate.ExportedStateHash, Is.EqualTo(reference.ExportedStateHash));
                }
            }
        }

        [Test]
        public void AccessModes_HaveDifferentObservableOutputsAndScenarioIdentity()
        {
            string previous = null;
            foreach (int period in new[] { 0, 1, 8 })
            {
                var factory = new ParticleIntegrateScenarioFactory(period);
                using (ICalibrationScenario scenario = factory.Create(17, ParticleDataSet.CalibrationSeed,
                           new[] { new CandidateDescriptor(LayoutKind.AoS, 64) }))
                {
                    var candidate = scenario.GetCandidate(0);
                    candidate.BoundaryCost.Ingress();
                    candidate.Execute(17, 1f / 60);
                    candidate.BoundaryCost.Export();
                    Assert.That(candidate.ExportedStateHash, Is.Not.EqualTo(previous));
                    Assert.That(scenario.Descriptor.ScenarioId, Is.EqualTo(factory.Descriptor.ScenarioId));
                    previous = candidate.ExportedStateHash;
                }
            }
        }
    }
}
