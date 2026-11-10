using System.Collections.Generic;
using UnityEngine;

namespace PoSumo
{
    /// Dynamic wear on the dohyo: the clay records the fight.
    ///
    /// Two kinds of mark, both driven by what the physics actually did:
    ///
    ///  - SCUFF streaks where a LOADED foot slides. Same signal Systems_FootScrape
    ///    hears — slip speed along the mat x foot load — so a fighter being driven
    ///    back toward the rim leaves a trail exactly where the scrape was audible.
    ///    One streak per few centimetres of slide, stretched along the direction
    ///    of travel and darker the harder it was pressed.
    ///  - DENTS where a body part (not a foot) lands on the clay hard enough to
    ///    matter, from Sensor_Impact's contact point.
    ///
    /// Like Systems_RingBlood, marks are stored in the ARENA's local space in a
    /// static list that survives the scene load between bouts and is cleared by
    /// the NEW TOURNAMENT reset, so the mat wears in over a bracket. Renderers are
    /// pooled in a ring and the oldest mark is recycled past the cap, so a long
    /// bracket costs a bounded number of objects and no Destroy churn.
    ///
    /// Presentation only, no brain affected. Spawned behind `enableClayWear`.
    public sealed class Systems_ClayWear : MonoBehaviour
    {
        [SerializeField] private int _maxMarks = 700;
        [Tooltip("Planted-foot slide speed (m/s) below which no streak is laid. Matches the floor of Systems_FootScrape's ramp, so a mark is never laid that was not heard.")]
        [SerializeField] private float _slipMinSpeed = 0.5f;
        [Tooltip("Foot load (fraction of body weight) below which a sliding foot is too light to mark the clay.")]
        [SerializeField] private float _minLoad = 0.15f;
        [Tooltip("Metres of slide between two streaks.")]
        [SerializeField] private float _streakSpacing = 0.12f;
        [Tooltip("Body-on-clay impact speed (m/s) above which a dent is left.")]
        [SerializeField] private float _dentMinSpeed = 2f;

        private const int FIGHTER_COUNT = 2;
        private const int FEET_PER_FIGHTER = 2;
        private const int MARK_SORTING = -1;   // on the clay, under the fighters — same layer as the blood

        private static readonly Color ScuffColour = new Color(0.27f, 0.17f, 0.10f, 1f);
        private static readonly Color DentColour = new Color(0.22f, 0.14f, 0.09f, 1f);

        private struct Mark
        {
            public float X, Y, Width, Height, Alpha;
            public bool Dent;
        }

        private static readonly List<Mark> Store = new List<Mark>();
        private static Sprite _markSprite;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ClearOnPlaySessionStart()
        {
            Store.Clear();
            _markSprite = null;
        }

        /// Called by the tournament reset beside Systems_RingBlood.ClearAll.
        public static void ClearAll() => Store.Clear();

        public static int MarkCount => Store.Count;

        private readonly List<SpriteRenderer> _pool = new List<SpriteRenderer>();
        private int _nextPoolSlot;
        private readonly Agent_Biped[] _fighters = new Agent_Biped[FIGHTER_COUNT];
        private readonly Agent_BipedBody[] _bodies = new Agent_BipedBody[FIGHTER_COUNT];
        private readonly float[] _slideAccum = new float[FIGHTER_COUNT * FEET_PER_FIGHTER];
        private Systems_SumoArena _arena;
        private Transform _arenaTransform;
        private Systems_GameMatchManager _manager;

        private void Start()
        {
            _manager = FindAnyObjectByType<Systems_GameMatchManager>();
            _arena = FindAnyObjectByType<Systems_SumoArena>();
            if (_manager == null || _arena == null)
            {
                enabled = false;
                return;
            }
            _arenaTransform = _arena.transform;
            _fighters[0] = _manager.wrestlerA;
            _fighters[1] = _manager.wrestlerB;
            for (int fighterIndex = 0; fighterIndex < FIGHTER_COUNT; fighterIndex++)
            {
                _bodies[fighterIndex] = _fighters[fighterIndex] != null
                    ? _fighters[fighterIndex].GetComponent<Agent_BipedBody>() : null;
            }

            // Replay this tournament's wear onto the freshly loaded arena.
            int first = Mathf.Max(0, Store.Count - _maxMarks);
            for (int storeIndex = first; storeIndex < Store.Count; storeIndex++)
            {
                Paint(Store[storeIndex]);
            }
        }

        private void OnEnable() => Sensor_Impact.AnyImpact += OnImpact;

        private void OnDisable() => Sensor_Impact.AnyImpact -= OnImpact;

        private void FixedUpdate()
        {
            if (_manager == null || !_manager.RoundActive)
            {
                return;
            }
            float dt = Time.fixedDeltaTime;
            for (int fighterIndex = 0; fighterIndex < FIGHTER_COUNT; fighterIndex++)
            {
                Agent_BipedBody body = _bodies[fighterIndex];
                Agent_Biped fighter = _fighters[fighterIndex];
                if (body == null || fighter == null)
                {
                    continue;
                }
                float groundY = fighter.arenaGroundY;
                TrackFoot(fighterIndex * FEET_PER_FIGHTER, body.FootNearAttached && body.FootDownNear,
                          body.FootNear, body.FootLoadNear, groundY, dt);
                TrackFoot(fighterIndex * FEET_PER_FIGHTER + 1, body.FootFarAttached && body.FootDownFar,
                          body.FootFar, body.FootLoadFar, groundY, dt);
            }
        }

