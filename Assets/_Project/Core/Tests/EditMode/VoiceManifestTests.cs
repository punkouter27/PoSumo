using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace PoSumo.Tests
{
    /// The voice manifest (`voices.json`) and the advanced procedural imitation,
    /// per the Po voice-manifest spec (2026-11-22). Read-only: nothing here writes
    /// the manifest or any WAV.
    public sealed class VoiceManifestTests
    {
        private static readonly Regex StandardFile =
            new Regex(@"^[a-z0-9]+/(angry|happy|hurt|insult|celebrate)-[1-5]\.wav$");
        private static readonly Regex LegacyFile =
            new Regex(@"^[A-Za-z0-9]+_(Angry|Happy|Hurt|Insult|Celebrate)_L[1-5]_[1-5]\.wav$");

        /// From the editor's own data path, not the working directory.
        private static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);

        private static Systems_VoiceManifest.Manifest LoadShipped()
        {
            var asset = Resources.Load<TextAsset>(Systems_VoiceManifest.RESOURCE_PATH);
            Assert.IsNotNull(asset, "voices.json missing from Resources/" + Systems_VoiceManifest.RESOURCE_PATH);
            var manifest = JsonUtility.FromJson<Systems_VoiceManifest.Manifest>(asset.text);
            Assert.AreEqual(Systems_VoiceManifest.SCHEMA, manifest.schema);
            return manifest;
        }

        private static List<Agent_CharacterDefinition> Roster()
        {
            var roster = new List<Agent_CharacterDefinition>();
            foreach (string guid in AssetDatabase.FindAssets("t:Agent_CharacterDefinition", new[] { "Assets/_Project/RL_Models" }))
            {
                var character = AssetDatabase.LoadAssetAtPath<Agent_CharacterDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (character != null) roster.Add(character);
            }
            return roster;
        }

        [Test]
        public void ManifestCoversEveryRosterFighterFiveByFive()
        {
            Systems_VoiceManifest.Manifest manifest = LoadShipped();
            List<Agent_CharacterDefinition> roster = Roster();
            Assert.GreaterOrEqual(roster.Count, 2);
            CollectionAssert.AreEqual(Systems_VoiceManifest.EMOTIONS, manifest.emotions);

            foreach (Agent_CharacterDefinition fighter in roster)
            {
                string id = Systems_VoiceManifest.IdOf(fighter.behaviorName);
                Systems_VoiceManifest.Character entry = System.Array.Find(manifest.characters, c => c.id == id);
                Assert.IsNotNull(entry, $"{fighter.behaviorName} is not in voices.json — run PoSumo > Voice > Regenerate voices.json");
                Assert.IsNotNull(entry.voice, id + " has no voice profile");
                Assert.Greater(entry.voice.pitchHz, 0f, id);
                Assert.Greater(entry.voice.formantScale, 0f, id);
                foreach (string emotion in Systems_VoiceManifest.EMOTIONS)
                {
                    Systems_VoiceManifest.Clip[] cells = entry.clips.For(emotion);
                    Assert.AreEqual(5, cells.Length, $"{id} {emotion}");
                    for (int level = 1; level <= 5; level++)
                    {
                        Systems_VoiceManifest.Clip clip = cells[level - 1];
                        Assert.AreEqual(level, clip.level);
                        bool standard = clip.file == Systems_VoiceManifest.StandardFile(fighter.behaviorName, emotion, level);
                        bool legacy = LegacyFile.IsMatch(clip.file) && clip.file.StartsWith(fighter.behaviorName + "_");
                        Assert.IsTrue(StandardFile.IsMatch(clip.file) && standard || legacy, $"{id} {emotion} {level}: {clip.file}");
                        Assert.AreEqual(File.Exists(Path.Combine(ProjectRoot, Systems_VoiceManifest.ROOT, clip.file)), clip.exists,
                                        $"{clip.file}: exists flag out of date — regenerate voices.json");
                    }
                }
            }
        }

        [Test]
        public void StandardBeatsLegacyAndMissingStaysStandard()
        {
            var roster = new[] { new Systems_VoiceManifest.RosterEntry("Matt", 98f, 0.94f) };
            var files = new[] { "matt/angry-2.wav", "Matt_Angry_L2_1.wav", "Matt_Happy_L1_1.wav", "Matt_Happy_L1_3.wav" };
            var manifest = Systems_VoiceManifest.Build(roster, files, null, "t");
            var matt = manifest.characters[0];
            Assert.AreEqual("matt", matt.id);
            Assert.AreEqual("matt/angry-2.wav", matt.clips.angry[1].file);
            Assert.IsTrue(matt.clips.angry[1].exists);
            Assert.AreEqual("Matt_Happy_L1_1.wav", matt.clips.happy[0].file);
            CollectionAssert.AreEqual(new[] { "Matt_Happy_L1_1.wav", "Matt_Happy_L1_3.wav" }, matt.clips.happy[0].takes);
            Assert.AreEqual("matt/celebrate-5.wav", matt.clips.celebrate[4].file);
            Assert.IsFalse(matt.clips.celebrate[4].exists);
            StringAssert.StartsWith("2 of 25 clips recorded", Systems_VoiceManifest.Summary(manifest));
        }

        [Test]
        public void JsonRoundTripsAndKeepsHandTunedProfiles()
        {
            var roster = new[] { new Systems_VoiceManifest.RosterEntry("Kim", 196f, 1.14f) };
            var first = Systems_VoiceManifest.Build(roster, new string[0], null, "t");
            first.characters[0].voice.rasp = 0.77f;
            var parsed = JsonUtility.FromJson<Systems_VoiceManifest.Manifest>(Systems_VoiceManifest.ToJson(first));
            Assert.AreEqual(0.77f, parsed.characters[0].voice.rasp, 1e-4f);
            Assert.AreEqual("kim/insult-3.wav", parsed.characters[0].clips.insult[2].file);
            var second = Systems_VoiceManifest.Build(roster, new string[0], parsed, "t");
            Assert.AreEqual(0.77f, second.characters[0].voice.rasp, 1e-4f, "a regeneration kept the profile");
        }

        // ---- the procedural imitation ---------------------------------------

        private static Systems_VoiceSynth.Voice VoiceOf(string name, float pitch, float tract) =>
            Systems_VoiceSynth.FromProfile(name, Systems_VoiceManifest.DefaultProfile(name, pitch, tract));

        private static double Rms(float[] samples)
        {
            double sum = 0;
            foreach (float s in samples) sum += s * (double)s;
            return System.Math.Sqrt(sum / samples.Length);
        }

        [Test]
        public void EveryEmotionIsAudibleAndLevelFiveIsLouderAndLonger()
        {
            Systems_VoiceSynth.Voice voice = VoiceOf("Matt", 98f, 0.94f);
            foreach (Systems_FighterMood mood in Systems_FighterVoice.STANDARD_EMOTIONS)
            {
                for (int take = 1; take <= 5; take++)
                {
                    float[] mild = Systems_VoiceSynth.Synthesize(voice, mood, 1, take);
                    float[] strong = Systems_VoiceSynth.Synthesize(voice, mood, 5, take);
                    Assert.Greater(Rms(mild), 0.01, $"{mood} L1 take {take} is silent");
                    Assert.Greater(Rms(strong), Rms(mild), $"{mood} take {take}: level 5 not louder");
                    Assert.Greater(strong.Length, mild.Length, $"{mood} take {take}: level 5 not longer");
                }
            }
        }

        [Test]
        public void DifferentFightersSoundDifferentAndTheSameRequestRepeats()
        {
            Systems_VoiceSynth.Voice matt = VoiceOf("Matt", 98f, 0.94f);
            Systems_VoiceSynth.Voice maya = VoiceOf("Maya", 310f, 0.7f);
            foreach (Systems_FighterMood mood in Systems_FighterVoice.STANDARD_EMOTIONS)
            {
                CollectionAssert.AreEqual(Systems_VoiceSynth.Synthesize(matt, mood, 3, 2),
                                          Systems_VoiceSynth.Synthesize(matt, mood, 3, 2), mood + " not deterministic");
                CollectionAssert.AreNotEqual(Systems_VoiceSynth.Synthesize(matt, mood, 3, 2),
                                             Systems_VoiceSynth.Synthesize(maya, mood, 3, 2), mood + ": two fighters identical");
            }
        }
    }
}
