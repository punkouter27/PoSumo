using UnityEngine;

namespace PoSumo
{
    /// Rates ONE arena's bouts in the tournament-standard Elo
    /// (`Systems_TournamentElo`, 1000 / K 32, tournament_elo.json) — the demo-Elo
    /// rule of 2026-11-22: every COMPLETED CPU-v-CPU bout between rated fighters
    /// counts, in the same store the bracket uses.
    ///
    /// PoSumo is 1v1, so the rule is plain Elo: win 1, loss 0, draw 0.5 each (no
    /// higher-seed rule outside the bracket). A fighter never plays itself: a
    /// mirror bout records nothing. Only a decided match counts — QUIT MATCH, a
    /// scene change or an unfinished bout never reaches `TryRecord` — and each
    /// match is counted at most once (`MatchStarted` re-arms it for a rematch).
    ///
    /// A BRACKET bout (every demo-mode bout is one: demo mode plays the bracket)
    /// is already rated by Systems_TournamentReporter, so here it is only marked
    /// counted. Plain class, no Unity objects, so the rule is unit-testable.
    public sealed class Systems_ExhibitionElo
    {
        public readonly bool BracketBout;
        private bool _counted;

        public Systems_ExhibitionElo(bool bracketBout)
        {
            BracketBout = bracketBout;
        }

        /// True once this match has been rated (or handed to the bracket).
        public bool Counted => _counted;

        /// A new match (first one, or a rematch) may be rated once.
        public void MatchStarted() => _counted = false;

        public void MarkCounted() => _counted = true;

        /// Rates the completed match A v B (A scored `scoreA`: 1 / 0.5 / 0) and
        /// saves at once. False — nothing recorded — for a bracket bout, a match
        /// already counted, a mirror or an unrated (brainless / missing) fighter.
        public bool TryRecord(string a, string b, float scoreA, out float deltaA)
        {
            deltaA = 0f;
            if (_counted) return false;
            _counted = true;
            if (BracketBout) return false;
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b) || a == b) return false;
            deltaA = Systems_TournamentElo.RecordResult(a, b, scoreA);
            return true;
        }

        /// "ELO  MATT 1016 +16  ·  NICK 984 -16" for the result card.
        public static string Describe(string a, float deltaA, string b, float deltaB)
        {
            return $"ELO  {Side(a, deltaA)}  ·  {Side(b, deltaB)}";
        }

        private static string Side(string fighter, float delta)
        {
            int rating = Mathf.RoundToInt(Systems_TournamentElo.Get(fighter));
            int change = Mathf.RoundToInt(delta);
            string sign = change < 0 ? "-" : "+";   // ASCII: the project ships no font asset
            return $"{fighter.ToUpperInvariant()} {rating} {sign}{Mathf.Abs(change)}";
        }
    }
}
