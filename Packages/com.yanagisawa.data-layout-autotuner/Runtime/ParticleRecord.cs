using System;
using Unity.Mathematics;

namespace Yanagisawa.DataLayoutAutotuner
{
    [Serializable]
    public struct ParticleRecord
    {
        public float3 Position;
        public float3 Velocity;
        public quaternion Rotation;
        public float Lifetime;
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
