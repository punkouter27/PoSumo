using UnityEngine;

namespace PoSumo
{
    /// Physics-driven impact sound: MODAL SYNTHESIS, not recorded samples.
    ///
    /// An impact is a short force pulse exciting a few damped resonances. The
    /// three things a listener hears in a hit map onto three physical quantities
    /// the collision already gives us:
    ///
    ///  - LOUDNESS follows collision energy, E = 1/2 mu v_n^2, where mu is the
    ///    reduced mass of the two bodies (a static mat is infinitely heavy, so mu
    ///    is the part's own mass) and v_n the closing speed along the contact
    ///    normal. Speed alone ranks a fast graze above a slow chest-to-chest shove.
    ///  - BRIGHTNESS follows contact stiffness: a harder hit compresses faster, so
    ///    the force pulse is shorter (Hertzian contact) and excites higher
    ///    partials. That is why a slam is not just a louder tap.
    ///  - TIMBRE follows the MATERIAL PAIR — flesh on flesh rings low and dies at
    ///    once, flesh on clay is a dull thump with a gritty tail, a knock on the
    ///    straw tawara is higher and drier, a foot on clay is small and sandy.
    ///
    /// Synthesising per collision at runtime would allocate an AudioClip per hit,
    /// so the bank is rendered ONCE — material x energy bucket x variant, 48 clips,
    /// ~0.6 MB of float samples, ~40 ms of CPU — and every hit picks its clip by
    /// energy and material and is pitched by the struck part's mass. The bank is
    /// static and survives scene loads; it is cleared on SubsystemRegistration
    /// because runtime AudioClips are destroyed when Play mode exits and domain
    /// reload is off in this project.
    ///
    /// Presentation only: reads collisions, touches no physics state, so no brain
    /// is affected. Main thread only (AudioClip.Create).
    public static class Systems_ImpactSynth
    {
        public enum Material
        {
            FleshOnFlesh = 0,
            FleshOnClay = 1,
            FleshOnStraw = 2,
            FootOnClay = 3,
        }

        public const int MATERIAL_COUNT = 4;
        public const int ENERGY_BUCKETS = 4;
        public const int VARIANTS = 3;

        private const int SAMPLE_RATE = 22050;
        private const float CLIP_SECONDS = 0.6f;

        /// Energy (J) mapped onto 0..1 on a LOG scale — perceived loudness is
        /// logarithmic, and the range is wide: a forearm brushing a forearm is
        /// ~0.5 J, a 13 kg chest landing on clay at 6 m/s is ~230 J.
        private const float ENERGY_MIN = 0.4f;
        private const float ENERGY_MAX = 250f;

        private static AudioClip[] _bank;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlaySessionStart() => _bank = null;

        public static bool Ready => _bank != null;

        /// Renders the bank if it does not exist yet. Call from a scene-load
        /// path (Awake), never from a collision callback.
        public static void EnsureBank()
        {
            if (_bank != null)
            {
                return;
            }
            var bank = new AudioClip[MATERIAL_COUNT * ENERGY_BUCKETS * VARIANTS];
            int samples = Mathf.RoundToInt(CLIP_SECONDS * SAMPLE_RATE);
            var buffer = new float[samples];
            for (int materialIndex = 0; materialIndex < MATERIAL_COUNT; materialIndex++)
            {
                for (int bucketIndex = 0; bucketIndex < ENERGY_BUCKETS; bucketIndex++)
                {
                    for (int variantIndex = 0; variantIndex < VARIANTS; variantIndex++)
                    {
                        float energy01 = (bucketIndex + 0.5f) / ENERGY_BUCKETS;
                        int seed = 9173 + materialIndex * 131 + bucketIndex * 17 + variantIndex;
                        Render((Material)materialIndex, energy01, seed, buffer);
                        AudioClip clip = AudioClip.Create(
                            $"ImpactSynth_{(Material)materialIndex}_{bucketIndex}_{variantIndex}",
                            samples, 1, SAMPLE_RATE, false);
                        clip.SetData(buffer, 0);
                        bank[Index(materialIndex, bucketIndex, variantIndex)] = clip;
                    }
                }
            }
            _bank = bank;
        }

        private static int Index(int material, int bucket, int variant) =>
            (material * ENERGY_BUCKETS + bucket) * VARIANTS + variant;

        /// Kinetic energy dissipated along the contact normal, in joules.
        public static float CollisionEnergy(Collision2D collision)
        {
            Rigidbody2D self = collision.otherRigidbody;
            Rigidbody2D other = collision.rigidbody;
            float selfMass = self != null ? self.mass : 1f;
            float reducedMass = selfMass;
            if (other != null && other.bodyType == RigidbodyType2D.Dynamic)
            {
                reducedMass = selfMass * other.mass / Mathf.Max(0.001f, selfMass + other.mass);
            }
            float closing = collision.contactCount > 0
                ? Mathf.Abs(Vector2.Dot(collision.relativeVelocity, collision.GetContact(0).normal))
                : collision.relativeVelocity.magnitude;
            return 0.5f * reducedMass * closing * closing;
        }

