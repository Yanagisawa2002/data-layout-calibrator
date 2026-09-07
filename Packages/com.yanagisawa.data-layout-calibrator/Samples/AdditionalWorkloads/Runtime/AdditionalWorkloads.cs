using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace Yanagisawa.DataLayoutCalibrator.Samples.AdditionalWorkloads
{
    [GenerateDataLayout("spatial-point-v1", 1)]
    public struct SpatialPoint
    {
        [DataLayoutField(0, DataLayoutFieldTemperature.Hot)] public float3 Position;
        [DataLayoutField(1, DataLayoutFieldTemperature.Hot)] public float Weight;
        [DataLayoutField(2, DataLayoutFieldTemperature.Cold)] public int EntityId;
        [DataLayoutField(3, DataLayoutFieldTemperature.Cold)] public float4 Metadata;
    }

    [GenerateDataLayout("animation-state-v1", 1)]
    public struct AnimationState
    {
        [DataLayoutField(0, DataLayoutFieldTemperature.Hot)] public float Phase;
        [DataLayoutField(1, DataLayoutFieldTemperature.Hot)] public float Speed;
        [DataLayoutField(2, DataLayoutFieldTemperature.Hot)] public float Weight;
        [DataLayoutField(3, DataLayoutFieldTemperature.Hot)] public float Target;
        [DataLayoutField(4, DataLayoutFieldTemperature.Hot)] public int State;
        [DataLayoutField(5, DataLayoutFieldTemperature.Cold)] public int EntityId;
        [DataLayoutField(6, DataLayoutFieldTemperature.Cold)] public float4 Metadata;
    }

    public sealed class SpatialNeighborhoodScenarioFactory : ICalibrationScenarioFactory
    {
        public ScenarioDescriptor Descriptor => new ScenarioDescriptor("spatial-neighborhood-v1", "Spatial Neighborhood", 1,
            "Radius 0.32 weighted query in a jittered 0.25 grid with shuffled record IDs and complete 27-cell adjacency. Immutable input; full record/result export.");
        public ICalibrationScenario Create(int elementCount, uint seed, CandidateDescriptor[] candidates = null) =>
            new AdditionalScenario(this, elementCount, seed, candidates, true);
    }

    public sealed class AnimationStateScenarioFactory : ICalibrationScenarioFactory
    {
        public ScenarioDescriptor Descriptor => new ScenarioDescriptor("animation-state-v1", "Animation State Update", 1,
            "Streaming conditional phase, state transition and blend-weight update; cold identity/metadata retained in full export.");
        public ICalibrationScenario Create(int elementCount, uint seed, CandidateDescriptor[] candidates = null) =>
            new AdditionalScenario(this, elementCount, seed, candidates, false);
    }

    public static class AdditionalWorkloadCandidates
    {
        public const uint CalibrationSeed = 0x19283745;
        public const uint HoldoutSeed = 0x81726354;

        public static CandidateDescriptor[] Create(bool spatial)
        {
            var result = new CandidateDescriptor[8];
            int index = 0;
            foreach (string layout in new[] { "AoS", "SoA" })
            foreach (ExecutionPolicy execution in new[] { ExecutionPolicy.FrameFaithful, ExecutionPolicy.DependencyChain })
            foreach (int batch in new[] { 64, 128 })
                result[index] = new CandidateDescriptor(new LayoutPolicy(layout),
                    new KernelPolicy(spatial ? "RadiusGather" : "StateTransition", KernelControlFlow.Branched),
                    BatchPolicy.JobBatch(batch), execution, isBaseline: layout == "AoS", sortOrder: index++);
            return result;
        }
    }

    internal sealed class AdditionalScenario : ICalibrationScenario, IParityValidator
    {
        private readonly AdditionalCandidate[] _candidates;
        private bool _disposed;
        public ScenarioDescriptor Descriptor { get; }
        public string DatasetHash { get; }
        public int CandidateCount => _candidates.Length;
        public int ReferenceCandidateIndex { get; }
        public IParityValidator ParityValidator => this;

        public AdditionalScenario(ICalibrationScenarioFactory factory, int count, uint seed, CandidateDescriptor[] definitions, bool spatial)
        {
            if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
            Descriptor = factory.Descriptor;
            definitions = definitions ?? AdditionalWorkloadCandidates.Create(spatial);
            if (definitions.Length == 0) throw new ArgumentException("At least one candidate is required.");
            _candidates = new AdditionalCandidate[definitions.Length];
            int baseline = -1;
            try
            {
                for (int i = 0; i < definitions.Length; i++)
                {
                    _candidates[i] = spatial
                        ? (AdditionalCandidate)new SpatialCandidate(definitions[i], count, seed)
                        : new AnimationCandidate(definitions[i], count, seed);
                    if (definitions[i].IsBaseline && baseline < 0) baseline = i;
                }
                if (baseline < 0) throw new ArgumentException("An AoS baseline is required.");
                ReferenceCandidateIndex = baseline;
                DatasetHash = _candidates[baseline].InputHash;
            }
            catch { Dispose(); throw; }
        }

        public ICalibrationCandidate GetCandidate(int index)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(AdditionalScenario));
            return _candidates[index];
        }

        public ParityReport Validate(ICalibrationCandidate reference, ICalibrationCandidate candidate, float tolerance)
        {
            reference.BoundaryCost.Export(); candidate.BoundaryCost.Export();
            string expected = reference.ExportedStateHash, actual = candidate.ExportedStateHash;
            if (!(reference is AdditionalCandidate a) || !(candidate is AdditionalCandidate b) || a.GetType() != b.GetType())
                return ParityReport.Fail(0, -1, expected, actual, "Workload types differ.");
            int mismatch = a.FirstMismatch(b, tolerance);
            return mismatch < 0 && expected == actual
                ? ParityReport.Pass(a.ElementCount, expected, actual)
                : ParityReport.Fail(a.ElementCount, mismatch, expected, actual, "Full canonical fields or hashes differ.");
        }

        public void Dispose()
        {
            if (_disposed) return;
            foreach (AdditionalCandidate candidate in _candidates) candidate?.Dispose();
            _disposed = true;
        }
    }

    internal abstract class AdditionalCandidate : ICalibrationCandidate, IBoundaryCost
    {
        protected readonly bool SoA;
        protected bool Disposed;
        public CandidateDescriptor Descriptor { get; }
        public int ElementCount { get; }
        public abstract long ResidentBytes { get; }
        public IBoundaryCost BoundaryCost => this;
        BoundaryCostDescriptor IBoundaryCost.Descriptor => new BoundaryCostDescriptor(
            "Full canonical records and query adjacency copied into persistent generated storage; allocation-free.",
            "Full records including cold fields, and any query output, exported into persistent canonical buffers.");
        public abstract string ExportedStateHash { get; }
        public abstract string InputHash { get; }
        protected AdditionalCandidate(CandidateDescriptor descriptor, int count, string kernel)
        {
            Descriptor = descriptor.NormalizePolicies();
            Descriptor.ValidateFactorConsistency();
            LayoutPolicy layout = Descriptor.EffectiveLayout;
            KernelPolicy policy = Descriptor.EffectiveKernel;
            ExecutionPolicy execution = Descriptor.EffectiveExecution;
            if ((layout.PolicyId != "AoS" && layout.PolicyId != "SoA") || layout.BlockWidth != 1 ||
                layout.AlignmentBytes != 0 || layout.PaddingBytes != 0 ||
                policy.PolicyId != kernel || policy.ControlFlow != KernelControlFlow.Branched || policy.VectorWidth != 1 ||
                (!execution.Equals(ExecutionPolicy.FrameFaithful) && !execution.Equals(ExecutionPolicy.DependencyChain)) ||
                !Descriptor.EffectiveBatch.Equals(BatchPolicy.JobBatch(Descriptor.LogicalBatchSize)) ||
                Descriptor.LogicalBatchSize <= 0 || (Descriptor.IsBaseline && layout.PolicyId != "AoS"))
                throw new ArgumentException("Unsupported additional-workload policy combination.", nameof(descriptor));
            SoA = layout.PolicyId == "SoA";
            ElementCount = count;
        }
        public void Execute(int ticks, float fixedDeltaTime)
        {
            Check();
            if (ticks <= 0 || !math.isfinite(fixedDeltaTime) || fixedDeltaTime <= 0)
                throw new ArgumentOutOfRangeException(nameof(ticks));
            JobHandle dependency = default;
            for (int i = 0; i < ticks; i++)
            {
                dependency = Schedule(fixedDeltaTime, dependency);
                if (Descriptor.EffectiveExecution.Topology == ExecutionTopology.FrameFaithful) dependency.Complete();
            }
            dependency.Complete();
        }
        protected abstract JobHandle Schedule(float dt, JobHandle dependency);
        public abstract void Ingress();
        public abstract void Export();
        public abstract int FirstMismatch(AdditionalCandidate other, float tolerance);
        public abstract void Dispose();
        protected void Check() { if (Disposed) throw new ObjectDisposedException(GetType().Name); }
        protected static ulong Mix(ulong hash, uint value) => unchecked((hash ^ value) * 1099511628211UL);
        protected static ulong MixFloat(ulong hash, float value) => Mix(hash, unchecked((uint)(int)math.round(value * 10000f)));
        protected static ulong MixVector(ulong hash, float4 v) => MixFloat(MixFloat(MixFloat(MixFloat(hash, v.x), v.y), v.z), v.w);
    }

    internal sealed class SpatialCandidate : AdditionalCandidate
    {
        private NativeArray<SpatialPoint> _input, _export;
        private NativeArray<int> _canonicalNeighbors, _neighbors;
        private NativeArray<float2> _output, _exportOutput;
        private SpatialPointGeneratedAoSStorage _aos;
        private SpatialPointGeneratedSoAStorage _soa;
        public override long ResidentBytes => (long)ElementCount * (UnsafeUtility.SizeOf<SpatialPoint>() + 27 * sizeof(int) + 8);
        public override string InputHash => Hash(_input, default);
        public override string ExportedStateHash { get { Check(); return Hash(_export, _exportOutput); } }
        public SpatialCandidate(CandidateDescriptor descriptor, int count, uint seed) : base(descriptor, count, "RadiusGather")
        {
            try
            {
                _input = new NativeArray<SpatialPoint>(count, Allocator.Persistent);
                _export = new NativeArray<SpatialPoint>(count, Allocator.Persistent);
                _canonicalNeighbors = new NativeArray<int>(checked(count * 27), Allocator.Persistent);
                _neighbors = new NativeArray<int>(_canonicalNeighbors.Length, Allocator.Persistent);
                _output = new NativeArray<float2>(count, Allocator.Persistent);
                _exportOutput = new NativeArray<float2>(count, Allocator.Persistent);
                if (SoA) _soa = SpatialPointGeneratedSoAStorage.Allocate(count, Allocator.Persistent);
                else _aos = SpatialPointGeneratedAoSStorage.Allocate(count, Allocator.Persistent);
                BuildInput(seed);
                Ingress();
            }
            catch { Dispose(); throw; }
        }
        private void BuildInput(uint seed)
        {
            int count = ElementCount, side = (int)Math.Ceiling(Math.Pow(count, 1d / 3d));
            while ((long)side * side * side < count) side++;
            var shuffled = new int[count];
            for (int i = 0; i < count; i++) shuffled[i] = i;
            var random = new Unity.Mathematics.Random(seed == 0 ? 1u : seed);
            for (int i = count - 1; i > 0; i--) { int j = random.NextInt(i + 1); int temp = shuffled[i]; shuffled[i] = shuffled[j]; shuffled[j] = temp; }
            for (int cell = 0; cell < count; cell++)
            {
                int record = shuffled[cell], x = cell % side, y = (cell / side) % side, z = cell / (side * side);
                _input[record] = new SpatialPoint { Position = new float3(x, y, z) * 0.25f + random.NextFloat3(-0.01f, 0.01f),
                    Weight = random.NextFloat(0.5f, 1.5f), EntityId = record, Metadata = random.NextFloat4() };
                int offset = record * 27;
                for (int dz = -1; dz <= 1; dz++)
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy, nz = z + dz;
                    int neighbor = nx + side * (ny + side * nz);
                    _canonicalNeighbors[offset++] = nx >= 0 && nx < side && ny >= 0 && ny < side && nz >= 0 && nz < side && neighbor < count
                        ? shuffled[neighbor] : -1;
                }
            }
        }
        public override void Ingress()
        {
            Check(); if (SoA) _soa.Ingress(_input); else _aos.Ingress(_input);
            _neighbors.CopyFrom(_canonicalNeighbors);
            for (int i = 0; i < ElementCount; i++) _output[i] = default;
        }
        public override void Export() { Check(); if (SoA) _soa.Export(_export); else _aos.Export(_export); _exportOutput.CopyFrom(_output); }
        protected override JobHandle Schedule(float dt, JobHandle dependency) => SoA
            ? new SpatialSoAQueryJob { Positions = _soa.Field_Position, Weights = _soa.Field_Weight, Neighbors = _neighbors, Output = _output }.Schedule(ElementCount, Descriptor.LogicalBatchSize, dependency)
            : new SpatialAoSQueryJob { Records = _aos.Records, Neighbors = _neighbors, Output = _output }.Schedule(ElementCount, Descriptor.LogicalBatchSize, dependency);
        public override int FirstMismatch(AdditionalCandidate other, float tolerance)
        {
            var b = (SpatialCandidate)other;
            if (ElementCount != b.ElementCount) return 0;
            for (int i = 0; i < ElementCount; i++)
                if (!math.all(_export[i].Position == b._export[i].Position) || _export[i].Weight != b._export[i].Weight ||
                    _export[i].EntityId != b._export[i].EntityId || !math.all(_export[i].Metadata == b._export[i].Metadata) ||
                    !math.all(math.isfinite(_exportOutput[i])) || !math.all(math.isfinite(b._exportOutput[i])) ||
                    math.any(math.abs(_exportOutput[i] - b._exportOutput[i]) > tolerance)) return i;
            return -1;
        }
        // Independent O(N^2) oracle for small correctness fixtures; not part of measured execution.
        public bool ValidateOracle(float tolerance)
        {
            Export();
            for (int i = 0; i < ElementCount; i++)
            {
                double sum = 0; int count = 0;
                for (int j = 0; j < ElementCount; j++)
                    if (i != j && math.distancesq(_input[i].Position, _input[j].Position) <= 0.32f * 0.32f)
                    { sum += _input[j].Weight; count++; }
                if (Math.Abs(_exportOutput[i].x - sum) > tolerance || _exportOutput[i].y != count) return false;
            }
            return true;
        }
        private static string Hash(NativeArray<SpatialPoint> records, NativeArray<float2> results)
        {
            ulong h = 14695981039346656037UL;
            for (int i = 0; i < records.Length; i++)
            {
                SpatialPoint r = records[i]; h = MixVector(h, new float4(r.Position, r.Weight));
                h = MixVector(Mix(h, (uint)r.EntityId), r.Metadata);
                if (results.IsCreated) h = MixFloat(MixFloat(h, results[i].x), results[i].y);
            }
            return "0x" + h.ToString("X16");
        }
        public override void Dispose()
        {
            if (Disposed) return;
            _aos.Dispose(); _soa.Dispose();
            if (_input.IsCreated) _input.Dispose(); if (_export.IsCreated) _export.Dispose();
            if (_neighbors.IsCreated) _neighbors.Dispose(); if (_canonicalNeighbors.IsCreated) _canonicalNeighbors.Dispose();
            if (_output.IsCreated) _output.Dispose(); if (_exportOutput.IsCreated) _exportOutput.Dispose(); Disposed = true;
        }
    }

    [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard)]
    public struct SpatialAoSQueryJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<SpatialPoint> Records;
        [ReadOnly] public NativeArray<int> Neighbors;
        [WriteOnly] public NativeArray<float2> Output;
        public void Execute(int index)
        {
            float3 center = Records[index].Position; float sum = 0; int count = 0;
            for (int n = 0; n < 27; n++)
            {
                int j = Neighbors[index * 27 + n];
                if (j >= 0 && j != index && math.distancesq(center, Records[j].Position) <= 0.32f * 0.32f)
                { sum += Records[j].Weight; count++; }
            }
            Output[index] = new float2(sum, count);
        }
    }

    [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard)]
    public struct SpatialSoAQueryJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float3> Positions;
        [ReadOnly] public NativeArray<float> Weights;
        [ReadOnly] public NativeArray<int> Neighbors;
        [WriteOnly] public NativeArray<float2> Output;
        public void Execute(int index)
        {
            float3 center = Positions[index]; float sum = 0; int count = 0;
            for (int n = 0; n < 27; n++)
            {
                int j = Neighbors[index * 27 + n];
                if (j >= 0 && j != index && math.distancesq(center, Positions[j]) <= 0.32f * 0.32f)
                { sum += Weights[j]; count++; }
            }
            Output[index] = new float2(sum, count);
        }
    }

    internal sealed class AnimationCandidate : AdditionalCandidate
    {
        private NativeArray<AnimationState> _input, _export;
        private AnimationStateGeneratedAoSStorage _aos;
        private AnimationStateGeneratedSoAStorage _soa;
        public override long ResidentBytes => (long)ElementCount * UnsafeUtility.SizeOf<AnimationState>();
        public override string InputHash => Hash(_input);
        public override string ExportedStateHash { get { Check(); return Hash(_export); } }
        public AnimationCandidate(CandidateDescriptor descriptor, int count, uint seed) : base(descriptor, count, "StateTransition")
        {
            try
            {
                _input = new NativeArray<AnimationState>(count, Allocator.Persistent);
                _export = new NativeArray<AnimationState>(count, Allocator.Persistent);
                if (SoA) _soa = AnimationStateGeneratedSoAStorage.Allocate(count, Allocator.Persistent);
                else _aos = AnimationStateGeneratedAoSStorage.Allocate(count, Allocator.Persistent);
                var random = new Unity.Mathematics.Random(seed == 0 ? 1u : seed);
                for (int i = 0; i < count; i++) _input[i] = new AnimationState { Phase = random.NextFloat(), Speed = random.NextFloat(0.2f, 2f),
                    Weight = random.NextFloat(), Target = random.NextFloat(), State = random.NextInt(3), EntityId = i, Metadata = random.NextFloat4() };
                Ingress();
            }
            catch { Dispose(); throw; }
        }
        public override void Ingress() { Check(); if (SoA) _soa.Ingress(_input); else _aos.Ingress(_input); }
        public override void Export() { Check(); if (SoA) _soa.Export(_export); else _aos.Export(_export); }
        protected override JobHandle Schedule(float dt, JobHandle dependency) => SoA
            ? new AnimationSoAStepJob { Phase = _soa.Field_Phase, Speed = _soa.Field_Speed, Weight = _soa.Field_Weight,
                Target = _soa.Field_Target, State = _soa.Field_State, DeltaTime = dt }.Schedule(ElementCount, Descriptor.LogicalBatchSize, dependency)
            : new AnimationAoSStepJob { Records = _aos.Records, DeltaTime = dt }.Schedule(ElementCount, Descriptor.LogicalBatchSize, dependency);
        public override int FirstMismatch(AdditionalCandidate other, float tolerance)
        {
            var b = (AnimationCandidate)other;
            if (ElementCount != b.ElementCount) return 0;
            for (int i = 0; i < ElementCount; i++)
            {
                AnimationState x = _export[i], y = b._export[i];
                if (!math.isfinite(x.Phase) || !math.isfinite(y.Phase) || !math.isfinite(x.Weight) || !math.isfinite(y.Weight) ||
                    math.abs(x.Phase - y.Phase) > tolerance || math.abs(x.Weight - y.Weight) > tolerance ||
                    x.Speed != y.Speed || x.Target != y.Target || x.State != y.State || x.EntityId != y.EntityId || !math.all(x.Metadata == y.Metadata)) return i;
            }
            return -1;
        }
        public bool ValidateOracle(int ticks, float dt, float tolerance)
        {
            Export();
            for (int i = 0; i < ElementCount; i++)
            {
                AnimationState expected = _input[i];
                for (int t = 0; t < ticks; t++)
                {
                    if (expected.State == 2) continue;
                    expected.Phase += expected.Speed * dt;
                    if (expected.Phase >= 1f) { expected.Phase -= (float)Math.Floor(expected.Phase); expected.State = 1 - expected.State; }
                    float desired = expected.State == 0 ? expected.Target : 1f - expected.Target;
                    expected.Weight = Math.Max(0f, Math.Min(1f, expected.Weight + (desired - expected.Weight) * Math.Min(1f, 4f * dt)));
                }
                if (Math.Abs(expected.Phase - _export[i].Phase) > tolerance || Math.Abs(expected.Weight - _export[i].Weight) > tolerance || expected.State != _export[i].State) return false;
            }
            return true;
        }
        private static string Hash(NativeArray<AnimationState> records)
        {
            ulong h = 14695981039346656037UL;
            for (int i = 0; i < records.Length; i++)
            {
                AnimationState r = records[i]; h = MixVector(h, new float4(r.Phase, r.Speed, r.Weight, r.Target));
                h = MixVector(Mix(Mix(h, (uint)r.State), (uint)r.EntityId), r.Metadata);
            }
            return "0x" + h.ToString("X16");
        }
        public override void Dispose()
        {
            if (Disposed) return;
            _aos.Dispose(); _soa.Dispose(); if (_input.IsCreated) _input.Dispose(); if (_export.IsCreated) _export.Dispose(); Disposed = true;
        }
    }

    public static class AnimationKernel
    {
        public static void Step(ref float phase, float speed, ref float weight, float target, ref int state, float dt)
        {
            if (state == 2) return;
            phase += speed * dt;
            if (phase >= 1f) { phase = math.frac(phase); state = 1 - state; }
            float desired = state == 0 ? target : 1f - target;
            weight = math.saturate(weight + (desired - weight) * math.min(1f, 4f * dt));
        }
    }
    [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard)]
    public struct AnimationAoSStepJob : IJobParallelFor
    {
        public NativeArray<AnimationState> Records;
        public float DeltaTime;
        public void Execute(int index)
        {
            AnimationState value = Records[index];
            AnimationKernel.Step(ref value.Phase, value.Speed, ref value.Weight, value.Target, ref value.State, DeltaTime);
            Records[index] = value;
        }
    }
    [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard)]
    public struct AnimationSoAStepJob : IJobParallelFor
    {
        public NativeArray<float> Phase, Weight;
        public NativeArray<int> State;
        [ReadOnly] public NativeArray<float> Speed, Target;
        public float DeltaTime;
        public void Execute(int index)
        {
            float phase = Phase[index], weight = Weight[index]; int state = State[index];
            AnimationKernel.Step(ref phase, Speed[index], ref weight, Target[index], ref state, DeltaTime);
            Phase[index] = phase; Weight[index] = weight; State[index] = state;
        }
    }
}
