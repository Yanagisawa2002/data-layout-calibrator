using System;
using System.IO;
using System.Text.Json;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using Yanagisawa.DataLayoutCalibrator.Samples.ParticleIntegrate;
using Yanagisawa.DataLayoutCalibrator.Samples.TransformExport;
using Yanagisawa.DataLayoutCalibrator.Samples.ExternalWorkloads;

internal static class Program
{
    private static int assertions;
    private static void Check(bool value, string reason)
    { assertions++; if (!value) throw new Exception(reason); }
    private static void Near(float a, float b, string reason, float tolerance = 0f)
    { Check(math.isfinite(a) && math.isfinite(b) && math.abs(a - b) <= tolerance * math.max(1f, math.max(math.abs(a), math.abs(b))), reason); }
    private static void Same(float3 a, float3 b, string reason, float tolerance = 0f)
    { Near(a.x, b.x, reason, tolerance); Near(a.y, b.y, reason, tolerance); Near(a.z, b.z, reason, tolerance); }
    private static void Equal(ParticleRecord a, ParticleRecord b)
    {
        Same(a.Position, b.Position, "position"); Same(a.Velocity, b.Velocity, "velocity"); Near(a.Lifetime, b.Lifetime, "lifetime");
        Check(math.all(math.asuint(a.Rotation.value) == math.asuint(b.Rotation.value)), "rotation payload");
        Check(a.Category == b.Category, "category payload");
    }
    private static void Reject(Action action)
    {
        bool rejected = false;
        try { action(); } catch (ArgumentException) { rejected = true; } catch (ObjectDisposedException) { rejected = true; }
        Check(rejected, "invalid call accepted");
    }
    private static ParticleRecord Particle(int i) => new ParticleRecord
    {
        Position = new float3(i * 0.25f, -i * 0.5f, i + 1f), Velocity = new float3(-0.25f, i * 0.125f, 0.5f),
        Rotation = new quaternion(new float4(i + 1, -i, math.asfloat(0x80000000u), math.asfloat(0x7fc00123u))),
        Lifetime = new[] { -11f, -0.125f, 0f, 0.125f, 0.25f, 1f, 9f }[i % 7], Category = i % 2 == 0 ? int.MinValue + i : int.MaxValue - i,
    };

    private static void ParticleCells()
    {
        foreach (int count in new[] { 0, 1, 3, 4, 5, 7, 8, 9, 15, 16, 17 })
        foreach (bool branchless in new[] { false, true })
        foreach (bool chain in new[] { false, true })
        {
            var input = new NativeArray<ParticleRecord>(count, Allocator.Temp);
            for (int i = 0; i < count; i++) input[i] = Particle(i);
            var aos = ParticleAoSStorage.FromRecords(input, Allocator.Temp);
            var soa = ParticleSoAStorage.FromRecords(input, Allocator.Temp);
            var hot = ParticleHotColdStorage.FromRecords(input, Allocator.Temp);
            var packed = ParticleRecordGeneratedPackedAoSoA8Storage.FromRecords(input, Allocator.Temp);
            try
            {
                hot.Rotations.ResetModelAccesses(); hot.Categories.ResetModelAccesses();
                ModelScheduler.Reset(chain);
                JobHandle a = default, s = default, h = default, p = default;
                for (int step = 0; step < 5; step++)
                {
                    float dt = step == 4 ? 11f : 0.125f; // upstream respawns once even for > one lifetime.
                    a = branchless ? ParticleJobScheduler.ScheduleBranchless(ref aos, 7, dt, a) : ParticleJobScheduler.Schedule(ref aos, 7, dt, a);
                    s = branchless ? new ParticleSoABranchlessStepJob { Positions = soa.Positions, Velocities = soa.Velocities, Lifetimes = soa.Lifetimes, DeltaTime = dt }.Schedule(count, 7, s)
                        : ParticleJobScheduler.Schedule(ref soa, 7, dt, s);
                    h = hot.ScheduleStep(dt, 7, branchless, h);
                    p = new ParticleAoSoA8StepJob { Blocks = packed.HotBlocks, DeltaTime = dt }.Schedule(packed.BlockCount, 1, p);
                    if (!chain) { a.Complete(); s.Complete(); h.Complete(); p.Complete(); }
                }
                if (chain) { a.Complete(); s.Complete(); h.Complete(); p.Complete(); }
                Check(ModelScheduler.Schedules == 20 && ModelScheduler.Completes == (chain ? 4 : 20), "execution topology");
                Check(hot.Rotations.ModelReads == 0 && hot.Rotations.ModelWrites == 0 && hot.Categories.ModelReads == 0 && hot.Categories.ModelWrites == 0, "cold field touched by hot step");
                for (int i = 0; i < count; i++)
                { Equal(aos.ReadRecord(i), soa.ReadRecord(i)); Equal(aos.ReadRecord(i), hot.ReadRecord(i)); Equal(aos.ReadRecord(i), packed.ReadRecord(i)); }
                var output = new NativeArray<ParticleRecord>(count, Allocator.Temp);
                hot.Export(output);
                for (int i = 0; i < count; i++) Equal(aos.ReadRecord(i), output[i]);
                output.Dispose();
                Reject(() => hot.ScheduleStep(0.1f, 0, branchless));
            }
            finally { packed.Dispose(); hot.Dispose(); soa.Dispose(); aos.Dispose(); input.Dispose(); }
        }
        Check(UnsafeUtility.SizeOf<ParticleHotRecord>() == 28, "hot record fields/stride");
        Check(UnsafeUtility.SizeOf<ParticleRecord>() == 48, "canonical stride");
    }