        /// 0..1 on the log energy scale.
        public static float Energy01(float joules) =>
            Mathf.Clamp01(Mathf.InverseLerp(Mathf.Log(ENERGY_MIN), Mathf.Log(ENERGY_MAX),
                                            Mathf.Log(Mathf.Max(1e-4f, joules))));

        /// Bigger resonator, lower pitch: a 13 kg chest sounds lower than a 1 kg
        /// forearm. Gentle exponent — this is a body, not a tuned bell.
        public static float PitchForMass(float partMass) =>
            Mathf.Clamp(Mathf.Pow(4f / Mathf.Max(0.3f, partMass), 0.12f), 0.82f, 1.22f);

        public static AudioClip Pick(Material material, float energy01, int variant)
        {
            if (_bank == null)
            {
                return null;
            }
            int bucket = Mathf.Clamp((int)(energy01 * ENERGY_BUCKETS), 0, ENERGY_BUCKETS - 1);
            return _bank[Index((int)material, bucket, ((variant % VARIANTS) + VARIANTS) % VARIANTS)];
        }

        // ---------------------------------------------------------- synthesis

        /// One resonance: frequency, decay time to -60 dB, relative gain.
        private struct Mode
        {
            public float Hz, T60, Gain;
            public Mode(float hz, float t60, float gain) { Hz = hz; T60 = t60; Gain = gain; }
        }

        private struct Recipe
        {
            public Mode[] Modes;
            public float NoiseGain;       // grit / slap component
            public float NoiseDecay;      // seconds
            public float NoiseCutoffLow;  // one-pole LP cutoff at energy 0
            public float NoiseCutoffHigh; // ... at energy 1
            public float Crackle;         // 0..1 sparse impulses (straw fibres)
            public float PulseSlowMs;     // contact duration at energy 0
            public float PulseFastMs;     // ... at energy 1
            public float Glide;           // fractional pitch drop over the attack
        }

        private static Recipe RecipeFor(Material material)
        {
            switch (material)
            {
                case Material.FleshOnFlesh:
                    // Torso cavity + soft tissue: low, heavily damped, with a
                    // bright slap riding the attack.
                    return new Recipe
                    {
                        Modes = new[] { new Mode(78f, 0.11f, 1f), new Mode(141f, 0.07f, 0.55f),
                                        new Mode(233f, 0.045f, 0.3f), new Mode(410f, 0.025f, 0.15f) },
                        NoiseGain = 0.55f, NoiseDecay = 0.025f,
                        NoiseCutoffLow = 1800f, NoiseCutoffHigh = 7000f,
                        Crackle = 0f, PulseSlowMs = 9f, PulseFastMs = 2.5f, Glide = 0.12f,
                    };
                case Material.FleshOnClay:
                    // Packed earth: the deepest, deadest thump plus a long sandy tail.
                    return new Recipe
                    {
                        Modes = new[] { new Mode(52f, 0.16f, 1f), new Mode(96f, 0.09f, 0.6f),
                                        new Mode(170f, 0.05f, 0.25f) },
                        NoiseGain = 0.4f, NoiseDecay = 0.16f,
                        NoiseCutoffLow = 900f, NoiseCutoffHigh = 3200f,
                        Crackle = 0.15f, PulseSlowMs = 12f, PulseFastMs = 4f, Glide = 0.06f,
                    };
                case Material.FleshOnStraw:
                    // Rice-straw bales: a smaller, stiffer resonator, dry, crackly.
                    return new Recipe
                    {
                        Modes = new[] { new Mode(190f, 0.05f, 1f), new Mode(330f, 0.035f, 0.7f),
                                        new Mode(560f, 0.025f, 0.45f), new Mode(940f, 0.015f, 0.25f) },
                        NoiseGain = 0.5f, NoiseDecay = 0.05f,
                        NoiseCutoffLow = 2500f, NoiseCutoffHigh = 8000f,
                        Crackle = 0.8f, PulseSlowMs = 5f, PulseFastMs = 1.6f, Glide = 0.02f,
                    };
                default:
                    // Foot on clay: small and gritty, mostly the sand.
                    return new Recipe
                    {
                        Modes = new[] { new Mode(88f, 0.06f, 1f), new Mode(160f, 0.04f, 0.4f) },
                        NoiseGain = 0.7f, NoiseDecay = 0.09f,
                        NoiseCutoffLow = 1200f, NoiseCutoffHigh = 4500f,
                        Crackle = 0.35f, PulseSlowMs = 10f, PulseFastMs = 3.5f, Glide = 0.03f,
                    };
            }
        }

