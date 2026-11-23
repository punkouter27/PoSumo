using UnityEngine;

namespace PoSumo
{
    /// Runtime view of `voices.json` (`Systems_VoiceManifest`): loaded ONCE per
    /// play session, then asked per (fighter, emotion, level, take) whether a
    /// recorded WAV exists and, if so, which Resources path plays.
    ///
    /// Never requests a file the manifest says is missing. A missing or invalid
    /// manifest means procedural voices for everybody, with one warning.
    public static class Systems_VoiceLibrary
    {
        private static Systems_VoiceManifest.Manifest _manifest;
        private static bool _loaded;

        /// Domain reload is off in this project: forget the last session's copy.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            _manifest = null;
            _loaded = false;
        }

        public static Systems_VoiceManifest.Manifest Manifest
        {
            get
            {
                if (_loaded) return _manifest;
                _loaded = true;
                TextAsset asset = Resources.Load<TextAsset>(Systems_VoiceManifest.RESOURCE_PATH);
                if (asset == null)
                {
                    Debug.LogWarning("Systems_VoiceLibrary: no voices.json in Resources/" +
                                     Systems_VoiceManifest.RESOURCE_PATH + " — every voice line is procedural. " +
                                     "Run PoSumo > Voice > Regenerate voices.json.");
                    return null;
                }
                try
                {
                    _manifest = JsonUtility.FromJson<Systems_VoiceManifest.Manifest>(asset.text);
                }
                catch (System.Exception e)
                {
                    _manifest = null;
                    Debug.LogWarning($"Systems_VoiceLibrary: voices.json is unreadable ({e.Message}) — every voice line is procedural.");
                    return null;
                }
                if (_manifest == null || _manifest.schema != Systems_VoiceManifest.SCHEMA || _manifest.characters == null)
                {
                    _manifest = null;
                    Debug.LogWarning("Systems_VoiceLibrary: voices.json is not a po-voice-manifest/1 file — every voice line is procedural.");
                }
                return _manifest;
            }
        }

        public static Systems_VoiceManifest.Character Find(string behaviorName)
        {
            Systems_VoiceManifest.Manifest manifest = Manifest;
            if (manifest == null) return null;
            string id = Systems_VoiceManifest.IdOf(behaviorName);
            foreach (Systems_VoiceManifest.Character character in manifest.characters)
            {
                if (character != null && character.id == id) return character;
            }
            return null;
        }

        /// The fighter's synth voice: its manifest profile, else derived from the
        /// character asset exactly as the generator would.
        public static Systems_VoiceSynth.Voice VoiceFor(Agent_CharacterDefinition character, string behaviorName)
        {
            Systems_VoiceManifest.Character entry = Find(behaviorName);
            if (entry?.voice != null && entry.voice.pitchHz > 0f)
            {
                return Systems_VoiceSynth.FromProfile(entry.id, entry.voice);
            }
            return Systems_VoiceSynth.VoiceFor(character, behaviorName);
        }

        /// Resources path (no extension) of the recorded line for this request,
        /// or null when the manifest says there is none — then synthesise.
        /// `take` (1-based) picks among a multi-take cell's recordings.
        public static string RecordedPath(string behaviorName, Systems_FighterMood mood, int level, int take)
        {
            Systems_VoiceManifest.Character entry = Find(behaviorName);
            if (entry?.clips == null) return null;
            Systems_VoiceManifest.Clip[] cells = entry.clips.For(Systems_FighterVoice.EmotionName(mood));
            if (cells == null) return null;
            foreach (Systems_VoiceManifest.Clip clip in cells)
            {
                if (clip == null || clip.level != level || !clip.exists) continue;
                string file = clip.takes != null && clip.takes.Length > 0
                    ? clip.takes[(Mathf.Max(1, take) - 1) % clip.takes.Length]
                    : clip.file;
                return ToResourcePath(file);
            }
            return null;
        }

        public static string ToResourcePath(string file)
        {
            if (string.IsNullOrEmpty(file)) return null;
            string path = file.EndsWith(".wav", System.StringComparison.OrdinalIgnoreCase)
                ? file.Substring(0, file.Length - 4)
                : file;
            return Systems_VoiceManifest.RESOURCE_ROOT + path;
        }
    }
}
