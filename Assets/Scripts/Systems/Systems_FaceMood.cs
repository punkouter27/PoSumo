using UnityEngine;

namespace PoSumo
{
    /// Drives a fighter's face through the four moods — Happy, Angry, Hurt, Insult —
    /// at five levels each, plus Neutral. The same moods and levels the VOICE uses,
    /// and driven by the same moments, so what a fighter says and what its face
    /// does can never disagree.
    ///
    /// Two inputs:
    ///   * a BASELINE from live dominance (smoothed ~2 s): winning drifts toward
    ///     Happy 1..5, losing toward Angry 1..5, Neutral in between;
    ///   * FLASHES from `Systems_FighterVoice.Expressed` — a hit taken (Hurt), a
    ///     taunt (Insult), a round won or lost, a knockdown. A match result is a
    ///     flash with infinite hold, released by a rematch.
    ///
    /// The displayed face walks one LEVEL per step, and changes mood only by
    /// passing through Neutral, so it animates rather than jumps.
    ///
    /// Art (see `Systems_FaceArt.Resolve`): a real `<Name>_<Mood>_<Level>` image
    /// wins; otherwise the nearest level of that mood; otherwise Neutral with the
    /// mood drawn procedurally — tint, squash and head tilt — scaled by however
    /// many levels the art is short. The 12 placeholder fighters (2026-11-10) have
    /// a Neutral only, so every one of their expressions is procedural.
    ///
    /// Spawned by Systems_GameMatchManager, one per fighter.
    public sealed class Systems_FaceMood : MonoBehaviour
    {
        [Tooltip("Behavior name of the fighter whose face reacts. Only used to resolve hand-placed instances — the runtime spawner assigns `fighter` directly.")]
        public string fighterBehaviorName = "Matt";
        [Tooltip("The fighter whose face reacts. Assigned by Systems_GameMatchManager when it spawns this.")]
        public Agent_Biped fighter;
        [Tooltip("Per-second rate the baseline chases live dominance.")]
        public float smoothingRate = 1.2f;
        [Tooltip("Seconds between single steps along the expression ladder.")]
        public float stepSeconds = 0.09f;
        [Tooltip("Idle flourish: after this long with nothing interesting, the face rides its current mood to 5 and back.")]
        public float idleMinSeconds = 15f;
        public float idleMaxSeconds = 30f;
        [Tooltip("Maximum idle flourishes per match.")]
        public int idleFlourishesPerMatch = 2;

        private const float FLOURISH_SECONDS = 0.9f;
        private const int LEVELS = Systems_FaceArt.LEVELS;

        private Systems_GameMatchManager _manager;
        private Systems_FightHud _hud;
        private Agent_Biped _fighter;
        private Agent_BipedBody _body;
        private bool _fighterIsA;
        private string _prefix;

        private float _smoothedDom = 50f;

        // What is on screen, and the moment it was last changed.
        private Systems_FighterMood _shownMood = Systems_FighterMood.Neutral;
        private int _shownLevel;
        private float _nextStepTime;

        private Systems_FighterMood _flashMood;
        private int _flashLevel;
        private float _flashUntil;
        private bool _flashActive;

        private float _lastInterestTime;
        private float _idleDelay;
        private int _idleFlourishes;
        private Systems_FighterMood _lastBaselineMood;

        /// Fallback for an instance placed by hand in a scene rather than spawned
        /// by the manager. Ambiguous in a mirror match, which is exactly why the
        /// spawner assigns the instance instead.
        private Agent_Biped ResolveByName()
        {
            if (_manager.wrestlerA != null && _manager.wrestlerA.behaviorName == fighterBehaviorName)
            {
                return _manager.wrestlerA;
            }
            if (_manager.wrestlerB != null && _manager.wrestlerB.behaviorName == fighterBehaviorName)
            {
                return _manager.wrestlerB;
            }
            return null;
        }

