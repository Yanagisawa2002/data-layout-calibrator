using System;

namespace Yanagisawa.DataLayoutCalibrator
{
    /// <summary>
    /// Stable identity and human-readable contract for one reusable calibration workload.
    /// </summary>
    [Serializable]
    public struct ScenarioDescriptor
    {
        public string ScenarioId;
        public string DisplayName;
        public int ContractVersion;
        public string ResidentOperation;

        public ScenarioDescriptor(
            string scenarioId,
            string displayName,
            int contractVersion,
            string residentOperation)
        {
            ScenarioId = scenarioId;
            DisplayName = displayName;
            ContractVersion = contractVersion;
            ResidentOperation = residentOperation;
        }
    }

    /// <summary>
    /// Declares exactly what crosses the layout boundary. The calibration engine times
    /// both operations independently and amortizes them over an explicit lifetime.
    /// </summary>
    [Serializable]
    public struct BoundaryCostDescriptor
    {
        public string IngressContract;
        public string ExportContract;

        public BoundaryCostDescriptor(string ingressContract, string exportContract)
        {
            IngressContract = ingressContract;
            ExportContract = exportContract;
        }
    }

    [Serializable]
    public struct ParityReport
    {
        public bool Passed;
        public int ComparedElementCount;
        public int FirstMismatchIndex;
        public string ReferenceStateHash;
        public string CandidateStateHash;
        public string Reason;

        public static ParityReport Pass(
            int comparedElementCount,
            string referenceStateHash,
            string candidateStateHash)
        {
            return new ParityReport
            {
                Passed = true,
                ComparedElementCount = comparedElementCount,
                FirstMismatchIndex = -1,
                ReferenceStateHash = referenceStateHash,
                CandidateStateHash = candidateStateHash,
                Reason = "Equivalent canonical exports.",
            };
        }

        public static ParityReport Fail(
            int comparedElementCount,
            int firstMismatchIndex,
            string referenceStateHash,
            string candidateStateHash,
            string reason)
        {
            return new ParityReport
            {
                Passed = false,
                ComparedElementCount = comparedElementCount,
                FirstMismatchIndex = firstMismatchIndex,
                ReferenceStateHash = referenceStateHash,
                CandidateStateHash = candidateStateHash,
                Reason = reason,
            };
        }
    }

    /// <summary>
    /// Boundary operations must reuse storage. Managed allocation is measured by the
    /// harness, so an implementation that allocates is rejected rather than rewarded.
    /// </summary>
    public interface IBoundaryCost
    {
        BoundaryCostDescriptor Descriptor { get; }

        void Ingress();

        void Export();
    }

    /// <summary>
    /// One concrete layout and scheduling candidate. Execute contains the literal job
    /// schedule sites so Burst AOT does not depend on reflection or generic discovery.
    /// </summary>
    public interface ICalibrationCandidate : IDisposable
    {
        CandidateDescriptor Descriptor { get; }

        int ElementCount { get; }

        long ResidentBytes { get; }

        IBoundaryCost BoundaryCost { get; }

        void Execute(int ticks, float fixedDeltaTime);

        string ExportedStateHash { get; }
    }

    public interface IParityValidator
    {
        ParityReport Validate(
            ICalibrationCandidate reference,
            ICalibrationCandidate candidate,
            float tolerance);
    }

    /// <summary>
    /// Owns immutable canonical input plus all candidates created from that input.
    /// </summary>
    public interface ICalibrationScenario : IDisposable
    {
        ScenarioDescriptor Descriptor { get; }

        string DatasetHash { get; }

        int CandidateCount { get; }

        int ReferenceCandidateIndex { get; }

        IParityValidator ParityValidator { get; }

        ICalibrationCandidate GetCandidate(int index);
    }

    /// <summary>
    /// Public plugin entry point. Supplying candidates allows holdout to instantiate
    /// only the AoS baseline and the calibration winner.
    /// </summary>
    public interface ICalibrationScenarioFactory
    {
        ScenarioDescriptor Descriptor { get; }

        ICalibrationScenario Create(
            int elementCount,
            uint seed,
            CandidateDescriptor[] candidates = null);
    }
}
