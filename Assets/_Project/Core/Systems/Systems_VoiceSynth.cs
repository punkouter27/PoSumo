using System;
using UnityEngine;

namespace PoSumo
{
    /// Synthesises a PLACEHOLDER voice line at runtime: one wordless vocalisation —
    /// a cheer, a roar, a pained grunt, a jeer — for any (fighter, mood, level,
    /// variant) that has no recorded WAV.
    ///
    /// It is runtime rather than an Editor bake on purpose. Since 2026-11-10 every
    /// fighter carries 4 moods x 5 levels x 5 variants = 100 lines, and there are
    /// 16 fighters: 1 600 WAVs, ~70 MB, shipped in Resources to an Android phone
    /// and a WebGL page, for sounds that are placeholders by definition. Generated
    /// on demand instead, a bout synthesises only the handful of lines its two
    /// fighters actually say (~1 ms each), and a real recording dropped at
    /// `Resources/Audio/Voice/<Name>_<Mood>_L<level>_<variant>.wav` simply wins.
    ///
    /// The synthesis is the formant model the old Editor tool
    /// (GeneratePlaceholderVoices, retired the same day) used for Happy / Sad /
    /// Insult, extended to Angry and Hurt and given per-VARIANT structure: a
    /// variant changes syllable count, contour, vowel and length, so five lines at
    /// one level are five different utterances rather than one sound re-pitched.
    ///
    /// Intensity rises with level in three ways at once — pitch up, vowel opening
    /// toward /a/, sharper attack — because loudness alone reads as a volume knob,
    /// not as effort.
    ///
    /// Pure and deterministic: same inputs, same samples. Main thread only
    /// (AudioClip.Create).
    public static class Systems_VoiceSynth
    {
        public const int SAMPLE_RATE = 22050;

        /// A fighter's vocal identity: fundamental, tract length, and a seed so two
        /// fighters with the same pitch still say different things.
        public readonly struct Voice
        {
            public readonly float F0;
            public readonly float Tract;
            public readonly int Seed;

            public Voice(float f0, float tract, int seed)
            {
                F0 = f0;
                Tract = tract;
                Seed = seed;
            }
        }

        /// The voice for a character asset. An unset pitch is derived from the
        /// behavior name, so every fighter sounds distinct and stable across runs.
        public static Voice VoiceFor(Agent_CharacterDefinition character, string fallbackName)
        {
            string name = character != null ? character.behaviorName : fallbackName;
            int seed = StableHash(name);
            float f0 = character != null && character.voicePitchHz > 0f
                ? character.voicePitchHz
                : 92f + (seed & 0x3F);                 // 92..155 Hz
            float tract = character != null && character.voiceTract > 0f ? character.voiceTract : 1f;
            return new Voice(f0, tract, seed);
        }

        /// FNV-1a. `string.GetHashCode` is not guaranteed stable across runtimes.
        public static int StableHash(string text)
        {
            unchecked
            {
                uint hash = 2166136261;
                if (text != null)
                {
                    for (int index = 0; index < text.Length; index++)
                    {
                        hash ^= text[index];
                        hash *= 16777619;
                    }
                }
                return (int)(hash & 0x7FFFFFFF);
            }
        }

        public static AudioClip Create(string clipName, Voice voice, Systems_FighterMood mood, int level, int variant)
        {
            float[] samples = Synthesize(voice, mood, level, variant);
            AudioClip clip = AudioClip.Create(clipName, samples.Length, 1, SAMPLE_RATE, false);
            clip.SetData(samples, 0);
            return clip;
        }

        // ------------------------------------------------------------- shapes

        /// The GESTURE of a mood — what actually makes a voice read as happy or
        /// hurt. Listeners key on RHYTHM and CONTOUR DIRECTION far more than on
        /// steady-state pitch, so these are modelled structurally.
        private struct Shape
        {
            public int Syllables;
            public float[] Gains;
            public float[] Steps;        // pitch multiplier at each syllable onset
            public float IntraGlide;     // pitch multiplier across a syllable
            public float Attack;         // fraction of a syllable
            public float Decay;
            public float Breath;
            public float VibratoHz;
            public float VibratoDepth;
            public float Onset;          // consonant noise burst
            public float Gap;            // silent fraction at the end of each slot
            public float Drive;          // tanh saturation: growl / strain
            public float Growl;          // 0..1 amplitude roughness at ~30 Hz
            public float Length;         // seconds
            public float Pitch;          // multiplier on the fighter's F0
            public float[] Vowel;        // three formants before tract scaling
        }

