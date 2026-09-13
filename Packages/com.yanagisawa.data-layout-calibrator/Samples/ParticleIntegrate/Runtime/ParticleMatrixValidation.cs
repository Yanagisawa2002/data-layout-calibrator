using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace Yanagisawa.DataLayoutCalibrator.Samples.ParticleIntegrate
{
    /// <summary>Actual candidate correctness only. No timings or winner claims.</summary>
    public static class ParticleMatrixValidation
    {
        public static int RunOrThrow()
        {
            var definitions = ParticleCandidateMatrix.CreateCandidates();
            if (definitions.Length != 120) throw new InvalidOperationException("Unexpected matrix size.");
            if (UnsafeUtility.SizeOf<ParticleRecord>() != 48 ||
                UnsafeUtility.SizeOf<ParticleRecordGeneratedPadded64Record>() != 64)
                throw new InvalidOperationException("Padding metadata does not match native stride.");
            int checks = 0;
            foreach (int count in new[] { 0, 1, 3, 4, 7, 8, 15, 16, 17, 257 })
            {
                // The calibration engine requires N>0; empty storage is a lifetime/codec check.
                if (count == 0)
                {
                    using (var empty = new NativeArray<ParticleRecord>(0, Allocator.Persistent))
                    using (var destination = new NativeArray<ParticleRecord>(0, Allocator.Persistent))
                    foreach (LayoutKind layout in new[] { LayoutKind.AoS, LayoutKind.SoA, LayoutKind.AoSoA4,
                                 LayoutKind.AoSoA8, LayoutKind.AoSoA16, LayoutKind.AoSPadded64 })
                    using (var domain = ParticleLayoutDomain.Create(layout, 64, empty))
                    {
                        domain.Ingress(empty);
                        domain.Export(destination);
                        if (domain.Count != 0) throw new InvalidOperationException("Empty count changed.");
                        checks++;
                    }
                    continue;
                }
                using (var source = ParticleDataSet.Create(count, ParticleDataSet.CalibrationSeed, Allocator.Persistent))
                using (var oracle = new NativeArray<ParticleRecord>(source, Allocator.Persistent))
                using (var scenario = new ParticleIntegrateScenarioFactory().Create(count, ParticleDataSet.CalibrationSeed, definitions))
                {
                    var writableOracle = oracle;
                    const int ticks = 97;
                    const float dt = 1f / 60f;
                    for (int tick = 0; tick < ticks; tick++)
                    for (int index = 0; index < count; index++)
                    {
                        var r = oracle[index];
                        r.Velocity = r.Velocity * ParticleStepContract.VelocityDamping +
                            new float3(ParticleStepContract.AccelerationX, ParticleStepContract.AccelerationY, ParticleStepContract.AccelerationZ) * dt;
                        r.Position += r.Velocity * dt;
                        r.Lifetime -= dt;
                        if (r.Lifetime <= 0) { r.Lifetime += ParticleStepContract.RespawnLifetimeSeconds; r.Position *= ParticleStepContract.RespawnPositionScale; }
                        writableOracle[index] = r;
                    }
                    foreach (int reset in new[] { 0, 1 })
                    for (int index = 0; index < scenario.CandidateCount; index++)
                    {
                        var candidate = (ParticleIntegrateCandidate)scenario.GetCandidate(index);
                        candidate.Ingress();
                        candidate.Execute(ticks, dt);
                        candidate.Export();
                        for (int record = 0; record < count; record++)
                            if (!ParticleStateValidation.ApproximatelyEqual(oracle[record], candidate.CanonicalExport[record], 1e-5f, out string reason))
                                throw new InvalidOperationException(candidate.Descriptor.CandidateId + ": " + reason);
                        var parity = scenario.ParityValidator.Validate(scenario.GetCandidate(scenario.ReferenceCandidateIndex), candidate, 1e-5f);
                        if (!parity.Passed) throw new InvalidOperationException(candidate.Descriptor.CandidateId + ": " + parity.Reason);
                        checks++;
                    }
                }
            }
            return checks;
        }
    }
}
