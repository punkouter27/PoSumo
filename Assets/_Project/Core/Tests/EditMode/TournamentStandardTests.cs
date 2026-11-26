using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace PoSumo.Tests
{
    /// EditMode tests for the eight-slot bracket
    /// seeded by rating on the 1v8, 4v5, 2v7, 3v6 chart,
    /// BYEs on the bottom seeds, draw to the higher seed, Elo 1000 / K 32, and the
    /// five-emotion x five-level voice matrix.
    ///
    /// Nothing here touches disk: seeding is driven through the rating-function
    /// overload (never `Systems_TournamentElo.Get`), no bout is reported (that
    /// would rewrite bracket.json and tournament_elo.json), and voice samples are
    /// synthesised without creating an AudioClip.
    public sealed class TournamentStandardTests
    {
        private readonly List<Agent_CharacterDefinition> _made = new List<Agent_CharacterDefinition>();

        [TearDown]
        public void TearDown()
        {
            Systems_TournamentState.ResetAll();
            foreach (Agent_CharacterDefinition character in _made)
            {
                Object.DestroyImmediate(character);
            }
            _made.Clear();
        }

        /// `count` fighters F1..Fn whose rating falls with their number, handed
        /// over in REVERSE so the seeding has to sort them.
        private Agent_CharacterDefinition[] Roster(int count, out Dictionary<Agent_CharacterDefinition, float> ratings)
        {
            ratings = new Dictionary<Agent_CharacterDefinition, float>();
            var roster = new Agent_CharacterDefinition[count];
            for (int index = 0; index < count; index++)
            {
                var character = ScriptableObject.CreateInstance<Agent_CharacterDefinition>();
                character.behaviorName = "F" + (index + 1);
                _made.Add(character);
                ratings[character] = 1400f - index * 10f;
                roster[count - 1 - index] = character;
            }
            return roster;
        }

        private static string NameAt(int slot)
        {
            Agent_CharacterDefinition character = Systems_TournamentState.GetSeed(slot);
            return character != null ? character.behaviorName : "BYE";
        }

        [Test]
        public void FirstRoundFollowsTheStandardChart()
        {
            int[][] expected =
            {
                new[] { 1, 8 }, new[] { 4, 5 }, new[] { 2, 7 }, new[] { 3, 6 },
            };
            Assert.AreEqual(8, Systems_TournamentState.SEED_COUNT);
            for (int match = 0; match < expected.Length; match++)
            {
                Assert.AreEqual(expected[match][0], Systems_TournamentState.SeedOfSlot(match * 2), "match " + match);
                Assert.AreEqual(expected[match][1], Systems_TournamentState.SeedOfSlot(match * 2 + 1), "match " + match);
            }
        }

        [Test]
        public void FullFieldIsSeededByRating()
        {
            Agent_CharacterDefinition[] roster = Roster(16, out var ratings);
            Systems_TournamentState.AutoSeed(roster, character => ratings[character]);

            Assert.AreEqual(8, Systems_TournamentState.FighterCount);
            Assert.AreEqual(0, Systems_TournamentState.ByeCount);
            for (int slot = 0; slot < 8; slot++)
            {
                Assert.AreEqual("F" + Systems_TournamentState.SeedOfSlot(slot), NameAt(slot), "slot " + slot);
            }
        }

        [Test]
        public void ByesTakeTheBottomSeedsAndAdvanceTheirOpponents()
        {
            Agent_CharacterDefinition[] roster = Roster(5, out var ratings);
            Systems_TournamentState.AutoSeed(roster, character => ratings[character]);

            Assert.AreEqual(5, Systems_TournamentState.FighterCount);
            Assert.AreEqual(3, Systems_TournamentState.ByeCount);
            for (int slot = 0; slot < 8; slot++)
            {
                int seed = Systems_TournamentState.SeedOfSlot(slot);
                Assert.AreEqual(seed <= 5 ? "F" + seed : "BYE", NameAt(slot), "slot " + slot);
            }

            // 1 v BYE(8): seed 1 goes through unfought.
            Assert.AreEqual("F1", Systems_TournamentState.GetAdvancing(0).behaviorName);
            // 4 v 5 is a real quarterfinal, not decided in advance.
            Assert.IsNull(Systems_TournamentState.GetAdvancing(1));
            Assert.AreEqual("F2", Systems_TournamentState.GetAdvancing(2).behaviorName);
            Assert.AreEqual("F3", Systems_TournamentState.GetAdvancing(3).behaviorName);
            Assert.AreEqual(4, Systems_TournamentState.BoutCount);
        }

        [Test]
        public void EqualRatingsKeepRosterOrder()
        {
            Agent_CharacterDefinition[] roster = Roster(3, out _);
            Agent_CharacterDefinition[] ordered = Systems_TournamentState.SeedOrder(roster, _ => Systems_TournamentElo.START);
            CollectionAssert.AreEqual(roster, ordered);
        }

        [Test]
        public void ADrawGoesToTheHigherSeed()
        {
            Agent_CharacterDefinition[] roster = Roster(16, out var ratings);
            Systems_TournamentState.AutoSeed(roster, character => ratings[character]);

            Agent_CharacterDefinition seed4 = Systems_TournamentState.GetSeed(2);
            Agent_CharacterDefinition seed5 = Systems_TournamentState.GetSeed(3);
            Assert.AreSame(seed4, Systems_TournamentState.HigherSeed(seed4, seed5));
            Assert.AreSame(seed4, Systems_TournamentState.HigherSeed(seed5, seed4));
            Assert.AreSame(seed5, Systems_TournamentState.HigherSeed(null, seed5));
        }

        [Test]
        public void EloStartsAt1000WithK32()
        {
            Assert.AreEqual(1000f, Systems_TournamentElo.START);
            Assert.AreEqual(32f, Systems_TournamentElo.K);
            Assert.AreEqual(16f, Systems_TournamentElo.Delta(1000f, 1000f), 1e-4f);
            // An upset pays more than an expected win; the two sum to K.
            float upset = Systems_TournamentElo.Delta(1000f, 1200f);
            float expected = Systems_TournamentElo.Delta(1200f, 1000f);
            Assert.Greater(upset, expected);
            Assert.AreEqual(32f, upset + expected, 1e-3f);
        }

        [Test]
        public void VoiceMatrixIsFiveEmotionsByFiveLevelsWithStandardNames()
        {
            CollectionAssert.AreEqual(
                new[] { "angry", "happy", "hurt", "insult", "celebrate" },
                System.Array.ConvertAll(Systems_FighterVoice.STANDARD_EMOTIONS, Systems_FighterVoice.EmotionName));
            Assert.AreEqual(5, Systems_FighterVoice.LEVELS);
            Assert.AreEqual("Audio/Voice/matt/celebrate-3",
                            Systems_FighterVoice.StandardName("Matt", Systems_FighterMood.Celebrate, 3));
            Assert.AreEqual("Audio/Voice/Matt_Happy_L2_1",
                            Systems_FighterVoice.LegacyName("Matt", Systems_FighterMood.Happy, 2, 1));

            // Every cell has a procedural fallback.
            var voice = new Systems_VoiceSynth.Voice(120f, 1f, 42);
            foreach (Systems_FighterMood mood in Systems_FighterVoice.STANDARD_EMOTIONS)
            {
                for (int level = 1; level <= Systems_FighterVoice.LEVELS; level++)
                {
                    float[] samples = Systems_VoiceSynth.Synthesize(voice, mood, level, 1);
                    Assert.Greater(samples.Length, 1000, $"{mood} {level}");
                }
            }
        }

        [Test]
        public void CelebrateHasItsOwnHighEnergyProfile()
        {
            var voice = new Systems_VoiceSynth.Voice(120f, 1f, 42);
            float[] celebrate = Systems_VoiceSynth.Synthesize(voice, Systems_FighterMood.Celebrate, 5, 2);
            float[] happy = Systems_VoiceSynth.Synthesize(voice, Systems_FighterMood.Happy, 5, 2);
            CollectionAssert.AreNotEqual(happy, celebrate);
            float[] celebrateMild = Systems_VoiceSynth.Synthesize(voice, Systems_FighterMood.Celebrate, 1, 2);
            float[] happyMild = Systems_VoiceSynth.Synthesize(voice, Systems_FighterMood.Happy, 1, 2);
            Assert.Greater(celebrateMild.Length, happyMild.Length, "even a mild whoop outlasts a mild cheer");
        }
    }
}