    private static void Codecs()
    {
        foreach (int count in new[] { 0, 1, 3, 4, 5, 7, 8, 9, 15, 16, 17 })
        {
            var input = new NativeArray<ParticleRecord>(count, Allocator.Temp);
            var output = new NativeArray<ParticleRecord>(count, Allocator.Temp);
            for (int i = 0; i < count; i++) input[i] = Particle(i);
            var four = ParticleRecordGeneratedPackedAoSoA4Storage.FromRecords(input, Allocator.Temp);
            var eight = ParticleRecordGeneratedPackedAoSoA8Storage.FromRecords(input, Allocator.Temp);
            var sixteen = ParticleRecordGeneratedPackedAoSoA16Storage.FromRecords(input, Allocator.Temp);
            try
            {
                four.HotBlocks.ResetModelAccesses(); eight.HotBlocks.ResetModelAccesses(); sixteen.HotBlocks.ResetModelAccesses();
                four.Ingress(input); eight.Ingress(input); sixteen.Ingress(input);
                Check(four.HotBlocks.ModelReads == 0 && four.HotBlocks.ModelWrites == four.BlockCount, "four block ingress");
                Check(eight.HotBlocks.ModelReads == 0 && eight.HotBlocks.ModelWrites == eight.BlockCount, "eight block ingress");
                Check(sixteen.HotBlocks.ModelReads == 0 && sixteen.HotBlocks.ModelWrites == sixteen.BlockCount, "sixteen block ingress");
                ModelScheduler.Reset(true);
                ParticleBlockExportScheduler.Schedule(ref four, output, 7).Complete();
                Check(four.HotBlocks.ModelReads == four.BlockCount, "four block export loads");
                for (int i = 0; i < count; i++) Equal(input[i], output[i]);
                ParticleBlockExportScheduler.Schedule(ref eight, output, 7).Complete();
                Check(eight.HotBlocks.ModelReads == eight.BlockCount, "eight block export loads");
                for (int i = 0; i < count; i++) Equal(input[i], output[i]);
                ParticleBlockExportScheduler.Schedule(ref sixteen, output, 7).Complete();
                Check(sixteen.HotBlocks.ModelReads == sixteen.BlockCount, "sixteen block export loads");
                for (int i = 0; i < count; i++) Equal(input[i], output[i]);
                // Poison padding; no extra source lanes may escape either boundary.
                if (count % 8 != 0)
                {
                    var tail = eight.HotBlocks[eight.BlockCount - 1];
                    for (int lane = count % 8; lane < 8; lane++) ParticleRecordGeneratedPackedAoSoA8Storage.Write_Lifetime(ref tail, lane, float.NaN);
                    eight.HotBlocks[eight.BlockCount - 1] = tail;
                    eight.Export(output);
                    for (int i = 0; i < count; i++) Equal(input[i], output[i]);
                    eight.Ingress(input);
                    tail = eight.HotBlocks[eight.BlockCount - 1];
                    for (int lane = count % 8; lane < 8; lane++) Near(ParticleRecordGeneratedPackedAoSoA8Storage.Read_Lifetime(tail, lane), 0f, "tail reset");
                }
                var wrong = new NativeArray<ParticleRecord>(count + 1, Allocator.Temp);
                Reject(() => eight.Export(wrong)); Reject(() => eight.Ingress(wrong));
                Reject(() => ParticleBlockExportScheduler.Schedule(ref eight, wrong, 7)); wrong.Dispose();
            }
            finally { sixteen.Dispose(); eight.Dispose(); four.Dispose(); output.Dispose(); input.Dispose(); }
        }
    }