        private static readonly float[] VowelA = { 730f, 1090f, 2440f };   // open: effort
        private static readonly float[] VowelU = { 400f, 900f, 2300f };    // closed: groan
        private static readonly float[] VowelE = { 530f, 1840f, 2480f };   // bright: jeer
        private static readonly float[] VowelO = { 570f, 840f, 2410f };    // round: roar
        private static readonly float[] VowelI = { 300f, 2200f, 2950f };   // tight: yelp

        private static Shape ShapeFor(Systems_FighterMood mood, float intensity, System.Random rng, int variant)
        {
            Shape shape = default;
            // Variant-level structure, drawn once per (fighter, mood, variant).
            float spread = (float)rng.NextDouble();
            switch (mood)
            {
                // A ROAR. Low, saturated, sustained, an open rounded vowel, and a
                // fast amplitude roughness that reads as a growl. The contour rises
                // into the effort and drops at the end.
                case Systems_FighterMood.Angry:
                    shape.Syllables = 1 + variant % 2;                       // 1-2
                    shape.Gains = shape.Syllables == 1 ? new[] { 1f } : new[] { 0.75f, 1f };
                    shape.Steps = shape.Syllables == 1 ? new[] { 1f } : new[] { 1f, 1.04f + 0.04f * spread };
                    shape.IntraGlide = Mathf.Lerp(1.04f, 0.90f, spread);
                    shape.Attack = Mathf.Lerp(0.10f, 0.05f, intensity);
                    shape.Decay = Mathf.Lerp(1.4f, 1.0f, intensity);
                    shape.Breath = 0.18f;
                    shape.VibratoHz = 5f;
                    shape.VibratoDepth = 0.01f;
                    shape.Onset = 0.25f;
                    shape.Gap = 0.12f;
                    shape.Drive = Mathf.Lerp(1.6f, 4.0f, intensity);
                    shape.Growl = Mathf.Lerp(0.20f, 0.65f, intensity);
                    shape.Length = Mathf.Lerp(0.50f, 1.00f, intensity) * Mathf.Lerp(0.85f, 1.15f, spread);
                    shape.Pitch = 0.92f;
                    shape.Vowel = variant % 3 == 0 ? VowelA : VowelO;
                    break;

                // A PAIN GRUNT. Short, punched in with almost no attack, a sharp pitch
                // spike that collapses, a tight vowel, and strain at the top end.
                // ("ugh!", "oof", "agh-") — the opposite of a slow groan.
                case Systems_FighterMood.Hurt:
                    shape.Syllables = variant == 4 ? 2 : 1;
                    shape.Gains = shape.Syllables == 1 ? new[] { 1f } : new[] { 1f, 0.55f };
                    shape.Steps = shape.Syllables == 1 ? new[] { 1f } : new[] { 1f, 0.82f };
                    shape.IntraGlide = Mathf.Lerp(0.80f, 0.62f, intensity);
                    shape.Attack = 0.015f;
                    shape.Decay = Mathf.Lerp(4.5f, 3.0f, intensity);
                    shape.Breath = Mathf.Lerp(0.40f, 0.28f, intensity);
                    shape.VibratoHz = 9f;
                    shape.VibratoDepth = 0.02f;
                    shape.Onset = 0.45f;
                    shape.Gap = 0.25f;
                    shape.Drive = Mathf.Lerp(1.2f, 2.6f, intensity);
                    shape.Growl = 0.10f;
                    shape.Length = Mathf.Lerp(0.26f, 0.62f, intensity) * Mathf.Lerp(0.85f, 1.2f, spread);
                    shape.Pitch = Mathf.Lerp(1.10f, 1.35f, intensity);
                    shape.Vowel = variant % 2 == 0 ? VowelU : VowelI;
                    break;

                // A JEER. Two to four clipped syllables stepping DOWN, punched in and
                // cut off, almost no breath — the control is the insult.
                case Systems_FighterMood.Insult:
                    shape.Syllables = 2 + variant % 3;                       // 2-4
                    shape.Gains = new float[shape.Syllables];
                    shape.Steps = new float[shape.Syllables];
                    for (int syllable = 0; syllable < shape.Syllables; syllable++)
                    {
                        shape.Gains[syllable] = syllable == 0 ? 1f : Mathf.Lerp(0.75f, 0.95f, (float)rng.NextDouble());
                        // Mostly stepping down; variant 3 bounces back up on the last.
                        float step = 1f - 0.06f * syllable;
                        if (variant == 3 && syllable == shape.Syllables - 1) step = 1.08f;
                        shape.Steps[syllable] = step;
                    }
                    shape.IntraGlide = 0.97f;
                    shape.Attack = 0.05f;
                    shape.Decay = Mathf.Lerp(5.5f, 4.5f, intensity);
                    shape.Breath = 0.10f;
                    shape.VibratoHz = 6.5f;
                    shape.VibratoDepth = 0.008f;
                    shape.Onset = 0.55f;
                    shape.Gap = 0.30f;
                    shape.Drive = 1.2f;
                    shape.Growl = 0f;
                    shape.Length = Mathf.Lerp(0.20f, 0.30f, intensity) * shape.Syllables * Mathf.Lerp(0.9f, 1.1f, spread);
                    shape.Pitch = 1.05f;
                    shape.Vowel = VowelE;
                    break;

                // A CHEER. Weight on the LAST syllable with the pitch rising into
                // it — a rise at the end separates delight from mere effort.
                default:
                    shape.Syllables = 1 + variant % 3;                       // 1-3
                    shape.Gains = new float[shape.Syllables];
                    shape.Steps = new float[shape.Syllables];
                    for (int syllable = 0; syllable < shape.Syllables; syllable++)
                    {
                        float k = shape.Syllables == 1 ? 1f : syllable / (float)(shape.Syllables - 1);
                        shape.Gains[syllable] = Mathf.Lerp(0.72f, 1f, k);
                        shape.Steps[syllable] = 1f + k * Mathf.Lerp(0.10f, 0.22f, intensity);
                    }
                    shape.IntraGlide = Mathf.Lerp(1.05f, 1.12f, intensity);
                    shape.Attack = Mathf.Lerp(0.09f, 0.04f, intensity);
                    shape.Decay = Mathf.Lerp(3.0f, 2.4f, intensity);
                    shape.Breath = Mathf.Lerp(0.22f, 0.14f, intensity);
                    shape.VibratoHz = 5.8f;
                    shape.VibratoDepth = 0.014f;
                    shape.Onset = 0.30f;
                    shape.Gap = 0.14f;
                    shape.Drive = Mathf.Lerp(1.0f, 1.8f, intensity);
                    shape.Growl = 0f;
                    shape.Length = Mathf.Lerp(0.40f, 0.80f, intensity) * Mathf.Lerp(0.85f, 1.15f, spread)
                                   * (0.7f + 0.3f * shape.Syllables);
                    shape.Pitch = 1.12f;
                    shape.Vowel = variant % 2 == 0 ? VowelA : VowelE;
                    break;
            }
            // Never longer than ~1.3 s: a voice is gated for 0.6 x the clip length
            // after it speaks, so a long placeholder would mute a fighter.
            shape.Length = Mathf.Min(shape.Length, 1.3f);
            return shape;
        }

