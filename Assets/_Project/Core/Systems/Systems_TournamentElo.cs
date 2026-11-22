using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PoSumo
{
    /// TOURNAMENT-MODE Elo: one rating per fighter, moved only by bracket bouts.
    ///
    /// Deliberately SEPARATE from `Systems_CareerStats.elo` (user's choice,
    /// 2026-11-10). The career rating also moves on exhibition bouts and carries
    /// upset/streak transfers tuned for the banzuke; this one is plain Elo over
    /// the self-playing 16-fighter bracket and nothing else, so "who wins
    /// tournaments" can be read without exhibition noise. Neither is the
    /// self-play ELO on TensorBoard — do not compare any of the three.
    ///
    /// Same shape as Systems_CareerStats: static (it must outlive the arena
    /// scene loads), keyed by BEHAVIOR NAME, one small JSON in
    /// persistentDataPath, reloaded from disk on SubsystemRegistration because
    /// Enter Play Mode domain reload is off.
    ///
    /// Zero-sum: a bout moves points from loser to winner, so the pool mean stays
    /// at START for ever and a rating is distance from 1000. A walkover moves
    /// nothing.
    public static class Systems_TournamentElo
    {
        private const string FILE_NAME = "tournament_elo.json";
        public const float START = 1000f;
        /// Higher than the career ladder's 24: a bracket is only 15 bouts, and the
        /// ratings should separate within a few tournaments, not a few dozen.
        private const float K = 32f;

        [Serializable]
        private sealed class Entry
        {
            public string fighter;
            public float elo = START;
            public int bouts;
            public int titles;
        }

        [Serializable]
        private sealed class SaveFile
        {
            public int version = 1;
            public List<Entry> entries = new List<Entry>();
        }

        private static SaveFile _data = new SaveFile();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ReloadOnPlaySessionStart() => Load();

        private static string SavePath => Path.Combine(Application.persistentDataPath, FILE_NAME);

        /// Current rating; START for a fighter who has never fought a bracket bout.
        public static float Get(string fighter)
        {
            Entry entry = Find(fighter);
            return entry != null ? entry.elo : START;
        }

        public static int Titles(string fighter)
        {
            Entry entry = Find(fighter);
            return entry != null ? entry.titles : 0;
        }

        /// Applies one decided bout and returns the points that moved (winner +,
        /// loser -). Zero for a missing or mirror pairing.
        public static float RecordBout(string winner, string loser)
        {
            if (string.IsNullOrEmpty(winner) || string.IsNullOrEmpty(loser) || winner == loser) return 0f;
            Entry w = FindOrAdd(winner);
            Entry l = FindOrAdd(loser);
            float expected = 1f / (1f + Mathf.Pow(10f, (l.elo - w.elo) / 400f));
            float delta = K * (1f - expected);
            w.elo += delta;
            l.elo -= delta;
            w.bouts++;
            l.bouts++;
            Save();
            return delta;
        }

        public static void RecordTitle(string champion)
        {
            if (string.IsNullOrEmpty(champion)) return;
            FindOrAdd(champion).titles++;
            Save();
        }

        private static Entry Find(string fighter)
        {
            if (string.IsNullOrEmpty(fighter)) return null;
            List<Entry> entries = _data.entries;
            for (int index = 0; index < entries.Count; index++)
            {
                if (entries[index].fighter == fighter) return entries[index];
            }
            return null;
        }

        private static Entry FindOrAdd(string fighter)
        {
            Entry entry = Find(fighter);
            if (entry != null) return entry;
            entry = new Entry { fighter = fighter };
            _data.entries.Add(entry);
            return entry;
        }

        private static void Load()
        {
            _data = new SaveFile();
            try
            {
                if (!File.Exists(SavePath)) return;
                SaveFile file = JsonUtility.FromJson<SaveFile>(File.ReadAllText(SavePath));
                if (file != null && file.entries != null) _data = file;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Systems_TournamentElo: could not read {FILE_NAME} ({e.Message}); starting fresh.");
            }
        }

        private static void Save()
        {
            try
            {
                File.WriteAllText(SavePath, JsonUtility.ToJson(_data, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Systems_TournamentElo: could not save ({e.Message}).");
            }
        }
    }
}
