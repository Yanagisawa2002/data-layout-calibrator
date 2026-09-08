using System;
using Unity.Mathematics;
using Yanagisawa.DataLayoutCalibrator;

namespace Yanagisawa.DataLayoutCalibrator.Samples.TransformExport
{
    [Serializable]
    [GenerateDataLayout(
        "transform-record",
        1,
        4,
        MinimumCompatibleSchemaVersion = 1,
        DefinitionVersion = 1)]
    public struct TransformRecord
    {
        [DataLayoutField(0, DataLayoutFieldTemperature.Hot)]
        public float3 Position;

        [DataLayoutField(1, DataLayoutFieldTemperature.Hot)]
        public quaternion Rotation;

        [DataLayoutField(2, DataLayoutFieldTemperature.Hot)]
        public float3 Scale;

        [DataLayoutField(3, DataLayoutFieldTemperature.Hot)]
        public int EntityId;

        [DataLayoutField(4, DataLayoutFieldTemperature.Hot)]
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
