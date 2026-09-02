using System;
using Unity.Mathematics;
using Yanagisawa.DataLayoutCalibrator;

namespace Yanagisawa.DataLayoutCalibrator.Samples.ParticleIntegrate
{
    [Serializable]
    [GenerateDataLayout(
        "particle-record",
        1,
        8,
        MinimumCompatibleSchemaVersion = 1,
        DefinitionVersion = 1)]
    public struct ParticleRecord
    {
        [DataLayoutField(0, DataLayoutFieldTemperature.Hot)]
        public float3 Position;

        [DataLayoutField(1, DataLayoutFieldTemperature.Hot)]
        public float3 Velocity;

        [DataLayoutField(2, DataLayoutFieldTemperature.Cold)]
        public quaternion Rotation;

        [DataLayoutField(3, DataLayoutFieldTemperature.Hot)]
        public float Lifetime;

        [DataLayoutField(4, DataLayoutFieldTemperature.Cold)]
        public int Category;
    }

    public static class ParticleStepContract
    {
        public const float RespawnLifetimeSeconds = 10.0f;
        public const float RespawnPositionScale = -0.25f;

        public const float AccelerationX = 0.1375f;
        public const float AccelerationY = -0.24375f;
        public const float AccelerationZ = 0.08125f;

        public const float VelocityDamping = 0.99925f;
    }
}