    private static void PackedMatrixCells()
    {
        foreach (int count in new[] { 0, 1, 3, 4, 5, 7, 8, 9, 15, 16, 17 })
        foreach (int kernel in new[] { 0, 1, 2 })
        foreach (bool chain in new[] { false, true })
        {
            var input = new NativeArray<ParticleRecord>(count, Allocator.Temp);
            for (int i = 0; i < count; i++) input[i] = Particle(i);
            var aos = ParticleAoSStorage.FromRecords(input, Allocator.Temp);
            var s4 = ParticleRecordGeneratedPackedAoSoA4Storage.FromRecords(input, Allocator.Temp);
            var s8 = ParticleRecordGeneratedPackedAoSoA8Storage.FromRecords(input, Allocator.Temp);
            var s16 = ParticleRecordGeneratedPackedAoSoA16Storage.FromRecords(input, Allocator.Temp);
            try
            {
                ModelScheduler.Reset(chain);
                JobHandle reference = default, h4 = default, h8 = default, h16 = default;
                for (int step = 0; step < 3; step++)
                {
                    reference = ParticleJobScheduler.Schedule(ref aos, 7, 0.125f, reference);
                    h4 = kernel == 0 ? new ParticleAoSoA4ScalarBranchedStepJob { Blocks = s4.HotBlocks, DeltaTime = 0.125f }.Schedule(s4.BlockCount, 1, h4)
                        : kernel == 1 ? new ParticleAoSoA4ScalarBranchlessStepJob { Blocks = s4.HotBlocks, DeltaTime = 0.125f }.Schedule(s4.BlockCount, 1, h4)
                        : new ParticleAoSoA4StepJob { Blocks = s4.HotBlocks, DeltaTime = 0.125f }.Schedule(s4.BlockCount, 1, h4);
                    h8 = kernel == 0 ? new ParticleAoSoA8ScalarBranchedStepJob { Blocks = s8.HotBlocks, DeltaTime = 0.125f }.Schedule(s8.BlockCount, 1, h8)
                        : kernel == 1 ? new ParticleAoSoA8ScalarBranchlessStepJob { Blocks = s8.HotBlocks, DeltaTime = 0.125f }.Schedule(s8.BlockCount, 1, h8)
                        : new ParticleAoSoA8StepJob { Blocks = s8.HotBlocks, DeltaTime = 0.125f }.Schedule(s8.BlockCount, 1, h8);
                    h16 = kernel == 0 ? new ParticleAoSoA16ScalarBranchedStepJob { Blocks = s16.HotBlocks, DeltaTime = 0.125f }.Schedule(s16.BlockCount, 1, h16)
                        : kernel == 1 ? new ParticleAoSoA16ScalarBranchlessStepJob { Blocks = s16.HotBlocks, DeltaTime = 0.125f }.Schedule(s16.BlockCount, 1, h16)
                        : new ParticleAoSoA16StepJob { Blocks = s16.HotBlocks, DeltaTime = 0.125f }.Schedule(s16.BlockCount, 1, h16);
                    if (!chain) { reference.Complete(); h4.Complete(); h8.Complete(); h16.Complete(); }
                }
                if (chain) { reference.Complete(); h4.Complete(); h8.Complete(); h16.Complete(); }
                for (int i = 0; i < count; i++)
                { Equal(aos.ReadRecord(i), s4.ReadRecord(i)); Equal(aos.ReadRecord(i), s8.ReadRecord(i)); Equal(aos.ReadRecord(i), s16.ReadRecord(i)); }
            }
            finally { s16.Dispose(); s8.Dispose(); s4.Dispose(); aos.Dispose(); input.Dispose(); }
        }
    }