        private void Start()
        {
            _manager = FindAnyObjectByType<Systems_GameMatchManager>();
            _hud = FindAnyObjectByType<Systems_FightHud>();
            if (_manager == null) { enabled = false; return; }

            // Bound by INSTANCE, never by behaviour name — in a mirror bout the
            // name is ambiguous and both drivers would write one head.
            _fighter = fighter != null ? fighter : ResolveByName();
            if (_fighter == null) { enabled = false; return; }
            _fighterIsA = _fighter == _manager.wrestlerA;
            _body = _fighter.GetComponent<Agent_BipedBody>();
            if (_body == null) { enabled = false; return; }

            _prefix = _body.character != null ? _body.character.FaceArtPrefix : fighterBehaviorName;
            if (Systems_FaceArt.Resolve(_prefix, Systems_FighterMood.Neutral, 0, out _) == null)
            {
                // No face art at all: the body drew its plain disc. Nothing to drive.
                Debug.LogWarning($"Systems_FaceMood ({_prefix}): no '{_prefix}_Neutral' in the FaceAtlas — face stays static.");
                enabled = false;
                return;
            }

            _manager.MatchReset += OnMatchReset;
            _manager.RoundStarted += MarkInterest;

            MarkInterest();
            Show(Systems_FighterMood.Neutral, 0);
        }

        private void OnEnable()
        {
            Systems_FighterVoice.Expressed += OnExpressed;
        }

        private void OnDisable()
        {
            // Static event: a missed unsubscribe keeps a dead scene's face alive.
            Systems_FighterVoice.Expressed -= OnExpressed;
            if (_manager != null)
            {
                _manager.MatchReset -= OnMatchReset;
                _manager.RoundStarted -= MarkInterest;
            }
        }

        private void MarkInterest()
        {
            _lastInterestTime = Time.unscaledTime;
            _idleDelay = Random.Range(idleMinSeconds, idleMaxSeconds);
        }

        private void OnExpressed(Agent_Biped who, Systems_FighterMood mood, int level, float holdSeconds)
        {
            if (who != _fighter || _fighter == null) return;
            // A held face (the match result) is only replaced by another hold.
            if (_flashActive && float.IsPositiveInfinity(_flashUntil) && !float.IsPositiveInfinity(holdSeconds)) return;
            _flashMood = mood;
            _flashLevel = Mathf.Clamp(level, 1, LEVELS);
            _flashActive = true;
            _flashUntil = float.IsPositiveInfinity(holdSeconds) ? float.PositiveInfinity : Time.unscaledTime + holdSeconds;
            MarkInterest();
        }

        private void OnMatchReset()
        {
            _flashActive = false;
            _smoothedDom = 50f;
            _idleFlourishes = 0;
            MarkInterest();
        }

        private void Update()
        {
            if (_fighter == null || _body == null) return;

            float dom = _hud != null ? (_fighterIsA ? _hud.DominanceA : _hud.DominanceB) : 50f;
            float t = 1f - Mathf.Exp(-smoothingRate * Time.deltaTime);
            _smoothedDom = Mathf.Lerp(_smoothedDom, dom, t);

            if (_flashActive && Time.unscaledTime >= _flashUntil) _flashActive = false;

            Baseline(_smoothedDom, out Systems_FighterMood baseMood, out int baseLevel);
            if (baseMood != _lastBaselineMood)
            {
                _lastBaselineMood = baseMood;
                MarkInterest();
            }

            // Idle flourish: a dull stretch earns a quick ride to 5 in the current
            // baseline direction and back.
            if (!_flashActive && _manager.RoundActive
                && _idleFlourishes < idleFlourishesPerMatch
                && Time.unscaledTime - _lastInterestTime >= _idleDelay)
            {
                _idleFlourishes++;
                OnExpressed(_fighter, _smoothedDom >= 50f ? Systems_FighterMood.Happy : Systems_FighterMood.Angry,
                            LEVELS, FLOURISH_SECONDS);
            }

            Systems_FighterMood targetMood = _flashActive ? _flashMood : baseMood;
            int targetLevel = _flashActive ? _flashLevel : baseLevel;
            if (targetMood == Systems_FighterMood.Neutral) targetLevel = 0;

            if (Time.unscaledTime < _nextStepTime) return;
            if (_shownMood == targetMood && _shownLevel == targetLevel) return;

            // One level per step. A mood change walks DOWN to Neutral first, then up.
            if (_shownMood != targetMood && _shownLevel > 0)
            {
                Show(_shownMood, _shownLevel - 1);
            }
            else if (_shownMood != targetMood)
            {
                Show(targetMood, targetLevel > 0 ? 1 : 0);
            }
            else
            {
                Show(_shownMood, _shownLevel + (_shownLevel < targetLevel ? 1 : -1));
            }
            _nextStepTime = Time.unscaledTime + stepSeconds;
        }

