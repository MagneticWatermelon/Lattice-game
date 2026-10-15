using UnityEngine;

namespace Lattice.Players
{
    /// <summary>
    /// How a soldier on foot steps. Our own steps (first person: the bob and our
    /// footsteps) keep <see cref="Cadence"/>, a runner's. Soldiers' legs are their
    /// clips': those take short steps (1.66 m running and sprinting), so a sprint
    /// played to keep its feet planted ran at 5.4 steps a second, a cartoon; past a
    /// run the stride stretches (<see cref="Stride"/>: the clip plays slower and the
    /// feet reach further along, by IK). Their footsteps follow their legs
    /// (<see cref="SoldierStep"/>).
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

        /// <summary>How much the stride stretches at a full sprint, over the clips' (tuning).</summary>
        public static float StrideGain = 0.25f;

        /// <summary>How much longer than the clips' a soldier's stride is at <paramref name="speed"/>:
        /// 1 up to a run (the run clip at its own pace, feet planted: slowed, it floated, a
        /// leg hung behind for a quarter second), stretching past it to 1 +
        /// <see cref="StrideGain"/> at a sprint (5.4 steps a second played straight).</summary>
        public static float Stride(float speed)
        {
            float x = Mathf.Clamp01((speed - 6.5f) / 2.5f);
            return 1f + StrideGain * x * x * (3f - 2f * x);
        }

        /// <summary>A step's length (m) as soldiers' legs take them at <paramref name="speed"/>:
        /// for their footsteps, which must fall with their feet. The clips step 1.66 m
        /// running and sprinting (stretched past a run), less walking.</summary>
        public static float SoldierStep(float speed) =>
            speed >= 4.5f ? 1.66f * Stride(speed) : Mathf.Lerp(0.9f, 1.5f, Mathf.Clamp01((speed - 1f) / 3.5f));

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
