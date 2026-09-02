using System;
using Unity.Collections;
using Unity.Jobs;

namespace Yanagisawa.DataLayoutCalibrator.Samples.ParticleIntegrate
{
    public sealed class ParticleIntegrateScenarioFactory : ICalibrationScenarioFactory
    {
        private static readonly ScenarioDescriptor Scenario = new ScenarioDescriptor(
            "particle-integrate-v2",
            "Particle Integrate",
            2,
            "Integrate position, velocity and lifetime while preserving cold rotation/category fields.");

        public ScenarioDescriptor Descriptor => Scenario;

        public ICalibrationScenario Create(
            int elementCount,
            uint seed,
            CandidateDescriptor[] candidates = null)
        {
            return new ParticleIntegrateScenario(elementCount, seed, candidates);
        }
    }

    public sealed class ParticleIntegrateScenario : ICalibrationScenario
    {
        private static readonly int[] BatchSizes = { 32, 64, 128, 256 };
        private static readonly ExecutionPolicy[] Executions =
        {
            ExecutionPolicy.FrameFaithful,
            ExecutionPolicy.DependencyChain,
        };

        private readonly NativeArray<ParticleRecord> _canonicalInput;
        private readonly ParticleIntegrateCandidate[] _candidates;
        private readonly ParticleParityValidator _parityValidator = new ParticleParityValidator();
        private bool _disposed;

