using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PoSumo
{
    /// TOURNAMENT-STANDARD Elo: one rating per fighter, moved by every completed
    /// bout between two rated fighters — bracket bouts (Systems_TournamentReporter)
    /// AND, since 2026-11-22 (demo-Elo spec), exhibition / CPU-v-CPU bouts outside
    /// the bracket (Systems_ExhibitionElo). Demo mode itself plays the bracket, so
    /// its bouts were already rated. One store, no separate demo table.
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
        /// START 1000 / K 32 / persisted is the Po tournament standard.
        public const float K = 32f;

        /// Points a winner rated `winnerElo` takes from a loser rated `loserElo`.
        /// Pure, so the tests check the formula without touching the save file.
        public static float Delta(float winnerElo, float loserElo)
        {
            float expected = 1f / (1f + Mathf.Pow(10f, (loserElo - winnerElo) / 400f));
            return K * (1f - expected);
        }

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
        public static float RecordBout(string winner, string loser) => RecordResult(winner, loser, 1f);

        /// Points A gains (negative = loses) for scoring `scoreA` (1 win, 0.5
        /// draw, 0 loss) against B. Pure.
        public static float Exchange(float ratingA, float ratingB, float scoreA)
        {
            float expectedA = 1f / (1f + Mathf.Pow(10f, (ratingB - ratingA) / 400f));
            return K * (scoreA - expectedA);
        }

        /// Applies one completed bout, A v B, A scoring `scoreA` (1 / 0.5 / 0), and
        /// saves at once. Returns A's change (B's is its negative). A fighter never
        /// plays itself: a mirror or missing pairing records nothing.
        public static float RecordResult(string a, string b, float scoreA)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b) || a == b) return 0f;
            Entry ea = FindOrAdd(a);
            Entry eb = FindOrAdd(b);
            float delta = Exchange(ea.elo, eb.elo, Mathf.Clamp01(scoreA));
            ea.elo += delta;
            eb.elo -= delta;
            ea.bouts++;
            eb.bouts++;
            Save();
            return delta;
        }

        private static bool _memoryOnly;

        /// For tests: ratings live in memory only — nothing is read from or written
        /// to tournament_elo.json until the returned scope is disposed, which puts
        /// the real ratings back.
        public static IDisposable InMemoryScope()
        {
            SaveFile saved = _data;
            bool wasMemory = _memoryOnly;
            _data = new SaveFile();
            _memoryOnly = true;
            return new Scope(() =>
            {
                _data = saved;
                _memoryOnly = wasMemory;
            });
        }

        private sealed class Scope : IDisposable
        {
            private Action _onDispose;
            public Scope(Action onDispose) => _onDispose = onDispose;

            public void Dispose()
            {
                _onDispose?.Invoke();
                _onDispose = null;
            }
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
            if (_memoryOnly) return;
            WriteFile();
        }

        /// Separate from Save so the in-memory path never touches the serializer.
        private static void WriteFile()
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
