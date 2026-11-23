using System;
using System.Collections.Generic;
using UnityEngine;

namespace PoSumo
{
    /// The ADVANCED PROCEDURAL IMITATION (Po voice-manifest spec, 2026-11-22): a
    /// short human vocalisation by one particular fighter — "hah!", "rraah",
    /// "oof", "nyah-nyah", "woo-hoo!" — for any (fighter, emotion, level, take)
    /// whose WAV `voices.json` says does not exist.
    ///
    /// Model, per sample:
    ///   * SOURCE: a Rosenberg glottal pulse train at the fighter's pitch (the
    ///     flow derivative, which is what the lips radiate), with per-pulse jitter
    ///     (~1%) and shimmer (~4%), period-doubling + slow amplitude modulation for
    ///     RASP, and aspiration noise gated by the glottal opening for BREATH.
    ///   * FILTER: four formant resonators (RBJ band-passes) on vowel targets
    ///     a/e/i/o/u/schwa, scaled by the fighter's formant scale (tract length);
    ///     formants GLIDE from the previous vowel across the first part of each
    ///     syllable, so "woo-hoo" moves and a groan closes.
    ///   * SYLLABLES: 1-4, each with an optional consonant onset — h (breathy
    ///     noise through the vowel), s / sh (fricatives), k / t / p (closure +
    ///     plosive click), n (nasal murmur), w (u-glide) — plus a gasp (inhaled
    ///     noise) for hurt.
    ///   * PROSODY per emotion (pitch contour, timing, tension, vibrato, rasp,
    ///     nasality), scaled by level 1-5: loudness, length, pitch range, rasp,
    ///     breath, syllable count and vibrato all grow with the level.
    ///
    /// Replaces the earlier saw-through-three-formants model ("a cheer, a roar, a
    /// pained grunt, a jeer"), keeping its API, so every caller is unchanged.
    ///
    /// Deterministic: the seed is a hash of (fighter seed, emotion, level, take),
    /// so the same request always produces the same samples; takes differ a
    /// little. Samples are CACHED per request after the first synthesis. Pure
    /// C# maths (System.Math) except `Create`, which wraps the samples in an
    /// AudioClip on the main thread.
    public static class Systems_VoiceSynth
    {
        public const int SAMPLE_RATE = 22050;
        /// Longest line synthesised. The voice is gated for 0.6 x the clip length
        /// after it speaks, so a long placeholder would mute a fighter.
        public const float MAX_SECONDS = 1.6f;

        /// A fighter's vocal identity: the `voice` profile of `voices.json`.
        public readonly struct Voice
        {
            public readonly float F0;
            /// Vocal-tract length factor (>1 = longer tract = lower formants).
            /// The manifest's `formantScale` is its reciprocal.
            public readonly float Tract;
            public readonly int Seed;
            public readonly float Breath;
            public readonly float Rasp;
            public readonly float VibratoHz;
            public readonly float VibratoDepth;
            public readonly float Tempo;

            public Voice(float f0, float tract, int seed)
                : this(f0, tract, seed, 0.25f, 0.25f, 5.2f, 0.02f, 1f)
            {
            }

            public Voice(float f0, float tract, int seed, float breath, float rasp,
                         float vibratoHz, float vibratoDepth, float tempo)
            {
                F0 = f0;
                Tract = tract;
                Seed = seed;
                Breath = breath;
                Rasp = rasp;
                VibratoHz = vibratoHz;
                VibratoDepth = vibratoDepth;
                Tempo = tempo;
            }

            public float FormantScale => 1f / Math.Max(0.5f, Tract);
        }

        /// The voice for a character asset when there is no manifest profile. An
        /// unset pitch is derived from the behavior name, so every fighter sounds
        /// distinct and stable across runs; the texture terms come from the same
        /// derivation `voices.json` uses (`Systems_VoiceManifest.DefaultProfile`).
        public static Voice VoiceFor(Agent_CharacterDefinition character, string fallbackName)
        {
            string name = character != null ? character.behaviorName : fallbackName;
            Systems_VoiceManifest.VoiceProfile profile = Systems_VoiceManifest.DefaultProfile(
                name, character != null ? character.voicePitchHz : 0f, character != null ? character.voiceTract : 1f);
            return FromProfile(name, profile);
        }