        private void TrackFoot(int slot, bool planted, Rigidbody2D foot, float load, float groundY, float dt)
        {
            if (!planted || foot == null || load < _minLoad)
            {
                _slideAccum[slot] = 0f;
                return;
            }
            float slip = Mathf.Abs(foot.linearVelocity.x);
            if (slip < _slipMinSpeed)
            {
                _slideAccum[slot] = 0f;
                return;
            }
            _slideAccum[slot] += slip * dt;
            if (_slideAccum[slot] < _streakSpacing)
            {
                return;
            }
            _slideAccum[slot] = 0f;

            float press = Mathf.Clamp01(load / 0.5f) * Mathf.Clamp01(slip / 2f);
            // Sized against a live capture (2026-11-10): the mat is seen edge-on,
            // so a mark must reach down over the front lip to read at all. The
            // first pass (2.5-4 cm tall, every 5 cm of slide) hit the 700-mark cap
            // inside 13 bouts and read as a hairline.
            Stamp(foot.position.x, groundY, Random.Range(0.14f, 0.24f) * (1f + press),
                  Random.Range(0.05f, 0.08f), Mathf.Lerp(0.18f, 0.45f, press), dent: false);
        }

        private void OnImpact(Sensor_Impact sensor, Collision2D collision)
        {
            if (sensor.isFoot || collision.rigidbody != null || collision.contactCount == 0)
            {
                return;
            }
            float speed = collision.relativeVelocity.magnitude;
            if (speed < _dentMinSpeed || _manager == null)
            {
                return;
            }
            Vector2 point = collision.GetContact(0).point;
            float weight = Mathf.Clamp01((speed - _dentMinSpeed) / 4f);
            Agent_Biped owner = sensor.owner != null ? sensor.owner.GetComponent<Agent_Biped>() : null;
            float groundY = owner != null ? owner.arenaGroundY : point.y;
            Stamp(point.x, groundY, Mathf.Lerp(0.25f, 0.55f, weight), Mathf.Lerp(0.07f, 0.12f, weight),
                  Mathf.Lerp(0.3f, 0.55f, weight), dent: true);
        }

        private void Stamp(float worldX, float groundY, float width, float height, float alpha, bool dent)
        {
            if (_arenaTransform == null)
            {
                return;
            }
            // On the mat only: a mark laid out over the crowd floor is a mark on
            // nothing anyone is looking at.
            if (Mathf.Abs(worldX - _arenaTransform.position.x) > _arena.CurrentHalfWidth)
            {
                return;
            }
            Vector3 local = _arenaTransform.InverseTransformPoint(new Vector3(worldX, groundY, 0f));
            var mark = new Mark
            {
                X = local.x, Y = local.y - height * 0.5f,
                Width = width, Height = height, Alpha = alpha, Dent = dent,
            };
            Store.Add(mark);
            if (Store.Count > _maxMarks)
            {
                Store.RemoveAt(0);
            }
            Paint(mark);
        }

        private void Paint(Mark mark)
        {
            SpriteRenderer renderer;
            if (_pool.Count < _maxMarks)
            {
                var go = new GameObject("ClayWear");
                go.transform.SetParent(_arenaTransform, false);
                renderer = go.AddComponent<SpriteRenderer>();
                renderer.sprite = MarkSprite();
                renderer.sortingOrder = MARK_SORTING;
                _pool.Add(renderer);
            }
            else
            {
                renderer = _pool[_nextPoolSlot];
                _nextPoolSlot = (_nextPoolSlot + 1) % _pool.Count;
                if (renderer == null)
                {
                    return;
                }
            }
            Transform markTransform = renderer.transform;
            markTransform.localPosition = new Vector3(mark.X, mark.Y, -0.015f);
            markTransform.localRotation = Quaternion.identity;
            // The sprite is 1 x 0.25 units (64x16 at 64 ppu), so y is scaled x4
            // to make Height a height in metres.
            markTransform.localScale = new Vector3(mark.Width, mark.Height * 4f, 1f);
            Color colour = mark.Dent ? DentColour : ScuffColour;
            colour.a = mark.Alpha;
            renderer.color = colour;
        }

        /// A soft horizontal smear: an ellipse with a feathered edge and a little
        /// grain, so a run of them reads as a dragged trail, not a row of pills.
        private static Sprite MarkSprite()
        {
            if (_markSprite != null)
            {
                return _markSprite;
            }
            const int W = 64;
            const int H = 16;
            var texture = new Texture2D(W, H, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "PoSumo_ClayWear",
            };
            var rng = new System.Random(4111);
            for (int rowIndex = 0; rowIndex < H; rowIndex++)
            {
                for (int columnIndex = 0; columnIndex < W; columnIndex++)
                {
                    float u = (columnIndex + 0.5f) / W * 2f - 1f;
                    float v = (rowIndex + 0.5f) / H * 2f - 1f;
                    float distance = Mathf.Sqrt(u * u + v * v);
                    float alpha = Mathf.Clamp01(1f - distance);
                    float grain = 0.7f + 0.3f * (float)rng.NextDouble();
                    texture.SetPixel(columnIndex, rowIndex, new Color(1f, 1f, 1f, alpha * alpha * grain));
                }
            }
            texture.Apply();
            _markSprite = Sprite.Create(texture, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), W,
                                        0, SpriteMeshType.FullRect);
            return _markSprite;
        }
    }
}