        private static void Render(Material material, float energy01, int seed, float[] output)
        {
            var rng = new System.Random(seed);
            Recipe recipe = RecipeFor(material);
            int length = output.Length;
            System.Array.Clear(output, 0, length);

            // The excitation: a raised-cosine force pulse. Shorter = brighter.
            float pulseSeconds = Mathf.Lerp(recipe.PulseSlowMs, recipe.PulseFastMs, energy01) * 0.001f;
            int pulseSamples = Mathf.Max(2, Mathf.RoundToInt(pulseSeconds * SAMPLE_RATE));

            // Per-variant detune so three hits at one energy are three hits.
            float detune = 1f + ((float)rng.NextDouble() - 0.5f) * 0.08f;

            // Modes: two-pole resonators driven by the pulse. The coefficients are
            // refreshed every 16 samples so the "tension glide" — the pitch drop a
            // hard hit has while the tissue is still compressed — costs ~1/16 of
            // a cos per sample rather than one.
            for (int modeIndex = 0; modeIndex < recipe.Modes.Length; modeIndex++)
            {
                Mode mode = recipe.Modes[modeIndex];
                float r = Mathf.Exp(-6.9078f / (mode.T60 * SAMPLE_RATE));
                float y1 = 0f, y2 = 0f;
                float twoRCos = 0f;
                // A two-pole resonator's gain grows as 1 / ((1 - r) sin theta), so
                // without this the lowest, longest mode buries every other one.
                float theta0 = 2f * Mathf.PI * mode.Hz * detune / SAMPLE_RATE;
                float norm = (1f - r) * Mathf.Sin(theta0) * 4f;
                float gain = mode.Gain * norm * (1f + energy01 * 0.4f * modeIndex);   // stiffer -> more upper modes
                for (int sampleIndex = 0; sampleIndex < length; sampleIndex++)
                {
                    if ((sampleIndex & 15) == 0)
                    {
                        float t = sampleIndex / (float)SAMPLE_RATE;
                        float glide = 1f + recipe.Glide * energy01 * Mathf.Exp(-t / 0.03f);
                        float theta = 2f * Mathf.PI * mode.Hz * detune * glide / SAMPLE_RATE;
                        twoRCos = 2f * r * Mathf.Cos(theta);
                    }
                    float x = 0f;
                    if (sampleIndex < pulseSamples)
                    {
                        x = 0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * sampleIndex / pulseSamples);
                    }
                    float y = twoRCos * y1 - r * r * y2 + x;
                    y2 = y1;
                    y1 = y;
                    output[sampleIndex] += y * gain;
                }
            }

            // Normalise the modal part before adding noise, so the recipe gains
            // above mean the same thing for every material.
            Normalise(output, 0.8f);

            // Noise: slap / grit. One-pole low-pass, cutoff rising with energy.
            float cutoff = Mathf.Lerp(recipe.NoiseCutoffLow, recipe.NoiseCutoffHigh, energy01);
            float alpha = 1f - Mathf.Exp(-2f * Mathf.PI * cutoff / SAMPLE_RATE);
            float lp = 0f;
            float noiseDecay = recipe.NoiseDecay * Mathf.Lerp(0.8f, 1.4f, energy01);
            float crackleChance = recipe.Crackle * 0.004f;
            for (int sampleIndex = 0; sampleIndex < length; sampleIndex++)
            {
                float t = sampleIndex / (float)SAMPLE_RATE;
                float white = (float)rng.NextDouble() * 2f - 1f;
                if (crackleChance > 0f && rng.NextDouble() < crackleChance)
                {
                    white *= 6f;   // a fibre snapping
                }
                lp += alpha * (white - lp);
                float attack = Mathf.Clamp01(sampleIndex / (float)pulseSamples);
                output[sampleIndex] += lp * recipe.NoiseGain * attack * Mathf.Exp(-t / noiseDecay);
            }

            // A slam overdrives the tissue: gentle saturation at the top bucket.
            float drive = 1f + energy01 * energy01 * 1.5f;
            for (int sampleIndex = 0; sampleIndex < length; sampleIndex++)
            {
                output[sampleIndex] = Tanh(output[sampleIndex] * drive);
            }

            // Click-free tail.
            int fade = SAMPLE_RATE / 50;
            for (int fadeIndex = 0; fadeIndex < fade; fadeIndex++)
            {
                output[length - 1 - fadeIndex] *= fadeIndex / (float)fade;
            }
            Normalise(output, 0.9f);
        }

        private static float Tanh(float value)
        {
            float e = Mathf.Exp(2f * Mathf.Clamp(value, -9f, 9f));
            return (e - 1f) / (e + 1f);
        }

        private static void Normalise(float[] samples, float peak)
        {
            float max = 0f;
            for (int sampleIndex = 0; sampleIndex < samples.Length; sampleIndex++)
            {
                max = Mathf.Max(max, Mathf.Abs(samples[sampleIndex]));
            }
            if (max < 1e-6f)
            {
                return;
            }
            float scale = peak / max;
            for (int sampleIndex = 0; sampleIndex < samples.Length; sampleIndex++)
            {
                samples[sampleIndex] *= scale;
            }
        }
    }
}
