using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;

namespace Yanagisawa.DataLayoutCalibrator.Tests
{
    public sealed class SearchComparisonTests
    {
        private static readonly CandidateDescriptor[] Pool =
        {
            new CandidateDescriptor("AoS", 32, true, candidateId: "aos32"),
            new CandidateDescriptor("AoS", 64, true, candidateId: "aos64"),
            new CandidateDescriptor("SoA", 32, false, candidateId: "soa32"),
        };

        private static CalibrationRunSettings Settings() => new CalibrationRunSettings
        {
            ElementCount = 8, HoldoutElementCount = 9, PreflightElementCount = 3, PreflightTicks = 1,
            WarmupBlocks = 1, MinimumWarmupSeconds = 0, SamplesPerCandidate = 6, BoundarySamplesPerCandidate = 4,
            MaximumTicksPerBlock = 1, TargetBlockMilliseconds = .01, BootstrapIterations = 100, LifetimeTicks = 4,
        };

        [TestCase(true)]
        [TestCase(false)]
        public void ActualExecutionRetainsBaselinesAndFreezesBothSelectionsBeforeHoldout(bool adaptiveFirst)
        {
            var factory = new FixtureFactory();
            var names = new HashSet<string>();
            SearchComparisonResult result = ScenarioCalibrationEngine.RunSearchComparison(factory, Settings(), Pool,
                new AdvantageEnvelopeAxis(8, 4, 1, 1, "FrameFaithful"), new string('A', 64), adaptiveFirst,
                (name, value) =>
                {
                    Assert.That(names.Add(name), Is.True, "Immutable artifacts must have unique names");
                    if (name == "quick") Assert.That(((ScenarioCalibrationProfile)value).HoldoutBaselineResult, Is.Null);
                    if (name == "frozen-selections")
                    {
                        var frozen = (SearchComparisonResult)value;
                        Assert.That(frozen.Adaptive.HoldoutBaselineResult, Is.Null);
                        Assert.That(frozen.Exhaustive.HoldoutBaselineResult, Is.Null);
                        factory.Frozen = true;
                    }
                    // Synthetic test persistence token only, never formal evidence.
                    return new string('B', 64);
                });
            Assert.That(factory.Frozen, Is.True);
            Assert.That(result.ExecutedFinalistIds, Does.Contain("aos32").And.Contain("aos64"));
            Assert.That(result.ExhaustiveComponentEvaluationCount, Is.EqualTo(3 * (6 + 4 + 4)));
            Assert.That(result.AdaptiveComponentEvaluationCount, Is.EqualTo(
                (3 + result.AdaptiveFullCandidateCount) * (6 + 4 + 4)));
            Assert.That(result.AdaptiveCalibrationMilliseconds, Is.EqualTo(result.SharedPreflightMilliseconds +
                result.QuickMilliseconds + result.PlanningMilliseconds + result.AdaptiveFullMilliseconds));
            Assert.That(result.Quick.TicksPerBlock, Is.EqualTo(result.Exhaustive.TicksPerBlock));
            Assert.That(result.Adaptive.TicksPerBlock, Is.EqualTo(result.Exhaustive.TicksPerBlock));
            Assert.That(result.FinalEvidenceRequirementsUnchanged, Is.True);
        }

        [Test]
        public void RejectsReusedHoldoutSeedBeforeAnyExecution()
        {
            var settings = Settings(); settings.HoldoutSeed = settings.CalibrationSeed;
            Assert.Throws<ArgumentException>(() => ScenarioCalibrationEngine.RunSearchComparison(
                new FixtureFactory(), settings, Pool, new AdvantageEnvelopeAxis(8, 4, 1, 1, "FrameFaithful"),
                new string('A', 64), true, (name, value) => new string('B', 64)));
        }

        [Test]
        public void RejectsFactoryThatSubstitutesFrozenPool()
        {
            Assert.Throws<InvalidOperationException>(() => ScenarioCalibrationEngine.RunSearchComparison(
                new FixtureFactory { Substitute = true }, Settings(), Pool,
                new AdvantageEnvelopeAxis(8, 4, 1, 1, "FrameFaithful"), new string('A', 64), true,
                (name, value) => new string('B', 64)));
        }

