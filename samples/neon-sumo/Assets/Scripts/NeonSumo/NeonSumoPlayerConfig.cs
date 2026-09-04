using UnityEngine;

namespace NeonSumo
{
    /// <summary>
    /// Player lifecycle phase. Replaces scattered booleans for clearer state reasoning.
    /// </summary>
    public enum NeonSumoPlayerPhase
    {
        Spawning,
        RollingIn,
        Active,
        KnockbackLocked,
        Eliminated
    }

    /// <summary>
    /// Authority/simulation mode for network sync.
    /// </summary>
    public enum PlayerSimulationMode
    {
        HostSimulated,
        RemoteInterpolated
    }

    /// <summary>
    /// Typed snapshot for remote player state. Convert from network payload at the boundary.
    /// </summary>
    public struct PlayerStateSnapshot
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Velocity;
    }
    [System.Serializable]
    public class NeonSumoMovementConfig
    {
        public float moveSpeed = 20f;
        [Tooltip("Speed cap while active (after ramp). Raise to preserve ramp exit momentum.")]
        public float maxSpeed = 40f;
        public float boostForce = 12f;
        public float boostCooldown = 2.5f;
        [Tooltip("Minimum force factor when pushing against current velocity.")]
        [Range(0.1f, 1f)]
        public float turnResistanceMin = 0.4f;
    }

    [System.Serializable]
    public class NeonSumoPhysicsConfig
    {
        public float tunedMass = 1.6f;
        [Tooltip("Lower = more coasting after ramp / less artificial drag.")]
        public float tunedLinearDamping = 0.22f;
        public float tunedAngularDamping = 1.85f;
        public PhysicsMaterial playerPhysicsMaterial;
    }

    [System.Serializable]
    public class NeonSumoRampConfig
    {
        public float rampLinearDamping = 0.02f;
        public float rampAngularDamping = 0.05f;
        [Tooltip("One-shot push when roll-in starts (local forward + world down).")]
        public float rampImpulse = 28f;
        [Tooltip("Acceleration along the same axis each physics step while rolling in (host only). 0 = off.")]
        public float rampRollAcceleration = 22f;
    }

    [System.Serializable]
    public class NeonSumoCollisionConfig
    {
        public float bumpMultiplier = 1.6f;
        public float maxBumpForce = 18f;
        public float maxRelativeSpeed = 12f;
        public float baseImpactForce = 4f;
        public float bonusImpactForce = 9f;
        public float minSpeedForBonus = 2f;
        public float maxSpeedForBonus = 7f;
        public float verticalBumpForce = 1.1f;
        public float maxKnockbackSpeed = 13f;
        public float knockbackDamping = 0.15f;
        public float knockbackDampingDuration = 0.15f;
        public float knockbackLockoutDuration = 0.2f;
    }

    [System.Serializable]
    public class NeonSumoHitPauseConfig
    {
        public float hitPauseDuration = 0.04f;
    }

    [System.Serializable]
    public class NeonSumoEdgeConfig
    {
        [Tooltip("How close to the edge before slip starts (world units).")]
        public float edgeSlipStartOffset = 1.5f;
        public float edgeSlipForce = 3.2f;
    }
}
