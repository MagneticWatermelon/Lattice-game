using UnityEngine;

namespace Lattice.Players
{
    /// <summary>
    /// How a soldier on foot steps at a speed: one curve for the legs, the
    /// first-person view's bob and every footstep heard, so they keep time.
    /// The clips take short steps (1.66 m running and sprinting), so to keep the
    /// feet planted they'd play fast: 5.4 steps a second at a sprint, a
    /// cartoon. Past a walk the stride stretches instead (<see cref="Stride"/>:
    /// the clip plays slower and the feet reach further along, by IK), down to a
    /// sprinter's cadence.
    /// </summary>
    public static class Gait
    {
        /// <summary>Steps a second at <paramref name="speed"/> (m/s): a walk's 1.6 to a
        /// run's 2.95 at 6 m/s and a sprint's 3.7 at 9.</summary>
        public static float Cadence(float speed)
        {
            if (speed <= 3f)
                return Mathf.Lerp(1.6f, 2.6f, speed / 3f);
            if (speed <= 6f)
                return Mathf.Lerp(2.6f, 2.95f, (speed - 3f) / 3f);
            return Mathf.Lerp(2.95f, 3.7f, Mathf.Clamp01((speed - 6f) / 3f));
        }

        /// <summary>How much the stride stretches at a sprint, over the clips' (tuning).</summary>
        public static float StrideGain = 0.45f;

        /// <summary>How much longer than the clips' the stride is at <paramref name="speed"/>
        /// (1 up to a walk; 1 + <see cref="StrideGain"/> at a sprint): the clips play this much slower.</summary>
        public static float Stride(float speed)
        {
            float x = Mathf.Clamp01((speed - 3f) / 6f);
            return 1f + StrideGain * x * x * (3f - 2f * x);
        }

        /// <summary>Our own steps (first person: the bob and our footsteps), counted in
        /// steps: a footfall as it passes a whole number. Advanced once a frame by
        /// whoever asks first.</summary>
        public static float OwnPhase { get; private set; }
        static int ownFrame = -1;

        public static float AdvanceOwn(float speed, bool grounded, float dt)
        {
            if (ownFrame == Time.frameCount)
                return OwnPhase;
            ownFrame = Time.frameCount;
            // (Standing or in the air, the step waits where it was.)
            if (grounded && speed > 0.8f)
                OwnPhase += Cadence(speed) * dt;
            return OwnPhase;
        }
    }
}
