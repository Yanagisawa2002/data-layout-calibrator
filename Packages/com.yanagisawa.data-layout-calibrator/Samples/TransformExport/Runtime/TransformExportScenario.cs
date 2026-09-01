using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace Yanagisawa.DataLayoutCalibrator.Samples.TransformExport
{
    public sealed class TransformExportScenarioFactory : ICalibrationScenarioFactory
    {
        private static readonly ScenarioDescriptor Scenario = new ScenarioDescriptor(
            "transform-export-v1",
            "Transform Export (Negative Control)",
            1,
            "Export a full LocalToWorld matrix plus entity identity from immutable transform records.");

        public ScenarioDescriptor Descriptor => Scenario;

        public ICalibrationScenario Create(
            int elementCount,
            uint seed,
            CandidateDescriptor[] candidates = null)
        {
            return new TransformExportScenario(elementCount, seed, candidates);
        }
    }

    public sealed class TransformExportScenario : ICalibrationScenario
    {
        private static readonly int[] BatchSizes = { 32, 64, 128, 256 };
        private static readonly LayoutKind[] Layouts = { LayoutKind.AoS, LayoutKind.SoA };

        private readonly NativeArray<TransformRecord> _canonicalInput;
        private readonly TransformExportCandidate[] _candidates;
        private readonly TransformExportParityValidator _parity = new TransformExportParityValidator();
        private bool _disposed;

        internal TransformExportScenario(int count, uint seed, CandidateDescriptor[] requested)
        {
            _canonicalInput = TransformExportDataSet.Create(count, seed, Allocator.Persistent);
            DatasetHash = $"0x{TransformExportValidation.ComputeInputHash(_canonicalInput):X16}";
            CandidateDescriptor[] definitions = requested ?? CreateDefaultCandidates();
            if (definitions.Length == 0)
                throw new ArgumentException("At least one candidate is required.", nameof(requested));

            _candidates = new TransformExportCandidate[definitions.Length];
            try
            {
                int referenceIndex = -1;
                for (int index = 0; index < definitions.Length; index++)
                {
                    _candidates[index] = new TransformExportCandidate(definitions[index], _canonicalInput);
                    if (referenceIndex < 0 && definitions[index].IsBaseline)
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

        public ScenarioDescriptor Descriptor => new TransformExportScenarioFactory().Descriptor;

        public string DatasetHash { get; }

        public int CandidateCount => _candidates.Length;

        public int ReferenceCandidateIndex { get; }

        public IParityValidator ParityValidator => _parity;

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

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(TransformExportScenario));
        }
    }

    internal sealed class TransformExportCandidate : ICalibrationCandidate, IBoundaryCost
    {
        private static readonly BoundaryCostDescriptor BoundaryDescriptor = new BoundaryCostDescriptor(
            "Canonical NativeArray<TransformRecord> to candidate-owned persistent layout storage.",
            "Candidate-owned NativeArray<TransformExportRecord> to the canonical consumer buffer.");

        private readonly NativeArray<TransformRecord> _canonicalInput;
        private readonly NativeArray<TransformRecord> _aosRecords;
        private readonly NativeArray<TransformExportRecord> _residentOutput;
        private readonly NativeArray<TransformExportRecord> _canonicalExport;
        private readonly LayoutKind _layout;
        private TransformSoAStorage _soa;
        private bool _disposed;

        public TransformExportCandidate(
            CandidateDescriptor descriptor,
            NativeArray<TransformRecord> canonicalInput)
        {
            _layout = ParseLayout(descriptor);
            if (_layout != LayoutKind.AoS && _layout != LayoutKind.SoA)
                throw new ArgumentOutOfRangeException(nameof(descriptor), "TransformExport supports AoS and SoA.");

            Descriptor = descriptor;
            _canonicalInput = canonicalInput;
            _aosRecords = _layout == LayoutKind.AoS
                ? new NativeArray<TransformRecord>(canonicalInput.Length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory)
                : default;
            _soa = _layout == LayoutKind.SoA
                ? TransformSoAStorage.Allocate(canonicalInput.Length, Allocator.Persistent)
                : default;
            _residentOutput = new NativeArray<TransformExportRecord>(
                canonicalInput.Length,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            _canonicalExport = new NativeArray<TransformExportRecord>(
                canonicalInput.Length,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            try
            {
                Ingress();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public CandidateDescriptor Descriptor { get; }

        public int ElementCount => _canonicalInput.Length;

        public long ResidentBytes
        {
            get
            {
                long layoutBytes = (long)ElementCount * UnsafeUtility.SizeOf<TransformRecord>();
                long outputBytes = (long)ElementCount * UnsafeUtility.SizeOf<TransformExportRecord>();
                return layoutBytes + outputBytes;
            }
        }

        public IBoundaryCost BoundaryCost => this;

        BoundaryCostDescriptor IBoundaryCost.Descriptor => BoundaryDescriptor;

        public string ExportedStateHash
        {
            get
            {
                ThrowIfDisposed();
                return $"0x{TransformExportValidation.ComputeOutputHash(_canonicalExport):X16}";
            }
        }

        internal NativeArray<TransformExportRecord> CanonicalExport => _canonicalExport;

        public void Execute(int ticks, float fixedDeltaTime)
        {
            ThrowIfDisposed();
            if (ticks <= 0)
                throw new ArgumentOutOfRangeException(nameof(ticks));

            for (int tick = 0; tick < ticks; tick++)
                Schedule().Complete();
        }

        public void Ingress()
        {
            ThrowIfDisposed();
            if (_layout == LayoutKind.AoS)
            {
                _aosRecords.CopyFrom(_canonicalInput);
                return;
            }

            new TransformSoAIngressJob
            {
                Source = _canonicalInput,
                Positions = _soa.Positions,
                Rotations = _soa.Rotations,
                Scales = _soa.Scales,
                EntityIds = _soa.EntityIds,
                Flags = _soa.Flags,
            }.Schedule(ElementCount, 128).Complete();
        }

        public void Export()
        {
            ThrowIfDisposed();
            _canonicalExport.CopyFrom(_residentOutput);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            if (_aosRecords.IsCreated)
                _aosRecords.Dispose();
            _soa.Dispose();
            if (_residentOutput.IsCreated)
                _residentOutput.Dispose();
            if (_canonicalExport.IsCreated)
                _canonicalExport.Dispose();
            _disposed = true;
        }

        private JobHandle Schedule()
        {
            if (_layout == LayoutKind.AoS)
            {
                return new TransformAoSExportJob
                {
                    Records = _aosRecords,
                    Output = _residentOutput,
                }.Schedule(ElementCount, Math.Max(1, Descriptor.LogicalBatchSize));
            }

            return new TransformSoAExportJob
            {
                Positions = _soa.Positions,
                Rotations = _soa.Rotations,
                Scales = _soa.Scales,
                EntityIds = _soa.EntityIds,
                Flags = _soa.Flags,
                Output = _residentOutput,
            }.Schedule(ElementCount, Math.Max(1, Descriptor.LogicalBatchSize));
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(TransformExportCandidate));
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
                "TransformExport supports AoS and SoA.");
        }
    }

    internal sealed class TransformExportParityValidator : IParityValidator
    {
        public ParityReport Validate(
            ICalibrationCandidate reference,
            ICalibrationCandidate candidate,
            float tolerance)
        {
            if (!(reference is TransformExportCandidate expectedCandidate) ||
                !(candidate is TransformExportCandidate actualCandidate))
            {
                return ParityReport.Fail(0, -1, string.Empty, string.Empty, "TransformExport parity requires matching candidates.");
            }

            expectedCandidate.Export();
            actualCandidate.Export();
            NativeArray<TransformExportRecord> expected = expectedCandidate.CanonicalExport;
            NativeArray<TransformExportRecord> actual = actualCandidate.CanonicalExport;
            string expectedHash = expectedCandidate.ExportedStateHash;
            string actualHash = actualCandidate.ExportedStateHash;
            if (expected.Length != actual.Length)
                return ParityReport.Fail(0, -1, expectedHash, actualHash, "Candidate output counts differ.");

            for (int index = 0; index < expected.Length; index++)
            {
                if (!TransformExportValidation.ApproximatelyEqual(expected[index], actual[index], tolerance, out string reason))
                    return ParityReport.Fail(expected.Length, index, expectedHash, actualHash, reason);
            }

            if (!string.Equals(expectedHash, actualHash, StringComparison.Ordinal))
                return ParityReport.Fail(expected.Length, -1, expectedHash, actualHash, "Quantized output hashes differ.");
            return ParityReport.Pass(expected.Length, expectedHash, actualHash);
        }
    }
}
