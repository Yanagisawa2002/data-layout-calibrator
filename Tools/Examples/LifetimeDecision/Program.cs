using System;
using System.Text.Json;
using Yanagisawa.DataLayoutCalibrator;

// Invented costs illustrate the public API, not any measured workload.
// No candidate executes, and no timers, allocation counters or Unity APIs are used.
// This model assumes one terminal export and no recurring exports. The estimator
// has no export-cadence parameter; these totals cannot describe another cadence.
var incomplete = ConservativeLifetimeEnvelope.Estimate(null);
if (incomplete.Status != LifetimeEnvelopeStatus.Unknown)
    throw new InvalidOperationException("Missing evidence must remain Unknown.");
Print("missing-cost-evidence", incomplete);

var illustrativeCosts = new LifetimeProcessCosts[5];
for (int i = 0; i < illustrativeCosts.Length; i++)
{
    illustrativeCosts[i] = new LifetimeProcessCosts
    {
        ProcessId = "synthetic-pair-" + i,
        SourceFingerprint = new string('A', 64), // Canonical format, deliberately fictional identity.
        BaselineCandidateId = "illustrative-tuned-aos",
        CandidateId = "illustrative-alternative",
        Origin = TimingObservationOrigin.SyntheticFixture,
        IncludesConstructionIngressExportDisposal = true,
        BaselineResidentScoreMillisecondsPerTick = 1.0 + i * 0.01,
        CandidateResidentScoreMillisecondsPerTick = 0.6 + i * 0.01,
        // Invented construction + ingress + ONE terminal export + disposal costs.
        BaselineOneTimeMilliseconds = 0.25 + 0.25 + 0.25 + 0.25,
        CandidateOneTimeMilliseconds = 5.0 + 10.0 + (9.0 + i) + 1.0
    };
}
var estimate = ConservativeLifetimeEnvelope.Estimate(illustrativeCosts,
    minimumImprovementPercent: 10, iterations: 1000);
if (estimate.Status != LifetimeEnvelopeStatus.FiniteSustainedBreakEven ||
    estimate.Origin != TimingObservationOrigin.SyntheticFixture)
    throw new InvalidOperationException("Complete illustrative costs must yield finite bounds and retain their synthetic origin.");
if (estimate.IndependentProcessCount != illustrativeCosts.Length ||
    estimate.LowerLifetimeTicks < 1 || estimate.LowerLifetimeTicks > estimate.UpperLifetimeTicks)
    throw new InvalidOperationException("The conditional bounds or paired process association are invalid.");
Print("complete-synthetic-cost-model", estimate);

// Missing a teardown/export cost cannot be made valid by a faster resident kernel.
illustrativeCosts[0].IncludesConstructionIngressExportDisposal = false;
var incompleteBoundary = ConservativeLifetimeEnvelope.Estimate(illustrativeCosts, iterations: 1000);
if (incompleteBoundary.Status != LifetimeEnvelopeStatus.Unknown)
    throw new InvalidOperationException("Incomplete lifecycle costs must remain Unknown.");
Print("missing-boundary-cost", incompleteBoundary);

static void Print(string example, ConservativeLifetimeEstimate result)
{
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        example, inputOrigin = "synthetic API illustration",
        resultOrigin = result.Origin.ToString(), sourceIdentity = "fictional; not deployment provenance",
        execution = "cost inference only; no workload execution",
        exportCadence = "one terminal export; no recurring exports",
        costBoundary = "construction + ingress + resident ticks + terminal export + disposal",
        status = result.Status.ToString(), estimand = result.Estimand,
        conditionalLowerLifetimeTicks = result.Status == LifetimeEnvelopeStatus.FiniteSustainedBreakEven ?
            (double?)result.LowerLifetimeTicks : null,
        conditionalUpperLifetimeTicks = result.Status == LifetimeEnvelopeStatus.FiniteSustainedBreakEven ?
            (double?)result.UpperLifetimeTicks : null,
        diagnostic = result.Diagnostic,
        deploymentProfileEmitted = false,
        allocationEligibilityEstablished = false
    }));
}