        [Test]
        public void RejectsMissingEvidenceHash()
        {
            Assert.Throws<InvalidOperationException>(() => ScenarioCalibrationEngine.RunSearchComparison(
                new FixtureFactory(), Settings(), Pool, new AdvantageEnvelopeAxis(8, 4, 1, 1, "FrameFaithful"),
                new string('A', 64), true, (name, value) => "unavailable"));
        }

        [TestCase("settings")]
        [TestCase("quick")]
        [TestCase("frozen-selections")]
        public void RejectsPersistenceMutationBeforeHoldout(string stage)
        {
            var factory = new FixtureFactory();
            Assert.Throws<InvalidOperationException>(() => ScenarioCalibrationEngine.RunSearchComparison(
                factory, Settings(), Pool, new AdvantageEnvelopeAxis(8, 4, 1, 1, "FrameFaithful"),
                new string('A', 64), true, (name, value) =>
                {
                    if (name == stage)
                    {
                        if (value is CalibrationRunSettings settings) settings.HoldoutSeed = settings.CalibrationSeed;
                        else if (value is ScenarioCalibrationProfile quick) quick.CalibrationResults[0].ResidentSamplesMillisecondsPerTick[0] += 1;
                        else if (value is SearchComparisonResult frozen) frozen.Adaptive.CalibrationDecision.SelectedCandidate = Pool[0];
                        // Change an unconditional field even if selection already fell back.
                        if (value is SearchComparisonResult result) result.Adaptive.LifetimeTicks++;
                    }
                    return new string('B', 64);
                }));
            Assert.That(factory.Frozen, Is.False);
        }

        private sealed class FixtureFactory : ICalibrationScenarioFactory
        {
            public bool Frozen;
            public bool Substitute;
            public ScenarioDescriptor Descriptor => new ScenarioDescriptor("fixture", "Fixture", 1, "unit-test spin");
            public ICalibrationScenario Create(int count, uint seed, CandidateDescriptor[] candidates = null)
            {
                if (count == 9) Assert.That(Frozen, Is.True, "Holdout was generated before both choices were persisted");
                return new FixtureScenario(Descriptor, count, seed, Substitute ? new[] { Pool[0], Pool[2] } : candidates ?? Pool);
            }
        }

        private sealed class FixtureScenario : ICalibrationScenario, IParityValidator
        {
            private readonly FixtureCandidate[] _candidates;
            public FixtureScenario(ScenarioDescriptor descriptor, int count, uint seed, CandidateDescriptor[] candidates)
            {
                Descriptor = descriptor; DatasetHash = seed.ToString();
                _candidates = new FixtureCandidate[candidates.Length];
                for (int i = 0; i < candidates.Length; i++) _candidates[i] = new FixtureCandidate(candidates[i], count);
            }
            public ScenarioDescriptor Descriptor { get; }
            public string DatasetHash { get; }
            public int CandidateCount => _candidates.Length;
            public int ReferenceCandidateIndex => 0;
            public IParityValidator ParityValidator => this;
            public ICalibrationCandidate GetCandidate(int index) => _candidates[index];
            public ParityReport Validate(ICalibrationCandidate baseline, ICalibrationCandidate candidate, float tolerance)
                => ParityReport.Pass(candidate.ElementCount, "fixture", "fixture");
            public void Dispose() { }
        }

        private sealed class FixtureCandidate : ICalibrationCandidate, IBoundaryCost
        {
            public FixtureCandidate(CandidateDescriptor descriptor, int count) { Descriptor = descriptor; ElementCount = count; }
            public CandidateDescriptor Descriptor { get; }
            public int ElementCount { get; }
            public long ResidentBytes => 64;
            public IBoundaryCost BoundaryCost => this;
            public string ExportedStateHash => "fixture";
            BoundaryCostDescriptor IBoundaryCost.Descriptor => new BoundaryCostDescriptor("fixture", "fixture");
            public void Execute(int ticks, float dt) { Thread.SpinWait(Descriptor.IsBaseline ? 2000 : 100); }
            public void Ingress() { Thread.SpinWait(50); }
            public void Export() { Thread.SpinWait(50); }
            public void Dispose() { }
        }
    }
}
