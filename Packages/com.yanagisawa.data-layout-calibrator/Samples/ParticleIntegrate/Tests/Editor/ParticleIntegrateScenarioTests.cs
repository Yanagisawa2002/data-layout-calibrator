using NUnit.Framework;

namespace Yanagisawa.DataLayoutCalibrator.Samples.ParticleIntegrate.Tests
{
    public sealed class ParticleIntegrateScenarioTests
    {
        [Test]
        public void DefaultMatrix_ExposesExplicitFactorPoliciesAndUniqueCanonicalIds()
        {
            var factory = new ParticleIntegrateScenarioFactory();
            using (ICalibrationScenario scenario = factory.Create(
                       17,
                       ParticleDataSet.CalibrationSeed))
            {
                Assert.That(scenario.CandidateCount, Is.EqualTo(32));
                var ids = new System.Collections.Generic.HashSet<string>();
                bool foundBranchlessAoS = false;
                bool foundDependencyChain = false;
                for (int index = 0; index < scenario.CandidateCount; index++)
                {
                    ICalibrationCandidate candidate = scenario.GetCandidate(index);
                    CandidateDescriptor descriptor = candidate.Descriptor;
                    descriptor.ValidateFactorConsistency();
                    Assert.That(ids.Add(descriptor.CandidateId), Is.True,
                        $"Duplicate CandidateId: {descriptor.CandidateId}");
                    foundBranchlessAoS |=
                        descriptor.LayoutId == "AoS" &&
                        descriptor.EffectiveKernel.ControlFlow == KernelControlFlow.Branchless;
                    foundDependencyChain |=
                        descriptor.EffectiveExecution.Topology == ExecutionTopology.DependencyChain;
                    candidate.BoundaryCost.Ingress();
                    candidate.Execute(5, 1f / 60f);
                }

                Assert.That(foundBranchlessAoS, Is.True);
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
        public void BranchlessAoSDependencyChain_MatchesBranchedFrameFaithfulControl()
        {
            var factory = new ParticleIntegrateScenarioFactory();
            var baseline = new CandidateDescriptor(
                new LayoutPolicy("AoS"),
                new KernelPolicy("ScalarBranched", KernelControlFlow.Branched),
                BatchPolicy.JobBatch(64),
                ExecutionPolicy.FrameFaithful,
                isBaseline: true,
                candidateId: "test-aos-branched-frame");
            var branchless = new CandidateDescriptor(
                new LayoutPolicy("AoS"),
                new KernelPolicy("ScalarBranchless", KernelControlFlow.Branchless),
                BatchPolicy.JobBatch(64),
                ExecutionPolicy.DependencyChain,
                isBaseline: false,
                candidateId: "test-aos-branchless-chain");
            using (ICalibrationScenario scenario = factory.Create(
                       4099,
                       ParticleDataSet.CalibrationSeed,
                       new[] { baseline, branchless }))
            {
                ICalibrationCandidate reference = scenario.GetCandidate(0);
                ICalibrationCandidate candidate = scenario.GetCandidate(1);
                reference.BoundaryCost.Ingress();
                candidate.BoundaryCost.Ingress();
                reference.Execute(97, 1f / 60f);
                candidate.Execute(97, 1f / 60f);

                ParityReport parity = scenario.ParityValidator.Validate(reference, candidate, 1e-5f);

                Assert.That(parity.Passed, Is.True, parity.Reason);
                Assert.That(parity.ReferenceStateHash, Is.EqualTo(parity.CandidateStateHash));
            }
        }

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
