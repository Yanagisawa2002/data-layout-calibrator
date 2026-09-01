using System;
using Unity.Mathematics;

namespace Yanagisawa.DataLayoutCalibrator.Samples.TransformExport
{
    [Serializable]
    public struct TransformRecord
    {
        public float3 Position;
        public quaternion Rotation;
        public float3 Scale;
        public int EntityId;
        public int Flags;
    }

    [Serializable]
    public struct TransformExportRecord
    {
        public float4x4 LocalToWorld;
        public int EntityId;
        public int Flags;
    }
}
