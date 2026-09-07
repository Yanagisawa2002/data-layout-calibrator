using System;
using System.Linq;
using NUnit.Framework;

namespace Yanagisawa.DataLayoutCalibrator.Samples.ParticleIntegrate.Tests
{
    public sealed class ParticleCandidateMatrixTests
    {
        [Test]
        public void CrossedMatrixPreservesLegacyDefinitionsAndDeclaresExcludedCells()
        {
            var definitions = ParticleCandidateMatrix.CreateCandidates();
            Assert.That(definitions.Length, Is.EqualTo(120));
            Assert.That(definitions.Count(c => c.IsBaseline), Is.EqualTo(16));
            Assert.That(definitions.Select(c => c.CandidateId).Distinct().Count(), Is.EqualTo(120));
            Assert.That(ParticleCandidateMatrix.CreateCells().Length, Is.EqualTo(360));
            Assert.That(ParticleCandidateMatrix.CreateCells().Count(c => !c.Supported), Is.EqualTo(240));
            using (var legacy = new ParticleIntegrateScenarioFactory().Create(1, ParticleDataSet.CalibrationSeed))
                for (int i = 0; i < legacy.CandidateCount; i++)
                {
                    var old = legacy.GetCandidate(i).Descriptor;
                    var current = definitions.Single(c => c.CandidateId == old.CandidateId);
                    Assert.That(CandidateDefinitionProtocol.ComputeCandidateDefinitionSha256(current),
                        Is.EqualTo(CandidateDefinitionProtocol.ComputeCandidateDefinitionSha256(old)));
                }
            Assert.Throws<ArgumentException>(() => ParticleCandidateMatrix.CreateCandidates(new[] {64, 64}));
        }

        [Test]
        public void FalseAlignmentAndUnsupportedKernelMetadataAreRejected()
        {
            var definition = ParticleCandidateMatrix.CreateCandidates()[0];
            definition.Layout.AlignmentBytes = 64;
            Assert.That(ParticleCandidateMatrix.UnsupportedReason(definition), Does.Contain("no allocator contract"));
            Assert.Throws<ArgumentException>(() => new ParticleIntegrateScenarioFactory().Create(1, 1, new[] { definition }));
            var legacy = new CandidateDescriptor(LayoutKind.AoS, 64);
            legacy.Layout.PaddingBytes = 16;
            Assert.Throws<ArgumentException>(() => new ParticleIntegrateScenarioFactory().Create(1, 1, new[] { legacy }));
        }

        [Test]
        public void ActualJobsMatchOracleAcrossTailsAndRepeatedIngress()
        {
            Assert.That(ParticleMatrixValidation.RunOrThrow(), Is.EqualTo(2166));
        }
    }
}