    private static void Transforms()
    {
        foreach (int count in new[] { 0, 1, 3, 4, 5, 8, 9, 17 })
        {
            var input = new NativeArray<TransformRecord>(count, Allocator.Temp);
            var output = new NativeArray<TransformExportRecord>(count, Allocator.Temp);
            for (int i = 0; i < count; i++) input[i] = new TransformRecord
            {
                Position = new float3(i, -i, i * 0.25f), Scale = new float3(i % 2 == 0 ? -2f : 0f, 0.75f, i * 0.25f),
                Rotation = i == 0 ? quaternion.identity : quaternion.EulerXYZ(new float3(i * 0.125f, -i * 0.25f, i * 0.375f)),
                EntityId = int.MinValue + i, Flags = int.MaxValue - i,
            };
            var storage = TransformPacked4Storage.FromRecords(input, Allocator.Temp);
            try
            {
                ModelScheduler.Reset(true); storage.ScheduleExport(output, 7).Complete();
                for (int i = 0; i < count; i++)
                {
                    var r = input[i]; var actual = output[i]; var expected = float4x4.TRS(r.Position, r.Rotation, r.Scale);
                    for (int column = 0; column < 4; column++)
                    for (int row = 0; row < 4; row++) Near(actual.LocalToWorld[column][row], expected[column][row], "TRS", 2e-6f);
                    Check(actual.EntityId == r.EntityId && actual.Flags == r.Flags, "transform identity");
                }
            }
            finally { storage.Dispose(); output.Dispose(); input.Dispose(); }
        }
    }