        // ---------------------------------------------------------- synthesis

        private static float[] Synthesize(Voice voice, Systems_FighterMood mood, int level, int variant)
        {
            level = Mathf.Clamp(level, 1, 5);
            variant = Mathf.Clamp(variant, 1, 5);
            var rng = new System.Random(voice.Seed * 31 + (int)mood * 977 + level * 53 + variant * 7919);
            float intensity = (level - 1) / 4f;
            Shape shape = ShapeFor(mood, intensity, rng, variant);

            int count = Mathf.Max(1, (int)(shape.Length * SAMPLE_RATE));
            var buffer = new float[count];

            float f0 = voice.F0 * shape.Pitch * (1f + 0.28f * intensity) * Mathf.Lerp(0.95f, 1.05f, (float)rng.NextDouble());
            float openness = 0.30f * intensity;
            float tract = Mathf.Max(0.5f, voice.Tract);
            var r1 = Biquad.BandPass(Mathf.Lerp(shape.Vowel[0], VowelA[0], openness) / tract, 7f);
            var r2 = Biquad.BandPass(Mathf.Lerp(shape.Vowel[1], VowelA[1], openness) / tract, 9f);
            var r3 = Biquad.BandPass(Mathf.Lerp(shape.Vowel[2], VowelA[2], openness) / tract, 11f);
            var breathBp = Biquad.BandPass(1700f, 0.8f);
            var onsetBp = Biquad.BandPass(2600f, 1.4f);

            float slot = 1f / shape.Syllables;
            double phase = 0;
            float growlPhase = 0f;
            float growlHz = 26f + 10f * (float)rng.NextDouble();

            for (int index = 0; index < count; index++)
            {
                float progress = index / (float)count;
                int syllable = Mathf.Min(shape.Syllables - 1, (int)(progress / slot));
                float within = (progress - syllable * slot) / slot;

                float voicedSpan = syllable == shape.Syllables - 1 ? 1f : 1f - shape.Gap;
                if (within > voicedSpan)
                {
                    buffer[index] = 0f;
                    continue;
                }
                float inSyllable = within / voicedSpan;

                float hz = f0 * shape.Steps[syllable]
                           * Mathf.Lerp(1f, shape.IntraGlide, inSyllable)
                           * (1f + shape.VibratoDepth * Mathf.Sin(progress * shape.Length * Mathf.PI * 2f * shape.VibratoHz));

                phase += hz / SAMPLE_RATE;
                if (phase >= 1.0) phase -= 1.0;

                float source = (float)(2.0 * phase - 1.0);
                float voiced = r1.Process(source) + r2.Process(source) * 0.5f + r3.Process(source) * 0.22f;

                float noise = (float)(rng.NextDouble() * 2 - 1);
                float breath = breathBp.Process(noise) * shape.Breath;
                float onset = onsetBp.Process(noise) * shape.Onset * Mathf.Exp(-inSyllable * 42f);

                float env = Mathf.Min(1f, inSyllable / Mathf.Max(0.001f, shape.Attack))
                            * Mathf.Exp(-inSyllable * shape.Decay)
                            * shape.Gains[syllable];

                if (shape.Growl > 0f)
                {
                    growlPhase += growlHz / SAMPLE_RATE;
                    if (growlPhase >= 1f) growlPhase -= 1f;
                    env *= 1f - shape.Growl * 0.5f * (1f + Mathf.Sin(growlPhase * Mathf.PI * 2f));
                }

                float sample = (voiced + breath + onset) * env;
                if (shape.Drive > 1f)
                {
                    sample = (float)Math.Tanh(sample * shape.Drive) / (float)Math.Tanh(shape.Drive);
                }
                buffer[index] = sample;
            }

            // Short fade at both ends so a hard-cut syllable never clicks.
            int fade = Mathf.Min(count / 4, SAMPLE_RATE / 200);
            for (int index = 0; index < fade; index++)
            {
                float k = index / (float)fade;
                buffer[index] *= k;
                buffer[count - 1 - index] *= k;
            }

            return Normalize(buffer, Mathf.Lerp(0.55f, 0.85f, intensity));
        }

