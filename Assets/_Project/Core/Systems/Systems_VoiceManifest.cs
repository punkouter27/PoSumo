using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PoSumo
{
    /// `voices.json` — the Po voice manifest (schema `po-voice-manifest/1`,
    /// spec of 2026-11-22). It lists EVERY expected voice clip, 5 emotions x 5
    /// levels for every fighter in the roster, with the WAV each one uses and
    /// whether that WAV exists, plus the fighter's procedural voice profile. It is
    /// the one place to look to learn which file a fighter says what with.
    ///
    /// Lives at `Assets/_Project/Data/Resources/Audio/Voice/voices.json` (loaded
    /// as the TextAsset `Audio/Voice/voices` by `Systems_VoiceLibrary`). Written
    /// by *PoSumo → Voice → Regenerate voices.json* (`VoiceManifestGenerator`, also
    /// run when a WAV under the voice folder is imported or deleted) — never by
    /// hand: `exists` must match the disk.
    ///
    /// File naming, in the order a cell is resolved:
    ///   1. STANDARD `<id>/<emotion>-<level>.wav` (id = lower-case behavior name);
    ///   2. LEGACY   `<Behavior>_<Mood>_L<level>_<take>.wav` — the recordings that
    ///      shipped for Matt / Grandma / Nick / Kim. When those are what actually
    ///      plays, the cell lists the real file and every take in `takes`;
    ///   3. otherwise the standard name with `exists: false`, and the line is the
    ///      advanced procedural imitation (`Systems_VoiceSynth`).
    ///
    /// The model doubles as the JsonUtility shape (`[Serializable]`, public
    /// fields, `clips` as one array per emotion). This file is pure C# — no
    /// UnityEngine — so the generator core and its writer can also run (and be
    /// tested) outside the editor.
    public static class Systems_VoiceManifest
    {
        public const string SCHEMA = "po-voice-manifest/1";
        public const string GAME = "PoSumo";
        public const string ROOT = "Assets/_Project/Data/Resources/Audio/Voice";
        public const string FILE_NAME = "voices.json";
        /// Resources path of the manifest TextAsset.
        public const string RESOURCE_PATH = "Audio/Voice/voices";
        /// Resources prefix of a clip: `RESOURCE_ROOT + file` without ".wav".
        public const string RESOURCE_ROOT = "Audio/Voice/";
        public const int LEVELS = 5;
        public const int LEGACY_TAKES = 5;

        /// The standard emotions in the standard's order, as written in the file.
        public static readonly string[] EMOTIONS = { "angry", "happy", "hurt", "insult", "celebrate" };

        [Serializable]
        public sealed class Manifest
        {
            public string schema = SCHEMA;
            public string game = GAME;
            public string generated = string.Empty;
            public string root = ROOT;
            public string[] emotions = EMOTIONS;
            public int[] levels = { 1, 2, 3, 4, 5 };
            public string fallback = "procedural";
            public Character[] characters = new Character[0];
        }

        [Serializable]
        public sealed class Character
        {
            public string id;
            public string name;
            public VoiceProfile voice;
            public Clips clips;
        }

        [Serializable]
        public sealed class VoiceProfile
        {
            public float pitchHz;
            public float formantScale = 1f;
            public float breath;
            public float rasp;
            public float vibratoHz;
            public float vibratoDepth;
            public float tempo = 1f;
            public string timbre;
        }

        [Serializable]
        public sealed class Clips
        {
            public Clip[] angry;
            public Clip[] happy;
            public Clip[] hurt;
            public Clip[] insult;
            public Clip[] celebrate;

            public Clip[] For(string emotion)
            {
                switch (emotion)
                {
                    case "angry": return angry;
                    case "happy": return happy;
                    case "hurt": return hurt;
                    case "insult": return insult;
                    case "celebrate": return celebrate;
                    default: return null;
                }
            }

            public void Set(string emotion, Clip[] value)
            {
                switch (emotion)
                {
                    case "angry": angry = value; break;
                    case "happy": happy = value; break;
                    case "hurt": hurt = value; break;
                    case "insult": insult = value; break;
                    case "celebrate": celebrate = value; break;
                }
            }
        }

        [Serializable]
        public sealed class Clip
        {
            public int level;
            public string file;
            public bool exists;
            /// Every recorded take of a multi-take (legacy) cell, `file` first.
            public string[] takes;
        }

        /// One fighter as the generator sees it: the asset's identity and synth
        /// parameters (0 pitch = derive from the name).
        public struct RosterEntry
        {
            public string BehaviorName;
            public float PitchHz;
            public float Tract;

            public RosterEntry(string behaviorName, float pitchHz, float tract)
            {
                BehaviorName = behaviorName;
                PitchHz = pitchHz;
                Tract = tract;
            }
        }

        public static string IdOf(string behaviorName) =>
            string.IsNullOrEmpty(behaviorName) ? string.Empty : behaviorName.ToLowerInvariant();

        /// `<id>/<emotion>-<level>.wav`, relative to ROOT.
        public static string StandardFile(string behaviorName, string emotion, int level) =>
            $"{IdOf(behaviorName)}/{emotion}-{level}.wav";

        /// `<Behavior>_<Mood>_L<level>_<take>.wav`, relative to ROOT (Mood = enum name).
        public static string LegacyFile(string behaviorName, string emotion, int level, int take) =>
            $"{behaviorName}_{Capitalise(emotion)}_L{level}_{take}.wav";

        private static string Capitalise(string text) =>
            string.IsNullOrEmpty(text) ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);

        /// A distinct, stable profile for a fighter with none in the file yet:
        /// pitch and formant scale from the character asset (the values the synth
        /// always used), the texture terms from a hash of the name.
        public static VoiceProfile DefaultProfile(string behaviorName, float pitchHz, float tract)
        {
            uint hash = Fnv(IdOf(behaviorName));
            float H(int shift) => ((hash >> shift) & 0xFF) / 255f;
            float pitch = pitchHz > 0f ? pitchHz : 92f + (int)(hash & 0x3F);
            float formantScale = 1f / Math.Max(0.5f, tract > 0f ? tract : 1f);
            return new VoiceProfile
            {
                pitchHz = Round(pitch, 1),
                formantScale = Round(formantScale, 3),
                breath = Round(0.15f + 0.25f * H(0), 2),
                rasp = Round(0.10f + 0.40f * H(8), 2),
                vibratoHz = Round(4.6f + 1.6f * H(16), 2),
                vibratoDepth = Round(0.012f + 0.02f * H(24), 3),
                tempo = Round(0.9f + 0.2f * H(4), 2),
                timbre = TimbreFor(pitch, formantScale),
            };
        }

        private static string TimbreFor(float pitchHz, float formantScale)
        {
            if (pitchHz >= 260f || formantScale >= 1.3f) return "small-high";
            if (pitchHz >= 170f) return "adult-female";
            if (pitchHz <= 100f) return "deep-male";
            return "adult-male";
        }

        /// Builds the manifest for `roster` from the WAVs present under ROOT.
        /// `files` holds paths relative to ROOT with '/' separators; matching is
        /// case-insensitive (Windows and Resources both are) but the real name is
        /// what gets written. Profiles in `previous` are KEPT (hand-tuned voices
        /// survive a regeneration); new fighters get `DefaultProfile`.
        public static Manifest Build(IList<RosterEntry> roster, ICollection<string> files,
                                     Manifest previous, string generatedUtc)
        {
            var onDisk = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (files != null)
            {
                foreach (string file in files) onDisk[file.Replace('\\', '/')] = file.Replace('\\', '/');
            }

            var characters = new List<Character>();
            var seen = new HashSet<string>();
            foreach (RosterEntry entry in roster)
            {
                string id = IdOf(entry.BehaviorName);
                if (id.Length == 0 || !seen.Add(id)) continue;
                VoiceProfile profile = FindProfile(previous, id)
                                       ?? DefaultProfile(entry.BehaviorName, entry.PitchHz, entry.Tract);
                var clips = new Clips();
                foreach (string emotion in EMOTIONS)
                {
                    var cells = new Clip[LEVELS];
                    for (int level = 1; level <= LEVELS; level++)
                    {
                        cells[level - 1] = Resolve(entry.BehaviorName, emotion, level, onDisk);
                    }
                    clips.Set(emotion, cells);
                }
                characters.Add(new Character
                {
                    id = id,
                    name = entry.BehaviorName.ToUpperInvariant(),
                    voice = profile,
                    clips = clips,
                });
            }
            characters.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
            return new Manifest { generated = generatedUtc ?? string.Empty, characters = characters.ToArray() };
        }

        private static Clip Resolve(string behaviorName, string emotion, int level, Dictionary<string, string> onDisk)
        {
            string standard = StandardFile(behaviorName, emotion, level);
            if (onDisk.TryGetValue(standard, out string real))
            {
                return new Clip { level = level, file = real, exists = true, takes = new string[0] };
            }
            var takes = new List<string>();
            for (int take = 1; take <= LEGACY_TAKES; take++)
            {
                if (onDisk.TryGetValue(LegacyFile(behaviorName, emotion, level, take), out string legacy))
                {
                    takes.Add(legacy);
                }
            }
            if (takes.Count > 0)
            {
                return new Clip { level = level, file = takes[0], exists = true, takes = takes.ToArray() };
            }
            return new Clip { level = level, file = standard, exists = false, takes = new string[0] };
        }

        private static VoiceProfile FindProfile(Manifest manifest, string id)
        {
            if (manifest?.characters == null) return null;
            foreach (Character character in manifest.characters)
            {
                if (character != null && character.id == id && character.voice != null
                    && character.voice.pitchHz > 0f)
                {
                    return character.voice;
                }
            }
            return null;
        }

        /// "N of M clips recorded; missing: ..." — the generator's summary line.
        public static string Summary(Manifest manifest, int listMissing = 12)
        {
            int total = 0, recorded = 0;
            var missing = new List<string>();
            foreach (Character character in manifest.characters)
            {
                foreach (string emotion in EMOTIONS)
                {
                    foreach (Clip clip in character.clips.For(emotion))
                    {
                        total++;
                        if (clip.exists) recorded++;
                        else missing.Add(clip.file);
                    }
                }
            }
            var text = new StringBuilder($"{recorded} of {total} clips recorded");
            if (missing.Count > 0)
            {
                text.Append("; missing: ");
                text.Append(string.Join(", ", missing.GetRange(0, Math.Min(listMissing, missing.Count))));
                if (missing.Count > listMissing) text.Append($", ... (+{missing.Count - listMissing} more)");
            }
            return text.ToString();
        }

        // ------------------------------------------------------------ writer

        /// Deterministic, diff-friendly JSON in the spec's shape (one clip per
        /// line). JsonUtility reads it back into `Manifest`.
        public static string ToJson(Manifest manifest)
        {
            var json = new StringBuilder();
            json.Append("{\n");
            Field(json, 1, "schema", Quote(manifest.schema), true);
            Field(json, 1, "game", Quote(manifest.game), true);
            Field(json, 1, "generated", Quote(manifest.generated), true);
            Field(json, 1, "root", Quote(manifest.root), true);
            Field(json, 1, "emotions", "[" + string.Join(", ", Array.ConvertAll(manifest.emotions, Quote)) + "]", true);
            Field(json, 1, "levels", "[" + string.Join(", ", Array.ConvertAll(manifest.levels, l => l.ToString(CultureInfo.InvariantCulture))) + "]", true);
            Field(json, 1, "fallback", Quote(manifest.fallback), true);
            json.Append("  \"characters\": [\n");
            for (int index = 0; index < manifest.characters.Length; index++)
            {
                Character character = manifest.characters[index];
                VoiceProfile v = character.voice;
                json.Append("    {\n");
                Field(json, 3, "id", Quote(character.id), true);
                Field(json, 3, "name", Quote(character.name), true);
                Field(json, 3, "voice",
                      "{ " + $"\"pitchHz\": {Num(v.pitchHz)}, \"formantScale\": {Num(v.formantScale)}, " +
                      $"\"breath\": {Num(v.breath)}, \"rasp\": {Num(v.rasp)}, \"vibratoHz\": {Num(v.vibratoHz)}, " +
                      $"\"vibratoDepth\": {Num(v.vibratoDepth)}, \"tempo\": {Num(v.tempo)}, \"timbre\": {Quote(v.timbre)}" +
                      " }", true);
                json.Append("      \"clips\": {\n");
                for (int e = 0; e < EMOTIONS.Length; e++)
                {
                    Clip[] cells = character.clips.For(EMOTIONS[e]) ?? new Clip[0];
                    json.Append("        ").Append(Quote(EMOTIONS[e])).Append(": [\n");
                    for (int c = 0; c < cells.Length; c++)
                    {
                        Clip clip = cells[c];
                        json.Append("          { \"level\": ").Append(clip.level)
                            .Append(", \"file\": ").Append(Quote(clip.file))
                            .Append(", \"exists\": ").Append(clip.exists ? "true" : "false");
                        if (clip.takes != null && clip.takes.Length > 1)
                        {
                            json.Append(", \"takes\": [").Append(string.Join(", ", Array.ConvertAll(clip.takes, Quote))).Append(']');
                        }
                        json.Append(" }").Append(c < cells.Length - 1 ? ",\n" : "\n");
                    }
                    json.Append("        ]").Append(e < EMOTIONS.Length - 1 ? ",\n" : "\n");
                }
                json.Append("      }\n");
                json.Append("    }").Append(index < manifest.characters.Length - 1 ? ",\n" : "\n");
            }
            json.Append("  ]\n}\n");
            return json.ToString();
        }

        private static void Field(StringBuilder json, int depth, string name, string value, bool comma)
        {
            json.Append(' ', depth * 2).Append(Quote(name)).Append(": ").Append(value).Append(comma ? ",\n" : "\n");
        }

        private static string Num(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        private static string Quote(string text)
        {
            if (text == null) return "\"\"";
            var quoted = new StringBuilder("\"");
            foreach (char ch in text)
            {
                if (ch == '"' || ch == '\\') quoted.Append('\\').Append(ch);
                else if (ch < 0x20) quoted.Append("\\u").Append(((int)ch).ToString("x4"));
                else quoted.Append(ch);
            }
            return quoted.Append('"').ToString();
        }

        private static float Round(float value, int digits) => (float)Math.Round(value, digits);

        private static uint Fnv(string text)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (char ch in text)
                {
                    hash ^= ch;
                    hash *= 16777619;
                }
                return hash;
            }
        }
    }
}