        /// The synth voice for a manifest profile. `id` seeds it.
        public static Voice FromProfile(string id, Systems_VoiceManifest.VoiceProfile profile)
        {
            int seed = StableHash(string.IsNullOrEmpty(id) ? string.Empty : id.ToLowerInvariant());
            if (profile == null) return new Voice(92f + (seed & 0x3F), 1f, seed);
            float f0 = profile.pitchHz > 0f ? profile.pitchHz : 92f + (seed & 0x3F);
            float tract = profile.formantScale > 0f ? 1f / profile.formantScale : 1f;
            return new Voice(f0, tract, seed, Clamp01(profile.breath), Clamp01(profile.rasp),
                             profile.vibratoHz > 0f ? profile.vibratoHz : 5.2f,
                             Math.Max(0f, profile.vibratoDepth),
                             profile.tempo > 0.1f ? profile.tempo : 1f);
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

        // ------------------------------------------------------------- cache

        private const int CACHE_LIMIT = 256;
        private static readonly Dictionary<string, float[]> SampleCache = new Dictionary<string, float[]>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ClearCache() => SampleCache.Clear();

        private static string CacheKey(Voice voice, Systems_FighterMood mood, int level, int variant) =>
            $"{voice.Seed}|{voice.F0:F1}|{voice.Tract:F3}|{voice.Breath:F2}|{voice.Rasp:F2}|{(int)mood}|{level}|{variant}";

        /// The line as an AudioClip. Samples come from the cache after the first
        /// synthesis of the same request; the clip itself belongs to the caller.
        public static AudioClip Create(string clipName, Voice voice, Systems_FighterMood mood, int level, int variant)
        {
            string key = CacheKey(voice, mood, level, variant);
            if (!SampleCache.TryGetValue(key, out float[] samples))
            {
                samples = Synthesize(voice, mood, level, variant);
                if (SampleCache.Count >= CACHE_LIMIT) SampleCache.Clear();
                SampleCache[key] = samples;
            }
            AudioClip clip = AudioClip.Create(clipName, samples.Length, 1, SAMPLE_RATE, false);
            clip.SetData(samples, 0);
            return clip;
        }

        // ------------------------------------------------------------ phonetics

        private enum Vowel { A, E, I, O, U, Schwa }
        private enum Onset { None, H, S, Sh, K, T, P, N, W }

        /// F1-F4 (Hz) and bandwidth Q per vowel, adult reference tract.
        private static readonly float[][] Formants =
        {
            new[] { 730f, 1090f, 2440f, 3400f },   // a  "hah"
            new[] { 530f, 1840f, 2480f, 3500f },   // e  "heh"
            new[] { 300f, 2200f, 2950f, 3700f },   // i  "hee"
            new[] { 570f, 840f, 2410f, 3300f },    // o  "hoh"
            new[] { 330f, 870f, 2240f, 3200f },    // u  "oof"
            new[] { 500f, 1500f, 2500f, 3450f },   // schwa "uh"
        };
        private static readonly float[] FormantGain = { 1f, 0.62f, 0.30f, 0.14f };
        private static readonly float[] FormantQ = { 6f, 9f, 12f, 14f };

        private struct Syllable
        {
            public Onset Onset;
            public Vowel From;           // glide start (previous vowel, or u for w)
            public Vowel To;
            public float Seconds;        // voiced part
            public float Gap;            // silence after
            public float Gain;
            public float PitchStart;     // multipliers on F0
            public float PitchEnd;
            public float PitchBreakAt;   // 0..1 within the syllable, or > 1 for none
            public float PitchBreak;     // multiplier applied after the break
            public float Attack;         // seconds
            public float Release;        // fraction of the syllable that decays
        }

        private sealed class Plan
        {
            public readonly List<Syllable> Syllables = new List<Syllable>();
            public float GaspSeconds;    // inhaled noise before the first syllable
            public float Tension;        // 0 lax .. 1 pressed (closes the glottal pulse)
            public float Rasp;
            public float Breath;
            public float VibratoHz;
            public float VibratoDepth;
            public float Nasal;          // raises F2, adds a low murmur
            public float Drive;
            public float Brightness;     // raises formants a touch
        }

        // -------------------------------------------------------------- prosody

        private static Plan PlanFor(Voice voice, Systems_FighterMood mood, int level, int variant, System.Random rng)
        {
            float t = (level - 1) / 4f;                         // intensity 0..1
            float tempo = Math.Max(0.6f, Math.Min(1.5f, voice.Tempo));
            float take = (float)rng.NextDouble();               // small per-take variation
            var plan = new Plan
            {
                Rasp = voice.Rasp,
                Breath = voice.Breath,
                VibratoHz = voice.VibratoHz,
                VibratoDepth = voice.VibratoDepth,
                Tension = 0.5f,
                Drive = 1.2f + 0.8f * t,
            };
            Vowel last = Vowel.Schwa;

            void Add(Onset onset, Vowel vowel, float seconds, float gap, float gain,
                     float p0, float p1, float attack = 0.02f, float release = 0.35f,
                     float breakAt = 2f, float breakTo = 1f)
            {
                plan.Syllables.Add(new Syllable
                {
                    Onset = onset,
                    From = onset == Onset.W ? Vowel.U : last,
                    To = vowel,
                    Seconds = seconds * Lerp(0.92f, 1.08f, take) / tempo,
                    Gap = gap / tempo,
                    Gain = gain,
                    PitchStart = p0,
                    PitchEnd = p1,
                    Attack = attack,
                    Release = release,
                    PitchBreakAt = breakAt,
                    PitchBreak = breakTo,
                });
                last = vowel;
            }

            switch (mood)
            {
                // "rraah", "hup!", "grah-AAH": pressed, loud, falling, raspy.
                case Systems_FighterMood.Angry:
                {
                    plan.Tension = 0.85f;
                    plan.Rasp = Clamp01(voice.Rasp + 0.25f + 0.45f * t);
                    plan.Breath = voice.Breath * 0.6f;
                    plan.Drive = 1.8f + 2.6f * t;
                    plan.VibratoDepth *= 0.5f;
                    int count = 1 + (level >= 3 ? 1 : 0) + (level >= 5 && variant % 2 == 0 ? 1 : 0);
                    for (int s = 0; s < count; s++)
                    {
                        bool lastOne = s == count - 1;
                        Onset onset = s == 0 ? (variant % 3 == 0 ? Onset.K : Onset.H) : Onset.None;
                        float seconds = lastOne ? 0.26f + 0.42f * t : 0.10f + 0.06f * t;
                        Add(onset, variant % 2 == 0 ? Vowel.A : Vowel.O, seconds, lastOne ? 0f : 0.04f,
                            lastOne ? 1f : 0.8f, 1.02f + 0.20f * t, 0.84f - 0.06f * t, attack: 0.008f, release: 0.30f);
                    }
                    break;
                }

                // "heh", "ha-ha", "hee-hee-hah": bright, rise-fall, laugh pulses.
                case Systems_FighterMood.Happy:
                {
                    plan.Tension = 0.45f;
                    plan.Brightness = 0.06f + 0.06f * t;
                    plan.VibratoDepth = voice.VibratoDepth * (1f + t);
                    int count = level <= 1 ? 1 : level <= 2 ? 2 : level <= 4 ? 3 : 4;
                    Vowel vowel = variant % 2 == 0 ? Vowel.E : Vowel.A;
                    for (int s = 0; s < count; s++)
                    {
                        float k = count == 1 ? 0f : s / (float)(count - 1);
                        // Rise into the middle, fall at the end: delight, not effort.
                        float contour = 1f + (0.10f + 0.20f * t) * (float)Math.Sin(Math.PI * (0.3f + 0.6f * k));
                        Add(Onset.H, s == count - 1 && level >= 4 ? Vowel.A : vowel,
                            0.13f + 0.05f * t + (s == count - 1 ? 0.06f + 0.10f * t : 0f),
                            s == count - 1 ? 0f : 0.05f, Lerp(0.85f, 1f, k),
                            contour, contour * 0.96f, attack: 0.012f, release: 0.45f);
                    }
                    break;
                }

                // gasp, then "oof" / "ugh" with a pitch break; level 5 a long cry.
                case Systems_FighterMood.Hurt:
                {
                    plan.Tension = 0.6f;
                    plan.Breath = Clamp01(voice.Breath + 0.15f + 0.15f * t);
                    plan.Rasp = Clamp01(voice.Rasp + 0.1f * t);
                    plan.GaspSeconds = level >= 2 ? 0.08f + 0.10f * t : 0f;
                    if (level >= 5)
                    {
                        plan.VibratoDepth = voice.VibratoDepth * 2.5f + 0.02f;
                        Add(Onset.None, Vowel.A, 0.55f, 0f, 1f, 1.35f, 1.10f, attack: 0.02f, release: 0.15f);
                        Add(Onset.None, Vowel.U, 0.45f, 0f, 0.9f, 1.05f, 0.70f, attack: 0.03f, release: 0.6f);
                    }
                    else
                    {
                        Onset onset = variant % 2 == 0 ? Onset.None : Onset.H;
                        Vowel vowel = variant % 3 == 0 ? Vowel.Schwa : Vowel.U;
                        Add(onset, vowel, 0.16f + 0.24f * t, level >= 3 ? 0.03f : 0f, 1f,
                            1.18f + 0.22f * t, 0.95f, attack: 0.006f, release: 0.55f,
                            breakAt: 0.35f, breakTo: 0.74f);
                        if (level >= 3)
                        {
                            Add(Onset.None, Vowel.Schwa, 0.12f + 0.12f * t, 0f, 0.6f, 0.80f, 0.68f,
                                attack: 0.02f, release: 0.7f);
                        }
                    }
                    break;
                }

                // "nyah-nyah", "na-na-nah", "pfft-heh": nasal sing-song.
                case Systems_FighterMood.Insult:
                {
                    plan.Tension = 0.55f;
                    plan.Nasal = 0.6f + 0.3f * t;
                    plan.VibratoDepth *= 0.4f;
                    if (variant == 3)
                    {
                        // "pfft-heh": a plosive and a fricative, then a sneer.
                        Add(Onset.P, Vowel.Schwa, 0.02f, 0.06f + 0.04f * t, 0.15f, 1f, 1f, attack: 0.002f);
                        Add(Onset.H, Vowel.E, 0.14f + 0.10f * t, level >= 3 ? 0.05f : 0f, 1f, 1.10f, 0.96f);
                        if (level >= 3) Add(Onset.H, Vowel.E, 0.12f + 0.08f * t, 0f, 0.85f, 1.02f, 0.92f);
                        break;
                    }
                    int count = 2 + (level >= 3 ? 1 : 0) + (level >= 5 ? 1 : 0);
                    for (int s = 0; s < count; s++)
                    {
                        bool high = s % 2 == 0;
                        float lift = 0.12f + 0.12f * t;
                        float p = high ? 1f + lift : 1f - lift * 0.3f;
                        Add(Onset.N, Vowel.A, 0.12f + 0.07f * t + (s == count - 1 ? 0.06f * t : 0f),
                            s == count - 1 ? 0f : 0.04f, high ? 1f : 0.85f, p, p * 0.98f,
                            attack: 0.01f, release: 0.35f);
                    }
                    break;
                }

                // "yeah!", "woo-hoo!", "woo-hoo-hah-hah": a rising whoop held on
                // the final vowel with wide vibrato, a laugh after at high levels.
                case Systems_FighterMood.Celebrate:
                {
                    plan.Tension = 0.6f;
                    plan.Brightness = 0.08f;
                    plan.Drive = 1.6f + 1.6f * t;
                    plan.VibratoDepth = voice.VibratoDepth * 2f + 0.015f + 0.02f * t;
                    if (level >= 2)
                    {
                        Add(Onset.W, Vowel.U, 0.14f + 0.04f * t, 0.04f, 0.8f, 1.0f, 1.08f, attack: 0.02f);
                    }
                    Add(level >= 2 ? Onset.H : Onset.W, variant % 2 == 0 ? Vowel.O : Vowel.E,
                        0.42f + 0.36f * t, level >= 4 ? 0.05f : 0f, 1f,
                        1.08f + 0.10f * t, 1.38f + 0.22f * t, attack: 0.03f, release: 0.25f);
                    if (level >= 4)
                    {
                        Add(Onset.H, Vowel.A, 0.10f, 0.04f, 0.6f, 1.20f, 1.12f, attack: 0.01f, release: 0.5f);
                        Add(Onset.H, Vowel.A, 0.10f + 0.04f * t, 0f, 0.55f, 1.12f, 1.02f, attack: 0.01f, release: 0.6f);
                    }
                    break;
                }

                default:
                    Add(Onset.H, Vowel.Schwa, 0.2f, 0f, 1f, 1f, 0.95f);
                    break;
            }
            return plan;
        }

        // ------------------------------------------------------------- render

        private const float ONSET_SECONDS = 0.05f;
        private const float PLOSIVE_CLOSURE = 0.018f;

        /// The raw samples `Create` wraps. Deterministic for the same inputs.
        public static float[] Synthesize(Voice voice, Systems_FighterMood mood, int level, int variant)
        {
            level = Math.Max(1, Math.Min(5, level));
            variant = Math.Max(1, Math.Min(5, variant));
            int seed = unchecked(voice.Seed * 31 + (int)mood * 977 + level * 53 + variant * 7919);
            var rng = new System.Random(seed);
            Plan plan = PlanFor(voice, mood, level, variant, rng);
            float t = (level - 1) / 4f;

            // Lay the timeline out: gasp, then for each syllable onset + vowel + gap.
            int gasp = (int)(plan.GaspSeconds * SAMPLE_RATE);
            int total = gasp;
            foreach (Syllable syllable in plan.Syllables)
            {
                total += OnsetSamples(syllable.Onset) + (int)(syllable.Seconds * SAMPLE_RATE)
                         + (int)(syllable.Gap * SAMPLE_RATE);
            }
            total = Math.Max(1, Math.Min(total, (int)(MAX_SECONDS * SAMPLE_RATE)));
            var buffer = new float[total];

            float formantScale = voice.FormantScale * (1f + plan.Brightness);
            var resonators = new Biquad[4];
            for (int f = 0; f < 4; f++) resonators[f] = new Biquad();
            var nasalMurmur = Biquad.BandPass(260f * formantScale, 3f);
            var hiss = Biquad.BandPass(5200f, 2.2f);
            var hush = Biquad.BandPass(2800f, 1.6f);
            var gaspFilter = Biquad.BandPass(1900f, 0.9f);

            // Glottal state.
            double phase = 0;
            float pulseAmp = 1f;
            float pulseJitter = 1f;
            int pulseIndex = 0;
            float prevFlow = 0f;
            float raspPhase = 0f;
            float raspHz = 24f + 14f * (float)rng.NextDouble();
            float jitter = 0.005f + 0.01f * plan.Rasp;          // ~0.5-1.5 %
            float shimmer = 0.03f + 0.03f * plan.Rasp;          // ~3-6 %
            float openQuotient = Lerp(0.72f, 0.40f, plan.Tension);
            float speedQuotient = 0.66f;                        // opening share of the open phase

            int cursor = 0;
            // Gasp: an inhaled, rising, band-limited noise ("hhk").
            for (int i = 0; i < gasp && cursor < total; i++, cursor++)
            {
                float k = i / (float)Math.Max(1, gasp);
                float env = (float)Math.Sin(Math.PI * Math.Min(1f, k * 1.2f)) * (0.35f + 0.25f * t);
                buffer[cursor] = gaspFilter.Process(Noise(rng)) * env * 2.2f;
            }

            for (int s = 0; s < plan.Syllables.Count && cursor < total; s++)
            {
                Syllable syl = plan.Syllables[s];
                int onsetCount = OnsetSamples(syl.Onset);
                int voicedCount = (int)(syl.Seconds * SAMPLE_RATE);
                int gapCount = (int)(syl.Gap * SAMPLE_RATE);

                // ---- consonant onset
                for (int i = 0; i < onsetCount && cursor < total; i++, cursor++)
                {
                    float k = i / (float)Math.Max(1, onsetCount);
                    float sample = 0f;
                    switch (syl.Onset)
                    {
                        case Onset.H:
                            sample = ShapeFormants(resonators, syl.To, formantScale, plan.Nasal, Noise(rng))
                                     * 0.5f * k * syl.Gain;
                            break;
                        case Onset.S:
                            sample = hiss.Process(Noise(rng)) * 0.7f * Hann(k) * syl.Gain;
                            break;
                        case Onset.Sh:
                            sample = hush.Process(Noise(rng)) * 0.8f * Hann(k) * syl.Gain;
                            break;
                        case Onset.K:
                        case Onset.T:
                        case Onset.P:
                        {
                            float closure = PLOSIVE_CLOSURE * SAMPLE_RATE;
                            if (i >= closure)
                            {
                                float burst = (i - closure) / (onsetCount - closure);
                                float centre = syl.Onset == Onset.K ? 1600f : syl.Onset == Onset.T ? 3800f : 750f;
                                float click = (float)Math.Exp(-burst * 9f);
                                sample = Biquad.Ring(centre, burst, rng) * click * 0.9f * syl.Gain;
                            }
                            break;
                        }
                    }
                    buffer[cursor] = sample;
                }

                // ---- the voiced vowel
                for (int i = 0; i < voicedCount && cursor < total; i++, cursor++)
                {
                    float k = i / (float)Math.Max(1, voicedCount);
                    float seconds = i / (float)SAMPLE_RATE;

                    // Pitch: contour, optional break, vibrato (grows into the
                    // syllable), per-pulse jitter.
                    float contour = Lerp(syl.PitchStart, syl.PitchEnd, Smooth(k));
                    if (k >= syl.PitchBreakAt) contour *= syl.PitchBreak;
                    float vibrato = 1f + plan.VibratoDepth * Math.Min(1f, k * 3f)
                                         * (float)Math.Sin(2.0 * Math.PI * plan.VibratoHz * seconds);
                    float hz = voice.F0 * (1f + 0.25f * t) * contour * vibrato * pulseJitter;

                    phase += hz / SAMPLE_RATE;
                    if (phase >= 1.0)
                    {
                        phase -= 1.0;
                        pulseIndex++;
                        pulseJitter = 1f + jitter * (float)(rng.NextDouble() * 2 - 1);
                        pulseAmp = 1f + shimmer * (float)(rng.NextDouble() * 2 - 1);
                        // Rasp: period doubling — every other pulse is weaker.
                        if ((pulseIndex & 1) == 1) pulseAmp *= 1f - 0.55f * plan.Rasp;
                    }

                    float flow = Rosenberg((float)phase, openQuotient, speedQuotient);
                    float source = (flow - prevFlow) * 18f * pulseAmp;   // flow derivative
                    prevFlow = flow;

                    float aspiration = Noise(rng) * plan.Breath * (0.35f + 0.65f * flow) * 0.6f;

                    // Formants glide from the previous vowel over the first third.
                    float glide = Math.Min(1f, k / 0.33f);
                    if ((i & 31) == 0) TuneFormants(resonators, syl.From, syl.To, Smooth(glide), formantScale, plan.Nasal);
                    float voiced = 0f;
                    for (int f = 0; f < 4; f++) voiced += resonators[f].Process(source + aspiration) * FormantGain[f];
                    if (plan.Nasal > 0f) voiced += nasalMurmur.Process(source) * 0.5f * plan.Nasal;
                    if (syl.Onset == Onset.N && k < 0.18f)
                    {
                        voiced = nasalMurmur.Process(source) * 0.9f;   // "n" before the vowel opens
                    }

                    // Rasp also as a slow amplitude roughness.
                    float env = Math.Min(1f, seconds / Math.Max(0.001f, syl.Attack));
                    float releaseStart = 1f - syl.Release;
                    if (k > releaseStart) env *= (float)Math.Exp(-(k - releaseStart) / Math.Max(0.01f, syl.Release) * 3.2f);
                    raspPhase += raspHz / SAMPLE_RATE;
                    if (raspPhase >= 1f) raspPhase -= 1f;
                    env *= 1f - plan.Rasp * 0.35f * (1f + (float)Math.Sin(raspPhase * Math.PI * 2f)) * 0.5f;

                    float sample = voiced * env * syl.Gain;
                    if (plan.Drive > 1f) sample = (float)(Math.Tanh(sample * plan.Drive) / Math.Tanh(plan.Drive));
                    buffer[cursor] = sample;
                }

                // ---- gap
                for (int i = 0; i < gapCount && cursor < total; i++, cursor++) buffer[cursor] = 0f;
            }

            // Short fades so a hard-cut syllable never clicks.
            int fade = Math.Min(total / 4, SAMPLE_RATE / 200);
            for (int index = 0; index < fade; index++)
            {
                float k = index / (float)fade;
                buffer[index] *= k;
                buffer[total - 1 - index] *= k;
            }

            // Level sets loudness; recorded lines are normalised to the same range
            // by NormalizeVoice, so the two sit together.
            return Normalize(buffer, Lerp(0.50f, 0.90f, t));
        }

        private static int OnsetSamples(Onset onset)
        {
            switch (onset)
            {
                case Onset.None:
                case Onset.W:
                case Onset.N:
                    return 0;     // w and n are shaped inside the voiced part
                case Onset.K:
                case Onset.T:
                case Onset.P:
                    return (int)((PLOSIVE_CLOSURE + 0.025f) * SAMPLE_RATE);
                default:
                    return (int)(ONSET_SECONDS * SAMPLE_RATE);
            }
        }

        /// Rosenberg glottal flow over one period: opens over `oq*sq`, closes over
        /// the rest of the open phase, closed for 1 - oq.
        private static float Rosenberg(float phase, float openQuotient, float speedQuotient)
        {
            float rise = openQuotient * speedQuotient;
            float fall = openQuotient - rise;
            if (phase < rise) return 0.5f * (1f - (float)Math.Cos(Math.PI * phase / rise));
            if (phase < openQuotient) return (float)Math.Cos(Math.PI * 0.5 * (phase - rise) / fall);
            return 0f;
        }

        private static void TuneFormants(Biquad[] resonators, Vowel from, Vowel to, float k, float scale, float nasal)
        {
            float[] a = Formants[(int)from];
            float[] b = Formants[(int)to];
            for (int f = 0; f < 4; f++)
            {
                float hz = Lerp(a[f], b[f], k) * scale;
                if (f == 1) hz *= 1f + 0.18f * nasal;     // sneer: raised F2
                resonators[f].SetBandPass(hz, FormantQ[f]);
            }
        }

        /// Noise through the formants of one vowel ("h" carries the vowel's colour).
        private static float ShapeFormants(Biquad[] resonators, Vowel vowel, float scale, float nasal, float input)
        {
            TuneFormants(resonators, vowel, vowel, 1f, scale, nasal);
            float sum = 0f;
            for (int f = 0; f < 4; f++) sum += resonators[f].Process(input) * FormantGain[f];
            return sum;
        }

        private static float Noise(System.Random rng) => (float)(rng.NextDouble() * 2 - 1);
        private static float Hann(float k) => (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * k));
        private static float Smooth(float k) => k * k * (3f - 2f * k);
        private static float Lerp(float a, float b, float k) => a + (b - a) * Math.Max(0f, Math.Min(1f, k));
        private static float Clamp01(float value) => Math.Max(0f, Math.Min(1f, value));

