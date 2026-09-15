using System;
using NUnit.Framework;

namespace Yanagisawa.DataLayoutCalibrator.Tests
{
    public sealed class WholeTaskLayoutSelectorTests
    {
        static WholeTaskProcessSample[] Samples(double candidateMs = 80)
        {
            var values = new WholeTaskProcessSample[8];
            for (int i = 0; i < 4; i++) for (int arm = 0; arm < 2; arm++)
                values[2*i+arm] = new WholeTaskProcessSample {
                    ProcessIdentity = "process-"+(2*i+arm), PairId = "round-"+i,
                    CandidateId = arm == 0 ? "aos" : "soa", PartitionId = "calibration",
                    DatasetHash = "complete-input", SourceFingerprint = "frozen-source", ContractId = "one-task",
                    Observed = true, Completed = true, ParityPassed = true, IncludesCompleteBoundary = true, EnvironmentQualified = true,
                    CompleteMilliseconds = (arm == 0 ? 100 : candidateMs) + i
                };
            return values;
        }
        static WholeTaskTimingDecision Select(WholeTaskProcessSample[] x) => WholeTaskLayoutSelector.Select(
            x, new[] { "aos", "soa" }, "aos", "calibration", bootstrapIterations: 2000);

        [Test] public void IndependentTaskImprovementIsTimingOnly()
        {
            var d = Select(Samples());
            Assert.That(d.CandidateId, Is.EqualTo("soa"));
            Assert.That(d.TimingGatePassed, Is.True);
            Assert.That(d.LowerImprovementPercent, Is.GreaterThan(0));
            Assert.That(d.AllocationEligibility, Is.EqualTo("Unknown"));
            Assert.That(d.IsDeploymentProfile, Is.False);
        }
        [TestCase(100)] [TestCase(101)] [TestCase(99)]
        public void TiesRegressionAndTinyGainKeepFixedBaseline(double ms)
        { Assert.That(Select(Samples(ms)).CandidateId, Is.EqualTo("aos")); }
        [Test] public void RejectedCandidateGainIsNotAttributedToBaselineRecommendation()
        {
            var decision = Select(Samples(99));
            Assert.That(decision.CandidateId, Is.EqualTo("aos"));
            Assert.That(decision.ImprovementPercent, Is.EqualTo(0));
            Assert.That(decision.LowerImprovementPercent, Is.Null);
            Assert.That(decision.CalibrationBestCandidateId, Is.EqualTo("soa"));
            Assert.That(decision.CalibrationBestImprovementPercent, Is.GreaterThan(0));
        }
        [Test] public void RepeatedTicksWithinOneProcessAreRejected()
        {
            var x = Samples(); x[2].ProcessIdentity = x[0].ProcessIdentity;
            Assert.Throws<ArgumentException>(() => Select(x));
        }
        [Test] public void HoldoutCannotEnterCalibrationPartition()
        {
            var x = Samples(); x[1].PartitionId = "holdout";
            Assert.Throws<ArgumentException>(() => Select(x));
        }
        [Test] public void UnbalancedPairsAreRejected()
        { Assert.Throws<ArgumentException>(() => Select(new[] {Samples()[0], Samples()[1], Samples()[2]})); }
        [Test] public void ShiftedPairIdentitiesAreRejected()
        {
            var x = Samples(); x[1].PairId = "unpaired";
            Assert.Throws<ArgumentException>(() => Select(x));
        }
        [Test] public void SourceInputAndBoundaryMixingAreRejected()
        {
            var x = Samples(); x[1].SourceFingerprint = "other";
            Assert.Throws<ArgumentException>(() => Select(x));
            x = Samples(); x[1].DatasetHash = "other";
            Assert.Throws<ArgumentException>(() => Select(x));
            x = Samples(); x[1].ContractId = "other";
            Assert.Throws<ArgumentException>(() => Select(x));
        }
        [Test] public void MissingParityProfilerAndSyntheticEvidenceAreRejected()
        {
            var x = Samples(); x[1].ParityPassed = false;
            Assert.Throws<ArgumentException>(() => Select(x));
            x = Samples(); x[1].Diagnostic = true;
            Assert.Throws<ArgumentException>(() => Select(x));
            x = Samples(); x[1].Observed = false;
            Assert.Throws<ArgumentException>(() => Select(x));
            x = Samples(); x[1].IncludesCompleteBoundary = false;
            Assert.Throws<ArgumentException>(() => Select(x));
            x = Samples(); x[1].EnvironmentQualified = false;
            Assert.Throws<ArgumentException>(() => Select(x));
        }
        [Test] public void PermutationDoesNotChangeAlignedDecision()
        {
            var x = Samples(); var first = Select(x); Array.Reverse(x); var last = Select(x);
            Assert.That(last.CandidateId, Is.EqualTo(first.CandidateId));
            Assert.That(last.LowerImprovementPercent, Is.EqualTo(first.LowerImprovementPercent));
        }
        [Test] public void ConfirmationUsesOnlyFrozenCandidateAndIndependentInput()
        {
            var frozen = Select(Samples()); var all = Samples();
            var baseline = new WholeTaskProcessSample[4]; var selected = new WholeTaskProcessSample[4];
            for (int i=0; i<4; i++)
            {
                baseline[i] = all[2*i]; selected[i] = all[2*i+1];
                baseline[i].PartitionId = selected[i].PartitionId = "confirmation";
                baseline[i].DatasetHash = selected[i].DatasetHash = "fresh-input";
                baseline[i].ProcessIdentity = "confirmation-"+baseline[i].ProcessIdentity;
                selected[i].ProcessIdentity = "confirmation-"+selected[i].ProcessIdentity;
            }
            var result = WholeTaskLayoutSelector.Confirm(frozen, baseline, selected, 2000);
            Assert.That(result.ConfirmedTimingGain, Is.True);
            Assert.That(result.AllocationEligibility, Is.EqualTo("Unknown"));
            selected[0].DatasetHash = frozen.CalibrationDatasetHash;
            Assert.Throws<ArgumentException>(() => WholeTaskLayoutSelector.Confirm(frozen, baseline, selected, 2000));
            selected[0].DatasetHash = "fresh-input";
            selected[0].CandidateId = "hindsight-oracle";
            Assert.Throws<ArgumentException>(() => WholeTaskLayoutSelector.Confirm(frozen, baseline, selected, 2000));
        }
    }
}