        private static float[] Normalize(float[] buffer, float peak)
        {
            float max = 0f;
            for (int index = 0; index < buffer.Length; index++) max = Mathf.Max(max, Mathf.Abs(buffer[index]));
            if (max < 1e-6f) return buffer;
            float scale = peak / max;
            for (int index = 0; index < buffer.Length; index++) buffer[index] *= scale;
            return buffer;
        }

        /// Direct-form-I biquad band-pass, RBJ cookbook. Stateful: one per signal.
        private sealed class Biquad
        {
            private double _a0, _a2, _b1, _b2;
            private double _x1, _x2, _y1, _y2;

            public static Biquad BandPass(double freq, double q)
            {
                double omega = 2.0 * Math.PI * Math.Min(freq, SAMPLE_RATE * 0.45) / SAMPLE_RATE;
                double sn = Math.Sin(omega);
                double cs = Math.Cos(omega);
                double alpha = sn / (2.0 * q);
                double b0 = 1.0 + alpha;
                return new Biquad
                {
                    _a0 = alpha / b0,
                    _a2 = -alpha / b0,
                    _b1 = -2.0 * cs / b0,
                    _b2 = (1.0 - alpha) / b0,
                };
            }

            public float Process(float input)
            {
                double output = _a0 * input + _a2 * _x2 - _b1 * _y1 - _b2 * _y2;
                _x2 = _x1;
                _x1 = input;
                _y2 = _y1;
                _y1 = output;
                return (float)output;
            }
        }
    }
}