        private static float[] Normalize(float[] buffer, float peak)
        {
            float max = 0f;
            for (int index = 0; index < buffer.Length; index++) max = Math.Max(max, Math.Abs(buffer[index]));
            if (max < 1e-6f) return buffer;
            float scale = peak / max;
            for (int index = 0; index < buffer.Length; index++) buffer[index] *= scale;
            return buffer;
        }

        /// Direct-form-I biquad band-pass, RBJ cookbook. Stateful: one per signal.
        /// Re-tunable in place (formant glides) without losing its state.
        private sealed class Biquad
        {
            private double _a0, _a2, _b1, _b2;
            private double _x1, _x2, _y1, _y2;

            public static Biquad BandPass(double freq, double q)
            {
                var biquad = new Biquad();
                biquad.SetBandPass(freq, q);
                return biquad;
            }

            public void SetBandPass(double freq, double q)
            {
                double omega = 2.0 * Math.PI * Math.Min(freq, SAMPLE_RATE * 0.45) / SAMPLE_RATE;
                double sn = Math.Sin(omega);
                double cs = Math.Cos(omega);
                double alpha = sn / (2.0 * q);
                double b0 = 1.0 + alpha;
                _a0 = alpha / b0;
                _a2 = -alpha / b0;
                _b1 = -2.0 * cs / b0;
                _b2 = (1.0 - alpha) / b0;
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

            /// A plosive burst: noise with a damped ring at the place of articulation.
            public static float Ring(float centre, float k, System.Random rng)
            {
                double ring = Math.Sin(2 * Math.PI * centre * k * 0.025);
                return (float)(0.6 * ring + 0.4 * (rng.NextDouble() * 2 - 1));
            }
        }
    }
}