        internal ParticleIntegrateScenario(
            int elementCount,
            uint seed,
            CandidateDescriptor[] requestedCandidates)
        {
            if (elementCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(elementCount));

            _canonicalInput = ParticleDataSet.Create(elementCount, seed, Allocator.Persistent);
            DatasetHash = FormatHash(ParticleStateValidation.ComputeHash(_canonicalInput));
            CandidateDescriptor[] definitions = requestedCandidates ?? CreateDefaultCandidates();
            if (definitions.Length == 0)
                throw new ArgumentException("At least one candidate is required.", nameof(requestedCandidates));

            _candidates = new ParticleIntegrateCandidate[definitions.Length];
            try
            {
                int referenceIndex = -1;
                for (int index = 0; index < definitions.Length; index++)
                {
                    CandidateDescriptor definition = definitions[index];
                    _candidates[index] = new ParticleIntegrateCandidate(definition, _canonicalInput);
                    if (referenceIndex < 0 && definition.IsBaseline)
                        referenceIndex = index;
                }

                if (referenceIndex < 0)
                    throw new ArgumentException("Every scenario instance requires an AoS reference candidate.");
                ReferenceCandidateIndex = referenceIndex;
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public ScenarioDescriptor Descriptor => new ParticleIntegrateScenarioFactory().Descriptor;

        public string DatasetHash { get; }

        public int CandidateCount => _candidates.Length;

        public int ReferenceCandidateIndex { get; }

        public IParityValidator ParityValidator => _parityValidator;

        public ICalibrationCandidate GetCandidate(int index)
        {
            ThrowIfDisposed();
            if ((uint)index >= (uint)_candidates.Length)
                throw new ArgumentOutOfRangeException(nameof(index));
            return _candidates[index];
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            if (_candidates != null)
            {
                for (int index = 0; index < _candidates.Length; index++)
                    _candidates[index]?.Dispose();
            }

            if (_canonicalInput.IsCreated)
                _canonicalInput.Dispose();
            _disposed = true;
        }

        private static CandidateDescriptor[] CreateDefaultCandidates()
        {
            const int familyCount = 4;
            var candidates = new CandidateDescriptor[
                familyCount * Executions.Length * BatchSizes.Length];
            int cursor = 0;
            AddFamily(
                candidates,
                ref cursor,
                new LayoutPolicy("AoS"),
                new KernelPolicy("ScalarBranched", KernelControlFlow.Branched),
                true,
                0);
            AddFamily(
                candidates,
                ref cursor,
                new LayoutPolicy("AoS"),
                new KernelPolicy("ScalarBranchless", KernelControlFlow.Branchless),
                true,
                1);
            AddFamily(
                candidates,
                ref cursor,
                new LayoutPolicy("SoA"),
                new KernelPolicy("ScalarBranched", KernelControlFlow.Branched),
                false,
                2);
            AddFamily(
                candidates,
                ref cursor,
                new LayoutPolicy("AoSoA8", blockWidth: 8),
                new KernelPolicy("PackedBranchless8", KernelControlFlow.Branchless, vectorWidth: 8),
                false,
                3);
            return candidates;
        }

        private static void AddFamily(
            CandidateDescriptor[] candidates,
            ref int cursor,
            LayoutPolicy layout,
            KernelPolicy kernel,
            bool isBaseline,
            int familySortOrder)
        {
            for (int execution = 0; execution < Executions.Length; execution++)
            for (int batch = 0; batch < BatchSizes.Length; batch++)
            {
                candidates[cursor++] = new CandidateDescriptor(
                    layout,
                    kernel,
                    BatchPolicy.JobBatch(BatchSizes[batch]),
                    Executions[execution],
                    isBaseline,
                    sortOrder: (familySortOrder * 100) + (execution * 10) + batch);
            }
        }

        private static string FormatHash(ulong hash) => $"0x{hash:X16}";

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ParticleIntegrateScenario));
        }
    }

    internal sealed class ParticleIntegrateCandidate : ICalibrationCandidate, IBoundaryCost
    {
        private static readonly BoundaryCostDescriptor BoundaryDescriptor = new BoundaryCostDescriptor(
            "Canonical NativeArray<ParticleRecord> to candidate-owned persistent layout storage.",
            "Candidate-owned layout storage to canonical NativeArray<ParticleRecord>.");

        private readonly NativeArray<ParticleRecord> _canonicalInput;
        private readonly NativeArray<ParticleRecord> _canonicalExport;
        private readonly LayoutKind _layout;
        private readonly ParticleKernelKind _kernel;
        private readonly ExecutionPolicy _execution;
        private ParticleLayoutDomain _domain;
        private bool _disposed;

        public ParticleIntegrateCandidate(
            CandidateDescriptor descriptor,
            NativeArray<ParticleRecord> canonicalInput)
        {
            Descriptor = descriptor.NormalizePolicies();
            Descriptor.ValidateFactorConsistency();
            _layout = ParseLayout(Descriptor);
            _kernel = ParseKernel(Descriptor, _layout);
            _execution = ParseExecution(Descriptor);
            _canonicalInput = canonicalInput;
            _canonicalExport = new NativeArray<ParticleRecord>(
                canonicalInput.Length,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            try
            {
                _domain = ParticleLayoutDomain.Create(
                    _layout,
                    _kernel,
                    Descriptor.LogicalBatchSize,
                    canonicalInput);
            }
            catch
            {
                if (_canonicalExport.IsCreated)
                    _canonicalExport.Dispose();
                throw;
            }
        }

        public CandidateDescriptor Descriptor { get; }

        public int ElementCount => _canonicalInput.Length;

        public long ResidentBytes => _domain.ResidentBytes;

        public IBoundaryCost BoundaryCost => this;

        BoundaryCostDescriptor IBoundaryCost.Descriptor => BoundaryDescriptor;

        public string ExportedStateHash
        {
            get
            {
                ThrowIfDisposed();
                return $"0x{ParticleStateValidation.ComputeHash(_canonicalExport):X16}";
            }
        }

        internal NativeArray<ParticleRecord> CanonicalExport => _canonicalExport;

        public void Execute(int ticks, float fixedDeltaTime)
        {
            ThrowIfDisposed();
            if (ticks <= 0)
                throw new ArgumentOutOfRangeException(nameof(ticks));

            switch (_execution.Topology)
            {
                case ExecutionTopology.FrameFaithful:
                    for (int tick = 0; tick < ticks; tick++)
                        _domain.Schedule(fixedDeltaTime).Complete();
                    return;

                case ExecutionTopology.DependencyChain:
                    JobHandle dependency = default;
                    for (int tick = 0; tick < ticks; tick++)
                        dependency = _domain.Schedule(fixedDeltaTime, dependency);
                    dependency.Complete();
                    return;

                default:
                    throw new NotSupportedException(
                        $"ParticleIntegrate does not declare reorderable TemporalBlock semantics: {_execution.PolicyId}.");
            }
        }

        public void Ingress()
        {
            ThrowIfDisposed();
            _domain.Ingress(_canonicalInput);
        }

        public void Export()
        {
            ThrowIfDisposed();
            _domain.Export(_canonicalExport);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _domain?.Dispose();
            if (_canonicalExport.IsCreated)
                _canonicalExport.Dispose();
            _disposed = true;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ParticleIntegrateCandidate));
        }

        private static LayoutKind ParseLayout(CandidateDescriptor descriptor)
        {
            if (Enum.TryParse(descriptor.EffectiveLayout.PolicyId, true, out LayoutKind layout) &&
                Enum.IsDefined(typeof(LayoutKind), layout))
            {
                return layout;
            }

            throw new ArgumentOutOfRangeException(
                nameof(descriptor),
                descriptor.LayoutId,
                "ParticleIntegrate supports AoS, SoA, and AoSoA8.");
        }

        private static ParticleKernelKind ParseKernel(
            CandidateDescriptor descriptor,
            LayoutKind layout)
        {
            KernelPolicy policy = descriptor.EffectiveKernel;
            if (string.Equals(policy.PolicyId, "LegacyUnspecified", StringComparison.Ordinal))
            {
                switch (layout)
                {
                    case LayoutKind.AoS:
                    case LayoutKind.SoA:
                        return ParticleKernelKind.ScalarBranched;
                    case LayoutKind.AoSoA8:
                        return ParticleKernelKind.PackedBranchless8;
                }
            }

            if (layout == LayoutKind.AoS &&
                string.Equals(policy.PolicyId, "ScalarBranched", StringComparison.Ordinal) &&
                policy.ControlFlow == KernelControlFlow.Branched &&
                policy.VectorWidth == 1)
            {
                return ParticleKernelKind.ScalarBranched;
            }
            if (layout == LayoutKind.AoS &&
                string.Equals(policy.PolicyId, "ScalarBranchless", StringComparison.Ordinal) &&
                policy.ControlFlow == KernelControlFlow.Branchless &&
                policy.VectorWidth == 1)
            {
                return ParticleKernelKind.ScalarBranchless;
            }
            if (layout == LayoutKind.SoA &&
                string.Equals(policy.PolicyId, "ScalarBranched", StringComparison.Ordinal) &&
                policy.ControlFlow == KernelControlFlow.Branched &&
                policy.VectorWidth == 1)
            {
                return ParticleKernelKind.ScalarBranched;
            }
            if (layout == LayoutKind.AoSoA8 &&
                string.Equals(policy.PolicyId, "PackedBranchless8", StringComparison.Ordinal) &&
                policy.ControlFlow == KernelControlFlow.Branchless &&
                policy.VectorWidth == 8)
            {
                return ParticleKernelKind.PackedBranchless8;
            }

            throw new ArgumentOutOfRangeException(
                nameof(descriptor),
                policy.PolicyId,
                $"ParticleIntegrate does not implement kernel {policy.PolicyId} for {layout}.");
        }

        private static ExecutionPolicy ParseExecution(CandidateDescriptor descriptor)
        {
            ExecutionPolicy policy = descriptor.EffectiveExecution;
            if (policy.Topology == ExecutionTopology.FrameFaithful &&
                string.Equals(policy.PolicyId, "FrameFaithful", StringComparison.Ordinal))
            {
                return policy;
            }
            if (policy.Topology == ExecutionTopology.DependencyChain &&
                string.Equals(policy.PolicyId, "DependencyChain", StringComparison.Ordinal))
            {
                return policy;
            }

            throw new ArgumentOutOfRangeException(
                nameof(descriptor),
                policy.PolicyId,
                "ParticleIntegrate implements only FrameFaithful and DependencyChain execution.");
        }
    }

    internal sealed class ParticleParityValidator : IParityValidator
    {
        public ParityReport Validate(
            ICalibrationCandidate reference,
            ICalibrationCandidate candidate,
            float tolerance)
        {
            if (!(reference is ParticleIntegrateCandidate referenceCandidate) ||
                !(candidate is ParticleIntegrateCandidate measuredCandidate))
            {
                return ParityReport.Fail(
                    0,
                    -1,
                    string.Empty,
                    string.Empty,
                    "Particle parity requires two ParticleIntegrate candidates.");
            }

            referenceCandidate.Export();
            measuredCandidate.Export();
            NativeArray<ParticleRecord> expected = referenceCandidate.CanonicalExport;
            NativeArray<ParticleRecord> actual = measuredCandidate.CanonicalExport;
            string expectedHash = referenceCandidate.ExportedStateHash;
            string actualHash = measuredCandidate.ExportedStateHash;
            if (expected.Length != actual.Length)
            {
                return ParityReport.Fail(
                    Math.Min(expected.Length, actual.Length),
                    -1,
                    expectedHash,
                    actualHash,
                    "Candidate logical record counts differ.");
            }

            for (int index = 0; index < expected.Length; index++)
            {
                if (!ParticleStateValidation.ApproximatelyEqual(
                        expected[index],
                        actual[index],
                        tolerance,
                        out string failure))
                {
                    return ParityReport.Fail(
                        expected.Length,
                        index,
                        expectedHash,
                        actualHash,
                        failure);
                }
            }

            if (!string.Equals(expectedHash, actualHash, StringComparison.Ordinal))
            {
                return ParityReport.Fail(
                    expected.Length,
                    -1,
                    expectedHash,
                    actualHash,
                    "Quantized state hashes differ.");
            }

            return ParityReport.Pass(expected.Length, expectedHash, actualHash);
        }
    }
}
