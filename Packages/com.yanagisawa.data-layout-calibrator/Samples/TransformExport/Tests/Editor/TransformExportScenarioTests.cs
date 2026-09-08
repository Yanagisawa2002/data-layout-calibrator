using NUnit.Framework;

namespace Yanagisawa.DataLayoutCalibrator.Samples.TransformExport.Tests
{
    public sealed class TransformExportScenarioTests
    {
        [Test]
        public void DefaultMatrix_CrossesLayoutBatchAndExecutionPolicies()
        {
            var factory = new TransformExportScenarioFactory();
            using (ICalibrationScenario scenario = factory.Create(
                       17,
                       TransformExportDataSet.CalibrationSeed))
            {
                Assert.That(scenario.CandidateCount, Is.EqualTo(16));
                bool foundDependencyChain = false;
                for (int index = 0; index < scenario.CandidateCount; index++)
                {
                    ICalibrationCandidate candidate = scenario.GetCandidate(index);
                    CandidateDescriptor descriptor = candidate.Descriptor;
                    descriptor.ValidateFactorConsistency();
                    foundDependencyChain |=
                        descriptor.EffectiveExecution.Topology == ExecutionTopology.DependencyChain;
                    candidate.BoundaryCost.Ingress();
                    candidate.Execute(4, 1f / 60f);
                }
                Assert.That(foundDependencyChain, Is.True);
                ICalibrationCandidate reference = scenario.GetCandidate(scenario.ReferenceCandidateIndex);
                for (int index = 0; index < scenario.CandidateCount; index++)
                {
                    ParityReport parity = scenario.ParityValidator.Validate(
                        reference,
                        scenario.GetCandidate(index),
                        1e-5f);
                    Assert.That(parity.Passed, Is.True,
                        $"{scenario.GetCandidate(index).Descriptor.CandidateId}: {parity.Reason}");
                }
            }
        }

        [Test]
        public void DependencyChain_MatchesFrameFaithfulExport()
        {
            var factory = new TransformExportScenarioFactory();
            var frame = new CandidateDescriptor(
                new LayoutPolicy("AoS"),
                new KernelPolicy("FullMatrixExport", KernelControlFlow.Unspecified),
                BatchPolicy.JobBatch(64),
                ExecutionPolicy.FrameFaithful,
                isBaseline: true,
                candidateId: "test-transform-frame");
            var chain = new CandidateDescriptor(
                new LayoutPolicy("SoA"),
                new KernelPolicy("FullMatrixExport", KernelControlFlow.Unspecified),
                BatchPolicy.JobBatch(64),
                ExecutionPolicy.DependencyChain,
                isBaseline: false,
                candidateId: "test-transform-chain");
            using (ICalibrationScenario scenario = factory.Create(
                       4099,
                       TransformExportDataSet.CalibrationSeed,
                       new[] { frame, chain }))
            {
                ICalibrationCandidate reference = scenario.GetCandidate(0);
                ICalibrationCandidate candidate = scenario.GetCandidate(1);
                reference.BoundaryCost.Ingress();
                candidate.BoundaryCost.Ingress();
                reference.Execute(8, 1f / 60f);
                candidate.Execute(8, 1f / 60f);

                ParityReport parity = scenario.ParityValidator.Validate(reference, candidate, 1e-5f);

                Assert.That(parity.Passed, Is.True, parity.Reason);
                Assert.That(parity.ReferenceStateHash, Is.EqualTo(parity.CandidateStateHash));
            }
        }

        [Test]
        public void AoSAndSoA_ProduceEquivalentCanonicalExports()
        {
            var factory = new TransformExportScenarioFactory();
            using (ICalibrationScenario scenario = factory.Create(
                       4099,
                       TransformExportDataSet.CalibrationSeed,
                       new[]
                       {
                           new CandidateDescriptor(LayoutKind.AoS, 64),
                           new CandidateDescriptor(LayoutKind.SoA, 64),
                       }))
            {
                ICalibrationCandidate reference = scenario.GetCandidate(0);
                ICalibrationCandidate candidate = scenario.GetCandidate(1);
                reference.BoundaryCost.Ingress();
                candidate.BoundaryCost.Ingress();
                reference.Execute(8, 1f / 60f);
                candidate.Execute(8, 1f / 60f);

                ParityReport parity = scenario.ParityValidator.Validate(reference, candidate, 1e-5f);

                Assert.That(parity.Passed, Is.True, parity.Reason);
                Assert.That(parity.ComparedElementCount, Is.EqualTo(4099));
                Assert.That(parity.ReferenceStateHash, Is.EqualTo(parity.CandidateStateHash));
            }
        }

        [Test]
        public void Factory_ExposesNegativeControlContract()
        {
            var factory = new TransformExportScenarioFactory();

            Assert.That(factory.Descriptor.ScenarioId, Is.EqualTo("transform-export-v1"));
            Assert.That(factory.Descriptor.DisplayName, Does.Contain("Negative Control"));
        }
    }
}
