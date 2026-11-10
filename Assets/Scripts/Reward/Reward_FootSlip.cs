using UnityEngine;

namespace PoSumo
{
    /// How much a LOADED foot is sliding along the mat, in m/s weighted by the
    /// share of body weight that foot carries.
    ///
    /// The skate test the project already had (`Reward_StepCadence`) only uses
    /// foot speed to decide whether a foot COUNTS as planted; nothing was ever
    /// charged for a planted foot sliding under the body. Weighting by load is the
    /// whole point: a swing foot carries no weight and is free to move fast, which
    /// is correct gait, while a foot carrying the fighter and moving is a skate.
    ///
    /// Pure, so it is unit-testable: the two foot readings in, a number out. Load
    /// is `Agent_BipedBody.FootLoadNear/Far` (0..1, already sampled every physics
    /// step from the contact normal impulses) and is 0 for a foot not in contact,
    /// so an airborne foot can never be charged.
    public static class Reward_FootSlip
    {
        /// Above this load share a foot is treated as fully loaded. A fighter on
        /// two feet carries ~0.5 per foot, so 0.5 makes a normal two-footed stance
        /// count fully on both.
        private const float FULL_LOAD = 0.5f;

        public static float Measure(float nearLoad, float nearVx, float farLoad, float farVx)
        {
            return One(nearLoad, nearVx) + One(farLoad, farVx);
        }

        public static float Measure(Agent_BipedBody body)
        {
            if (body == null || body.Parts == null) return 0f;
            return Measure(body.FootDownNear ? body.FootLoadNear : 0f, body.FootNear.linearVelocity.x,
                           body.FootDownFar ? body.FootLoadFar : 0f, body.FootFar.linearVelocity.x);
        }

        private static float One(float load, float vx)
        {
            float weight = Mathf.Clamp01(Reward_Context.San(load) / FULL_LOAD);
            return weight * Mathf.Abs(Reward_Context.San(vx));
        }
    }
}
