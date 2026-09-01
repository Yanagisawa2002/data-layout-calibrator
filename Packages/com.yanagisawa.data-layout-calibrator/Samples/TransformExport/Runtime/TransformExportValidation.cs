using System;
using Unity.Collections;
using Unity.Mathematics;

namespace Yanagisawa.DataLayoutCalibrator.Samples.TransformExport
{
    public static class TransformExportValidation
    {
        private const float QuantizationScale = 10000f;
        private const ulong FnvOffset = 14695981039346656037ul;
        private const ulong FnvPrime = 1099511628211ul;

        public static ulong ComputeInputHash(NativeArray<TransformRecord> records)
        {
            if (!records.IsCreated)
                throw new ArgumentException("Records are not created.", nameof(records));

            ulong hash = FnvOffset;
            for (int index = 0; index < records.Length; index++)
            {
                TransformRecord record = records[index];
                hash = Add(hash, record.Position);
                hash = Add(hash, record.Rotation.value);
                hash = Add(hash, record.Scale);
                hash = Add(hash, record.EntityId);
                hash = Add(hash, record.Flags);
            }
            return hash;
        }

        public static ulong ComputeOutputHash(NativeArray<TransformExportRecord> records)
        {
            if (!records.IsCreated)
                throw new ArgumentException("Records are not created.", nameof(records));

            ulong hash = FnvOffset;
            for (int index = 0; index < records.Length; index++)
            {
                TransformExportRecord record = records[index];
                hash = Add(hash, record.LocalToWorld.c0);
                hash = Add(hash, record.LocalToWorld.c1);
                hash = Add(hash, record.LocalToWorld.c2);
                hash = Add(hash, record.LocalToWorld.c3);
                hash = Add(hash, record.EntityId);
                hash = Add(hash, record.Flags);
            }
            return hash;
        }

        public static bool ApproximatelyEqual(
            TransformExportRecord expected,
            TransformExportRecord actual,
            float tolerance,
            out string reason)
        {
            if (!Approximately(expected.LocalToWorld.c0, actual.LocalToWorld.c0, tolerance) ||
                !Approximately(expected.LocalToWorld.c1, actual.LocalToWorld.c1, tolerance) ||
                !Approximately(expected.LocalToWorld.c2, actual.LocalToWorld.c2, tolerance) ||
                !Approximately(expected.LocalToWorld.c3, actual.LocalToWorld.c3, tolerance))
            {
                reason = "LocalToWorld matrix mismatch.";
                return false;
            }

            if (expected.EntityId != actual.EntityId || expected.Flags != actual.Flags)
            {
                reason = "Entity identity or flags changed during export.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private static bool Approximately(float4 expected, float4 actual, float tolerance)
        {
            return math.all(math.abs(expected - actual) <= tolerance);
        }

        private static ulong Add(ulong hash, float3 value)
        {
            hash = Add(hash, Quantize(value.x));
            hash = Add(hash, Quantize(value.y));
            return Add(hash, Quantize(value.z));
        }

        private static ulong Add(ulong hash, float4 value)
        {
            hash = Add(hash, Quantize(value.x));
            hash = Add(hash, Quantize(value.y));
            hash = Add(hash, Quantize(value.z));
            return Add(hash, Quantize(value.w));
        }

        private static int Quantize(float value) => (int)math.round(value * QuantizationScale);

        private static ulong Add(ulong hash, int value)
        {
            unchecked
            {
                hash ^= (uint)value;
                return hash * FnvPrime;
            }
        }
    }
}