        /// Dominance -> baseline. Seven-point bands either side of an even fight,
        /// so a clear lead reaches Happy 3 and a rout reaches 5.
        private static void Baseline(float dom, out Systems_FighterMood mood, out int level)
        {
            if (dom >= 57f)
            {
                mood = Systems_FighterMood.Happy;
                level = Mathf.Clamp(1 + Mathf.FloorToInt((dom - 57f) / 7f), 1, LEVELS);
                return;
            }
            if (dom <= 43f)
            {
                mood = Systems_FighterMood.Angry;
                level = Mathf.Clamp(1 + Mathf.FloorToInt((43f - dom) / 7f), 1, LEVELS);
                return;
            }
            mood = Systems_FighterMood.Neutral;
            level = 0;
        }

        private void Show(Systems_FighterMood mood, int level)
        {
            if (level <= 0) mood = Systems_FighterMood.Neutral;
            _shownMood = mood;
            _shownLevel = mood == Systems_FighterMood.Neutral ? 0 : level;

            Sprite sprite = Systems_FaceArt.Resolve(_prefix, mood, _shownLevel, out int levelOfArt);
            if (sprite == null) return;
            _body.SetHeadSprite(sprite);

            // Whatever intensity the art does not already carry is drawn on top.
            // Art drawn at a HIGHER level than asked (nearest-level fallback) gets
            // a negative residual, which eases the procedural terms back.
            float residual = mood == Systems_FighterMood.Neutral ? 0f : _shownLevel - levelOfArt;
            if (Mathf.Approximately(residual, 0f)) return;
            Procedural(mood, residual, out Color tint, out float sx, out float sy, out float tilt);
            _body.SetHeadExpression(tint, sx, sy, tilt);
        }

        /// The procedural expression for `levels` of a mood (may be fractional or
        /// negative). Each mood is a distinct silhouette as well as a colour,
        /// because at gameplay size a head is ~100 px and colour alone is not read:
        ///   Happy   warm, a little wider, chin lifting
        ///   Angry   flushed red, compressed, chin DOWN and driving forward
        ///   Hurt    pale and cold, drawn long, head snapping BACK
        ///   Insult  sallow-violet, chin high — looking down the nose
        private static void Procedural(Systems_FighterMood mood, float levels,
                                       out Color tint, out float sx, out float sy, out float tilt)
        {
            float l = levels;
            switch (mood)
            {
                case Systems_FighterMood.Happy:
                    tint = new Color(1f, 1f - 0.02f * l, 1f - 0.07f * l);
                    sx = 1f + 0.03f * l; sy = 1f - 0.015f * l; tilt = 2f * l;
                    break;
                case Systems_FighterMood.Angry:
                    tint = new Color(1f, 1f - 0.09f * l, 1f - 0.10f * l);
                    sx = 1f + 0.025f * l; sy = 1f - 0.03f * l; tilt = -2.5f * l;
                    break;
                case Systems_FighterMood.Hurt:
                    tint = new Color(1f - 0.07f * l, 1f - 0.035f * l, 1f - 0.005f * l);
                    sx = 1f - 0.02f * l; sy = 1f + 0.03f * l; tilt = 3.5f * l;
                    break;
                case Systems_FighterMood.Insult:
                    tint = new Color(1f - 0.03f * l, 1f - 0.07f * l, 1f - 0.01f * l);
                    sx = 1f + 0.015f * l; sy = 1f; tilt = 4f * l;
                    break;
                default:
                    tint = Color.white; sx = 1f; sy = 1f; tilt = 0f;
                    break;
            }
            tint.r = Mathf.Clamp01(tint.r);
            tint.g = Mathf.Clamp01(tint.g);
            tint.b = Mathf.Clamp01(tint.b);
            tint.a = 1f;
        }
    }
}
