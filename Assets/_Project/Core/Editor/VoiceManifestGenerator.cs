using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PoSumo.EditorTools
{
    /// *PoSumo → Voice → Regenerate voices.json*: rewrites
    /// `Assets/_Project/Data/Resources/Audio/Voice/voices.json` from the roster
    /// (every `Agent_CharacterDefinition` under RL_Models) and the WAVs actually on
    /// disk — 5 emotions x 5 levels per fighter, `exists` flags, the real file a
    /// legacy recording lives in — keeping any voice profile already in the file.
    /// Logs "N of M clips recorded; missing: ...".
    ///
    /// It also runs by itself whenever a WAV under the voice folder is imported,
    /// moved or deleted (cheap: a directory listing and one small file), so
    /// dropping in `matt/angry-3.wav` is all it takes to add a recording.
    public sealed class VoiceManifestGenerator : AssetPostprocessor
    {
        private const string ROSTER_FOLDER = "Assets/_Project/RL_Models";
        private static bool _queued;

        [MenuItem("PoSumo/Voice/Regenerate voices.json")]
        public static void RegenerateFromMenu() => Regenerate(verbose: true);

        /// Returns the summary line. Writes only when something other than the
        /// timestamp changed, so re-running is not a diff.
        public static string Regenerate(bool verbose)
        {
            string root = Systems_VoiceManifest.ROOT;
            string manifestPath = root + "/" + Systems_VoiceManifest.FILE_NAME;

            var roster = new List<Systems_VoiceManifest.RosterEntry>();
            foreach (string guid in AssetDatabase.FindAssets("t:Agent_CharacterDefinition", new[] { ROSTER_FOLDER }))
            {
                var character = AssetDatabase.LoadAssetAtPath<Agent_CharacterDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (character == null || string.IsNullOrEmpty(character.behaviorName)) continue;
                roster.Add(new Systems_VoiceManifest.RosterEntry(character.behaviorName, character.voicePitchHz, character.voiceTract));
            }

            var files = new List<string>();
            if (Directory.Exists(root))
            {
                foreach (string wav in Directory.GetFiles(root, "*.wav", SearchOption.AllDirectories))
                {
                    files.Add(wav.Replace('\\', '/').Substring(root.Length + 1));
                }
            }

            Systems_VoiceManifest.Manifest previous = null;
            string oldText = File.Exists(manifestPath) ? File.ReadAllText(manifestPath) : null;
            if (oldText != null)
            {
                try
                {
                    previous = JsonUtility.FromJson<Systems_VoiceManifest.Manifest>(oldText);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"VoiceManifestGenerator: existing voices.json unreadable ({e.Message}); rebuilding profiles.");
                }
            }

            // Same timestamp first, to see whether anything else changed.
            Systems_VoiceManifest.Manifest manifest = Systems_VoiceManifest.Build(
                roster, files, previous, previous?.generated ?? string.Empty);
            string text = Systems_VoiceManifest.ToJson(manifest);
            if (text != oldText)
            {
                manifest.generated = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
                text = Systems_VoiceManifest.ToJson(manifest);
                Directory.CreateDirectory(root);
                File.WriteAllText(manifestPath, text);
                AssetDatabase.ImportAsset(manifestPath);
            }

            string summary = $"VOICE MANIFEST: {manifest.characters.Length} fighters · " +
                             Systems_VoiceManifest.Summary(manifest);
            if (verbose) Debug.Log(summary);
            return summary;
        }

        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (_queued || !(TouchesVoice(imported) || TouchesVoice(deleted) || TouchesVoice(moved) || TouchesVoice(movedFrom)))
            {
                return;
            }
            // Deferred: writing and importing an asset from inside the import
            // callback re-enters the AssetDatabase.
            _queued = true;
            EditorApplication.delayCall += () =>
            {
                _queued = false;
                Regenerate(verbose: true);
            };
        }

        private static bool TouchesVoice(string[] paths)
        {
            if (paths == null) return false;
            foreach (string path in paths)
            {
                if (path.StartsWith(Systems_VoiceManifest.ROOT + "/", StringComparison.OrdinalIgnoreCase)
                    && path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
