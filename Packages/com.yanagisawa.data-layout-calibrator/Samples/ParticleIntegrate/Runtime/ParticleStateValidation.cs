using System;
using Unity.Collections;
using Unity.Mathematics;

namespace Yanagisawa.DataLayoutCalibrator.Samples.ParticleIntegrate
{
    public static class ParticleStateValidation
    {
        private const float QuantizationScale = 10000.0f;
        private const ulong FnvOffset = 14695981039346656037ul;
        private const ulong FnvPrime = 1099511628211ul;

        public static ulong ComputeHash(ref ParticleAoSStorage storage) =>
            ComputeHash(storage.Count, storage.ReadRecord);

        public static ulong ComputeHash(ref ParticleSoAStorage storage) =>
            ComputeHash(storage.Count, storage.ReadRecord);

        public static ulong ComputeHash(ref ParticleAoSoA8Storage storage) =>
            ComputeHash(storage.Count, storage.ReadRecord);

        public static ulong ComputeHash(NativeArray<ParticleRecord> records)
        {
            if (!records.IsCreated)
                throw new ArgumentException("Records are not created.", nameof(records));

            ulong hash = FnvOffset;
            for (int index = 0; index < records.Length; index++)
            {
                ParticleRecord record = records[index];
                hash = AddRecord(hash, record);
            }

            return hash;
        }

        public static bool ApproximatelyEqual(
            ParticleRecord left,
            ParticleRecord right,
            float tolerance,
            out string failure)
        {
            if (!Approximately(left.Position, right.Position, tolerance))
            {
                failure = $"Position mismatch: {left.Position} vs {right.Position}.";
                return false;
            }
            if (!Approximately(left.Velocity, right.Velocity, tolerance))
            {
                failure = $"Velocity mismatch: {left.Velocity} vs {right.Velocity}.";
                return false;
            }
            if (math.abs(left.Lifetime - right.Lifetime) > tolerance)
            {
                failure = $"Lifetime mismatch: {left.Lifetime} vs {right.Lifetime}.";
                return false;
            }
            if (!left.Rotation.value.Equals(right.Rotation.value))
            {
                failure = "Cold rotation data changed.";
                return false;
            }
            if (left.Category != right.Category)
            {
                failure = "Cold category data changed.";
                return false;
            }

            failure = string.Empty;
            return true;
        }

        private static ulong ComputeHash(
            int count,
            Func<int, ParticleRecord> read)
        {
            ulong hash = FnvOffset;
            for (int index = 0; index < count; index++)
            {
                ParticleRecord record = read(index);
                hash = AddRecord(hash, record);
            }
            return hash;
        }

        private static ulong AddRecord(ulong hash, ParticleRecord record)
        {
            hash = Add(hash, Quantize(record.Position.x));
            hash = Add(hash, Quantize(record.Position.y));
            hash = Add(hash, Quantize(record.Position.z));
            hash = Add(hash, Quantize(record.Velocity.x));
            hash = Add(hash, Quantize(record.Velocity.y));
            hash = Add(hash, Quantize(record.Velocity.z));
            hash = Add(hash, Quantize(record.Lifetime));
            return Add(hash, record.Category);
        }

        private static int Quantize(float value) =>
            (int)math.round(value * QuantizationScale);

        private static ulong Add(ulong hash, int value)
        {
            unchecked
            {
                hash ^= (uint)value;
                return hash * FnvPrime;
            }
        }

        private static bool Approximately(float3 left, float3 right, float tolerance) =>
            math.all(math.abs(left - right) <= tolerance);
    }
}
