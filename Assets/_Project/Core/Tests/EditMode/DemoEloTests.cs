using NUnit.Framework;

namespace PoSumo.Tests
{
    /// Demo / exhibition Elo (spec of 2026-11-22): completed CPU-v-CPU bouts outside
    /// the bracket move the same tournament Elo, aborted ones do not, nothing is
    /// counted twice, a fighter never plays itself. Every test runs inside
    /// `Systems_TournamentElo.InMemoryScope`, so tournament_elo.json is never touched.
    public sealed class DemoEloTests
    {
        private System.IDisposable _scope;

        [SetUp]
        public void SetUp() => _scope = Systems_TournamentElo.InMemoryScope();

        [TearDown]
        public void TearDown() => _scope.Dispose();

        [Test]
        public void ACompletedExhibitionBoutUpdatesBothRatings()
        {
            var bout = new Systems_ExhibitionElo(bracketBout: false);
            bout.MatchStarted();
            Assert.IsTrue(bout.TryRecord("Matt", "Nick", 1f, out float delta));
            Assert.AreEqual(16f, delta, 1e-4f);
            Assert.AreEqual(1016f, Systems_TournamentElo.Get("Matt"), 1e-3f);
            Assert.AreEqual(984f, Systems_TournamentElo.Get("Nick"), 1e-3f);
        }

        [Test]
        public void AnAbortedBoutRecordsNothing()
        {
            var bout = new Systems_ExhibitionElo(bracketBout: false);
            bout.MatchStarted();
            // QUIT MATCH / scene change: the match never ends, then the next starts.
            bout.MatchStarted();
            Assert.IsFalse(bout.Counted);
            Assert.AreEqual(Systems_TournamentElo.START, Systems_TournamentElo.Get("Matt"));
            Assert.AreEqual(Systems_TournamentElo.START, Systems_TournamentElo.Get("Nick"));
        }

        [Test]
        public void AMatchIsNeverCountedTwiceButARematchCounts()
        {
            var bout = new Systems_ExhibitionElo(bracketBout: false);
            bout.MatchStarted();
            Assert.IsTrue(bout.TryRecord("Matt", "Nick", 1f, out _));
            Assert.IsFalse(bout.TryRecord("Matt", "Nick", 1f, out _), "replayed MatchEnded");
            Assert.AreEqual(1016f, Systems_TournamentElo.Get("Matt"), 1e-3f);

            bout.MatchStarted();   // REMATCH
            Assert.IsTrue(bout.TryRecord("Matt", "Nick", 0f, out float delta));
            Assert.Less(delta, 0f);
        }

        [Test]
        public void ADrawIsHalfAPointEach()
        {
            var bout = new Systems_ExhibitionElo(bracketBout: false);
            Assert.IsTrue(bout.TryRecord("Matt", "Nick", 0.5f, out float even));
            Assert.AreEqual(0f, even, 1e-4f, "equal ratings, draw: nothing moves");
            Assert.AreEqual(-8.31f, Systems_TournamentElo.Exchange(1200f, 1000f, 0.5f), 0.01f);
            Assert.Less(Systems_TournamentElo.Exchange(1200f, 1000f, 0.5f), 0f, "favourite loses points on a draw");
            Assert.Greater(Systems_TournamentElo.Exchange(1000f, 1200f, 0.5f), 0f);
        }

        [Test]
        public void AFighterNeverPlaysItself()
        {
            var bout = new Systems_ExhibitionElo(bracketBout: false);
            Assert.IsFalse(bout.TryRecord("Kim", "Kim", 1f, out float delta));
            Assert.AreEqual(0f, delta);
            Assert.AreEqual(Systems_TournamentElo.START, Systems_TournamentElo.Get("Kim"));
            Assert.AreEqual(0f, Systems_TournamentElo.RecordResult("Kim", "Kim", 1f));
        }

        [Test]
        public void ABracketBoutIsLeftToTheReporter()
        {
            var bout = new Systems_ExhibitionElo(bracketBout: true);
            Assert.IsFalse(bout.TryRecord("Matt", "Nick", 1f, out _));
            Assert.AreEqual(Systems_TournamentElo.START, Systems_TournamentElo.Get("Matt"));
        }

        [Test]
        public void ZeroSumWithStandardK()
        {
            float win = Systems_TournamentElo.Exchange(1000f, 1000f, 1f);
            Assert.AreEqual(16f, win, 1e-4f);
            Assert.AreEqual(Systems_TournamentElo.Delta(1100f, 1000f), Systems_TournamentElo.Exchange(1100f, 1000f, 1f), 1e-4f);
        }
    }
}