    private static void ExternalContracts()
    {
        Check(BabelStreamContract.DefaultArraySize == 33554432 && BabelStreamContract.DefaultIterations == 100, "BabelStream defaults");
        foreach (int count in new[] { 1, 3, 9, 17 })
        {
            var a = new NativeArray<double>(count, Allocator.Temp); var b = new NativeArray<double>(count, Allocator.Temp);
            var c = new NativeArray<double>(count, Allocator.Temp); var sum = new NativeArray<double>(1, Allocator.Temp);
            try
            {
                for (int i = 0; i < count; i++) { a[i] = BabelStreamContract.StartA; b[i] = BabelStreamContract.StartB; c[i] = BabelStreamContract.StartC; }
                double ga = BabelStreamContract.StartA, gb = BabelStreamContract.StartB, gc = BabelStreamContract.StartC;
                for (int step = 0; step < 3; step++)
                {
                    for (int i = 0; i < count; i++) new BabelCopyJob { A = a, C = c }.Execute(i);
                    for (int i = 0; i < count; i++) new BabelMulJob { B = b, C = c }.Execute(i);
                    for (int i = 0; i < count; i++) new BabelAddJob { A = a, B = b, C = c }.Execute(i);
                    for (int i = 0; i < count; i++) new BabelTriadJob { A = a, B = b, C = c }.Execute(i);
                    new BabelDotContractJob { A = a, B = b, Sum = sum }.Execute();
                    BabelStreamContract.AdvanceClassicGold(ref ga, ref gb, ref gc, count, out double gs);
                    for (int i = 0; i < count; i++) Check(BabelStreamContract.Matches(a[i], ga) && BabelStreamContract.Matches(b[i], gb) && BabelStreamContract.Matches(c[i], gc), "BabelStream upstream checker");
                    Check(BabelStreamContract.Matches(sum[0], gs, true), "BabelStream dot checker");
                }
                Check(!BabelStreamContract.Matches(a[0] + 0.1, ga) && !BabelStreamContract.Matches(double.NaN, ga), "BabelStream checker sensitivity");
                for (int i = 0; i < count; i++) new BabelNstreamJob { A = a, B = b, C = c }.Execute(i);
                ga += gb + BabelStreamContract.Scalar * gc;
                Check(BabelStreamContract.Matches(a[0], ga), "optional nstream");
            }
            finally { sum.Dispose(); c.Dispose(); b.Dispose(); a.Dispose(); }
        }
        Check(LlamaNBodyContract.DefaultCount == 65536 && LlamaNBodyContract.DefaultSteps == 5, "LLAMA defaults");
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "llama-msvc-prefix17.json")));
        var native = fixture.RootElement.GetProperty("records");
        Check(native.GetArrayLength() == 17, "native RNG prefix fixture count");
        foreach (bool nativeInput in new[] { false, true })
        foreach (int count in new[] { 0, 1, 3, 4, 5, 9, 17 })
        foreach (bool chain in new[] { false, true })
        {
            var input = new NativeArray<LlamaParticle>(count, Allocator.Temp); var output = new NativeArray<LlamaParticle>(count, Allocator.Temp);
            var reference = new LlamaParticle[count];
            for (int i = 0; i < count; i++) input[i] = reference[i] = new LlamaParticle
            { Position = new float3(i * 0.25f, -i * 0.5f, (i % 2) * 0.25f), Velocity = new float3(0.1f, -0.2f, 0.3f), Mass = i % 2 == 0 ? -0.01f : 0.02f };
            if (nativeInput)
                for (int i = 0; i < count; i++)
                {
                    var fields = native[i];
                    input[i] = reference[i] = new LlamaParticle
                    {
                        Position = new float3(math.asfloat(fields[0].GetUInt32()), math.asfloat(fields[1].GetUInt32()), math.asfloat(fields[2].GetUInt32())),
                        Velocity = new float3(math.asfloat(fields[3].GetUInt32()), math.asfloat(fields[4].GetUInt32()), math.asfloat(fields[5].GetUInt32())),
                        Mass = math.asfloat(fields[6].GetUInt32()),
                    };
                }
            var storage = LlamaNBodyPackedStorage.FromRecords(input, Allocator.Temp);
            try
            {
                ModelScheduler.Reset(true); JobHandle handle = default;
                for (int step = 0; step < 3; step++)
                {
                    for (int i = 0; i < count; i++)
                    {
                        var r = reference[i];
                        for (int j = 0; j < count; j++) LlamaNBodyContract.Interact(ref r, reference[j]);
                        reference[i].Velocity = r.Velocity;
                    }
                    for (int i = 0; i < count; i++) reference[i].Position += reference[i].Velocity * LlamaNBodyContract.TimeStep;
                    handle = storage.ScheduleStep(7, handle); if (!chain) handle.Complete();
                }
                if (chain) handle.Complete();
                Check(ModelScheduler.Schedules == 6 && ModelScheduler.Completes == (chain ? 1 : 3), "LLAMA update/move barrier");
                storage.Export(output);
                for (int i = 0; i < count; i++)
                { Same(output[i].Position, reference[i].Position, "nbody pos"); Same(output[i].Velocity, reference[i].Velocity, "nbody vel"); Near(output[i].Mass, reference[i].Mass, "nbody mass"); }
            }
            finally { storage.Dispose(); input.Dispose(); output.Dispose(); }
        }
        bool forbidden = false;
        try { BabelStreamContract.RefusePerformanceRun(); } catch (InvalidOperationException) { forbidden = true; }
        Check(forbidden, "performance gate");
    }

    public static int Main(string[] args)
    {
        if (args.Length != 1 || args[0] != "--functional-only")
        { Console.Error.WriteLine("Refused. Only --functional-only small deterministic contracts are available; performance requires new user authorization."); return 2; }
        try
        {
            ParticleCells(); PackedMatrixCells(); Codecs(); Transforms(); ExternalContracts();
            Console.WriteLine("PASS: " + assertions + " deterministic assertions. CPU model + Unity mathematics; performance Unmeasured."); return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
