using UnityEngine;

namespace PoSumo
{
    /// Low bars laid across ONE walk agent's lane, re-placed every episode.
    ///
    /// WHY, after five failed reward retrains: the crawl survives because nothing
    /// in the walk lane REQUIRES the swing foot to leave the clay. A body that
    /// drags its feet pays nothing for it. A bar a few centimetres high across
    /// the path changes that by geometry rather than by price: a dragged foot
    /// stops against it, and a shin or knee that touches it is a non-foot part on
    /// a static collider — `IsDown`, i.e. the ordinary walk fall terminal. The
    /// only way past is to lift the foot, which is the posture the reward could
    /// never buy.
    ///
    /// Training only in practice: the bars exist only when the
    /// walk_obstacle_height environment parameter is above 0, which no game
    /// session ever sets. Owned by `Agent_Biped`; one instance per walk agent,
    /// built lazily, never destroyed (a handful of static colliders per agent).
    ///
    /// The six walkers in each SCN_TRAIN_* scene run on disjoint 5 m segments
    /// with 6 m gaps, so bars placed strictly between an agent's start and its
    /// target never sit in another walker's path.
    public sealed class Systems_WalkObstacles
    {
        /// Bar depth along the lane, metres — a tawara-like bale.
        private const float BAR_WIDTH = 0.10f;
        /// Keep the first bar this far ahead of the spawn and the last this far
        /// short of the target, so the start pose and the graduation line are clear.
        private const float START_CLEARANCE = 1.0f;
        private const float END_CLEARANCE = 0.6f;
        /// Minimum spacing between bars: about one stride.
        private const float MIN_SPACING = 0.6f;
        public const int MAX_BARS = 4;

        private readonly GameObject[] _bars = new GameObject[MAX_BARS];
        private readonly float[] _xs = new float[MAX_BARS];
        private GameObject _holder;

        /// Lays `count` bars of `height` metres between startX and targetX on the
        /// mat at groundY, on `layer`. height <= 0 or count <= 0 hides them all.
        public void Place(float startX, float targetX, float groundY, float height, int count, int layer)
        {
            count = Mathf.Clamp(count, 0, MAX_BARS);
            float dir = Mathf.Sign(targetX - startX);
            float from = startX + dir * START_CLEARANCE;
            float to = targetX - dir * END_CLEARANCE;
            float span = (to - from) * dir;
            if (height <= 0f || count == 0 || span <= 0f)
            {
                Hide();
                return;
            }
            // Fit as many as the spacing allows.
            count = Mathf.Min(count, 1 + Mathf.FloorToInt(span / MIN_SPACING));

            // Even slots with jitter inside each slot: random, never overlapping.
            float slot = span / count;
            for (int barIndex = 0; barIndex < count; barIndex++)
            {
                float jitter = Random.Range(0f, Mathf.Max(0f, slot - MIN_SPACING * 0.5f));
                _xs[barIndex] = from + dir * (barIndex * slot + jitter);
            }

            EnsureBuilt();
            for (int barIndex = 0; barIndex < MAX_BARS; barIndex++)
            {
                GameObject bar = _bars[barIndex];
                bool on = barIndex < count;
                bar.SetActive(on);
                if (!on) continue;
                bar.layer = layer;
                bar.transform.position = new Vector3(_xs[barIndex], groundY + height * 0.5f, 0f);
                bar.transform.localScale = new Vector3(BAR_WIDTH, height, 1f);
            }
            Physics2D.SyncTransforms();
        }

        public void Hide()
        {
            for (int barIndex = 0; barIndex < MAX_BARS; barIndex++)
            {
                if (_bars[barIndex] != null) _bars[barIndex].SetActive(false);
            }
        }

        private void EnsureBuilt()
        {
            if (_holder != null) return;
            _holder = new GameObject("WalkObstacles");
            for (int barIndex = 0; barIndex < MAX_BARS; barIndex++)
            {
                var bar = new GameObject("Bale" + barIndex);
                bar.transform.SetParent(_holder.transform, false);
                // A unit box scaled to the bar: the drawn edge is the colliding edge.
                bar.AddComponent<BoxCollider2D>().size = Vector2.one;
                var art = bar.AddComponent<SpriteRenderer>();
                art.sprite = Agent_BipedBody.SquareSprite();
                art.color = new Color(0.55f, 0.42f, 0.25f);
                bar.SetActive(false);
                _bars[barIndex] = bar;
            }
        }
    }
}
