using UnityEngine;

namespace Lattice.Audio
{
    /// <summary>
    /// The mix's last stage, on the listener (it processes everything heard):
    /// a gentle compressor that holds a big fight's loudness in check, then a
    /// limiter so nothing clips however many rifles fire at once. Runs on the
    /// audio thread; allocates nothing.
    /// </summary>
    [RequireComponent(typeof(AudioListener))]
    public sealed class MasterBus : MonoBehaviour
    {
        /// <summary>Compression above this level (dBFS), at this ratio.</summary>
        public float Threshold = -16f, Ratio = 3f;
        /// <summary>The limiter's ceiling (dBFS): peaks never pass it.</summary>
        public float Ceiling = -1f;
        public float AttackMs = 4f, ReleaseMs = 160f, LimitReleaseMs = 60f;

        float env, limitEnv, gain = 1f;
        float attack, release, limitRelease, ceiling;
        int rate;

        /// <summary>The most gain taken off since last read (dB, positive), for the report.</summary>
        public float MaxReductionDb { get; private set; }
        public float ReadAndResetReduction()
        {
            float r = MaxReductionDb;
            MaxReductionDb = 0f;
            return r;
        }

        void OnEnable()
        {
            rate = AudioSettings.outputSampleRate;
            Prepare();
        }

        void Prepare()
        {
            float sr = Mathf.Max(rate, 8000);
            attack = 1f - Mathf.Exp(-1f / (AttackMs * 0.001f * sr));
            release = 1f - Mathf.Exp(-1f / (ReleaseMs * 0.001f * sr));
            limitRelease = 1f - Mathf.Exp(-1f / (LimitReleaseMs * 0.001f * sr));
            ceiling = Mathf.Pow(10f, Ceiling / 20f);
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            if (channels <= 0)
                return;
            float thr = Threshold, slope = 1f - 1f / Mathf.Max(Ratio, 1f);
            float maxRed = 0f;
            for (int i = 0; i < data.Length; i += channels)
            {
                float peak = 0f;
                for (int c = 0; c < channels; c++)
                {
                    float a = data[i + c];
                    if (a < 0f) a = -a;
                    if (a > peak) peak = a;
                }
                // Compressor: a level follower, gain from how far over the threshold it is.
                env += (peak - env) * (peak > env ? attack : release);
                float levelDb = 20f * Mathf.Log10(env + 1e-9f);
                float over = levelDb - thr;
                float target = over > 0f ? Mathf.Pow(10f, -over * slope / 20f) : 1f;
                // Limiter: instant on the way down, released gently.
                float after = peak * target;
                if (after > limitEnv)
                    limitEnv = after;
                else
                    limitEnv += (after - limitEnv) * limitRelease;
                if (limitEnv > ceiling)
                    target *= ceiling / limitEnv;
                gain += (target - gain) * (target < gain ? 0.5f : release);
                float red = -20f * Mathf.Log10(gain + 1e-9f);
                if (red > maxRed) maxRed = red;
                for (int c = 0; c < channels; c++)
                {
                    float x = data[i + c] * gain;
                    // Whatever still pokes over the ceiling is rounded off, never cut flat.
                    if (x > ceiling) x = ceiling + (1f - ceiling) * Tanh((x - ceiling) / (1f - ceiling));
                    else if (x < -ceiling) x = -ceiling - (1f - ceiling) * Tanh((-x - ceiling) / (1f - ceiling));
                    data[i + c] = x;
                }
            }
            if (maxRed > MaxReductionDb)
                MaxReductionDb = maxRed;
        }

        static float Tanh(float x)
        {
            float e = Mathf.Exp(-2f * x);
            return (1f - e) / (1f + e);
        }
    }
}
