using System;
using Unity.Collections;

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
        private static readonly LayoutKind[] Layouts =
        {
            LayoutKind.AoS,
            LayoutKind.SoA,
            LayoutKind.AoSoA8,
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
            var candidates = new CandidateDescriptor[Layouts.Length * BatchSizes.Length];
            int cursor = 0;
            for (int layout = 0; layout < Layouts.Length; layout++)
            for (int batch = 0; batch < BatchSizes.Length; batch++)
                candidates[cursor++] = new CandidateDescriptor(Layouts[layout], BatchSizes[batch]);
            return candidates;
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
        private ParticleLayoutDomain _domain;
        private bool _disposed;

        public ParticleIntegrateCandidate(
            CandidateDescriptor descriptor,
            NativeArray<ParticleRecord> canonicalInput)
        {
            Descriptor = descriptor;
            _layout = ParseLayout(descriptor);
            _canonicalInput = canonicalInput;
            _canonicalExport = new NativeArray<ParticleRecord>(
                canonicalInput.Length,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            try
            {
                _domain = ParticleLayoutDomain.Create(
                    _layout,
                    descriptor.LogicalBatchSize,
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

            for (int tick = 0; tick < ticks; tick++)
                _domain.Schedule(fixedDeltaTime).Complete();
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
            if (Enum.TryParse(descriptor.LayoutId, true, out LayoutKind layout) &&
                Enum.IsDefined(typeof(LayoutKind), layout))
            {
                return layout;
            }

            throw new ArgumentOutOfRangeException(
                nameof(descriptor),
                descriptor.LayoutId,
                "ParticleIntegrate supports AoS, SoA, and AoSoA8.");
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
