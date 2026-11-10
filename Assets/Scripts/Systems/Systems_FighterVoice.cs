using UnityEngine;

namespace PoSumo
{
    /// One fighter's voice — and the one place that decides WHEN a fighter emotes.
    ///
    /// Four moods (`Systems_FighterMood`: Happy, Angry, Hurt, Insult), five levels
    /// each (1 = mildest, 5 = strongest), five different lines per level: 100 lines
    /// per fighter (spec of 2026-11-10).
    ///
    /// Every emotional moment is decided here and announced on the static
    /// `Expressed` event BEFORE any voice gating, so `Systems_FaceMood` shows the
    /// same mood at the same level even when the voice is still busy with its last
    /// line. Companions do not reach into each other; they meet on that event, the
    /// way the audio meets the voice on `LineStarted`.
    ///
    /// What drives each mood:
    ///   Hurt    a hit received (level from impact speed), a knockdown (4),
    ///           losing the match by going down (5)
    ///   Angry   live dominance sinking past thresholds, losing a round (4),
    ///           losing the match by ring-out (5)
    ///   Happy   live dominance climbing past thresholds, winning a round (4, or 5
    ///           on a ring-out streak), winning the match (5)
    ///   Insult  roughly every insultInterval seconds while a round is live; the
    ///           fighter ahead taunts harder
    ///
    /// Lines resolve by naming convention, recorded first:
    ///     Resources/Audio/Voice/<Behavior>_<Mood>_L<level>_<variant>.wav
    /// and any line with no WAV is synthesised on first use by
    /// `Systems_VoiceSynth` — so every fighter, including the 12 placeholder
    /// fighters with no recordings at all, has a complete voice, and a real
    /// recording replaces a placeholder one file at a time. The old recordings
    /// (Happy / Sad / Insult, one per level) are variant 1; Sad became Hurt.
    ///
    /// Spawned at runtime by Systems_GameMatchManager, one per fighter.
    public sealed class Systems_FighterVoice : MonoBehaviour
    {
        private const string VOICE_PATH = "Audio/Voice/";

        [Tooltip("Only used to resolve hand-placed instances — the runtime spawner assigns `fighter` directly.")]
        public string fighterBehaviorName = "Matt";
        [Tooltip("The fighter this voice belongs to. Assigned by Systems_GameMatchManager when it spawns this.")]
        public Agent_Biped fighter;

        [Tooltip("Pitch multiplier. The second of two identical fighters is dropped so a mirror bout does not sound like one man arguing with himself.")]
        public float pitchScale = 1f;
        [Range(0f, 1f)] public float volume = 0.9f;

        [Header("Hurt (being hit)")]
        [Tooltip("Impact speed (m/s) that produces the mildest hurt line.")]
        public float hitMinSpeed = 2.6f;
        [Tooltip("Impact speed (m/s) that produces the strongest hurt line.")]
        public float hitMaxSpeed = 8.5f;

        [Header("Happy / Angry (dominance)")]
        [Tooltip("Dominance (0-100) above which he starts gloating.")]
        public float happyDominance = 62f;
        [Tooltip("Dominance at which the strongest in-round happy line fires.")]
        public float happyDominanceMax = 92f;
        [Tooltip("Dominance must fall back to this distance from even (50) before another happy or angry line can fire, so a steady lead does not loop.")]
        public float rearmDistance = 5f;

        [Header("Insults")]
        public float insultInterval = 8f;
        [Tooltip("Random +/- spread on the interval so the cadence is not metronomic.")]
        public float insultJitter = 1.6f;

        [Header("Pacing")]
        [Tooltip("Minimum gap between ANY two lines from this fighter.")]
        public float minGapSeconds = 1.4f;
        [Tooltip("How long the face holds a flashed expression.")]
        public float expressionSeconds = 1.5f;

        public const int LEVELS = 5;
        public const int VARIANTS = 5;
        private const int MOODS = 5;   // Systems_FighterMood incl. Neutral (unused slot 0)

        private const float LPF_OPEN = 22000f;
        private const float LPF_SLOWMO = 900f;

        /// Fired when a voice line ACTUALLY starts playing, carrying the clip length.
        /// Systems_MatchAudio ducks the beds under speech.
        public static event System.Action<float> LineStarted;

        /// Fired at every emotional moment, whether or not a line plays:
        /// (fighter, mood, level 1-5, hold seconds — +Infinity holds until a rematch).
        /// Systems_FaceMood draws it.
        public static event System.Action<Agent_Biped, Systems_FighterMood, int, float> Expressed;

        private AudioLowPassFilter _lowPass;
        private Systems_VoiceGains _gains;
        private AudioSource _source;
        private Systems_GameMatchManager _manager;
        private Systems_FightHud _hud;
        private Agent_Biped _fighter;
        private Agent_BipedBody _body;
        private bool _fighterIsA;

        private Systems_VoiceSynth.Voice _voice;
        private string _prefix;
        // [mood][level-1][variant-1]: null = not looked up yet.
        private readonly AudioClip[][][] _bank = new AudioClip[MOODS][][];
        private readonly bool[][][] _synthesised = new bool[MOODS][][];
        private readonly int[] _lastVariant = new int[MOODS];

        private float _nextAllowedTime;
        private float _nextInsultTime;
        private bool _happyArmed = true;
        private bool _angryArmed = true;
        private bool _matchDecided;
        private bool _wasDown;
        private float _downRearmTime;
        private int _ringOutStreak;

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
            if (_manager == null)
            {
                enabled = false;
                return;
            }

            // Bound by INSTANCE, never by behaviour name (mirror bouts).
            _fighter = fighter != null ? fighter : ResolveByName();
            if (_fighter == null)
            {
                enabled = false;
                return;
            }
            _fighterIsA = _fighter == _manager.wrestlerA;
            _body = _fighter.GetComponent<Agent_BipedBody>();

            Agent_CharacterDefinition character = _body != null ? _body.character : null;
            _prefix = character != null ? character.behaviorName : fighterBehaviorName;
            _voice = Systems_VoiceSynth.VoiceFor(character, _prefix);
            for (int mood = 0; mood < MOODS; mood++)
            {
                _bank[mood] = new AudioClip[LEVELS][];
                _synthesised[mood] = new bool[LEVELS][];
                for (int level = 0; level < LEVELS; level++)
                {
                    _bank[mood][level] = new AudioClip[VARIANTS];
                    _synthesised[mood][level] = new bool[VARIANTS];
                }
            }

            // Regenerated by PoSumo > Normalize Voice Levels. Synthesised lines are
            // not in it and play at gain 1, which is what they are normalised to.
            _gains = Resources.Load<Systems_VoiceGains>("VoiceGains");

            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.loop = false;
            // 2D with manual pan, matching Systems_MatchAudio: true 3D falloff
            // pumps with the follow camera's zoom in an orthographic game.
            _source.spatialBlend = 0f;
            Systems_AudioMix.AddArenaReverb(gameObject);
            _lowPass = gameObject.AddComponent<AudioLowPassFilter>();
            _lowPass.cutoffFrequency = LPF_OPEN;

            _manager.RoundEnded += OnRoundEnded;
            _manager.MatchEnded += OnMatchEnded;
            _manager.MatchReset += OnMatchReset;
            _manager.RoundStarted += OnRoundStarted;

            ScheduleInsult();
        }

        private void OnEnable()
        {
            Sensor_Impact.AnyImpact += OnImpact;
        }

        private void OnDisable()
        {
            Sensor_Impact.AnyImpact -= OnImpact;
            if (_manager != null)
            {
                _manager.RoundEnded -= OnRoundEnded;
                _manager.MatchEnded -= OnMatchEnded;
                _manager.MatchReset -= OnMatchReset;
                _manager.RoundStarted -= OnRoundStarted;
            }
        }

        private void OnDestroy()
        {
            // Runtime-created clips are not scene objects; free the ones made here.
            for (int mood = 0; mood < MOODS; mood++)
            {
                if (_bank[mood] == null) continue;
                for (int level = 0; level < LEVELS; level++)
                {
                    for (int variant = 0; variant < VARIANTS; variant++)
                    {
                        if (_synthesised[mood][level][variant] && _bank[mood][level][variant] != null)
                        {
                            Destroy(_bank[mood][level][variant]);
                        }
                    }
                }
            }
        }

        // ------------------------------------------------------------- events

        private void OnRoundStarted()
        {
            _happyArmed = true;
            _angryArmed = true;
            ScheduleInsult();
        }

        private void OnRoundEnded(Agent_Biped winner, Agent_Biped loser)
        {
            bool ringOut = _manager != null && _manager.LastOutcome == Systems_GameMatchManager.RoundOutcome.RingOut;
            _ringOutStreak = winner == _fighter && ringOut ? _ringOutStreak + 1 : 0;
            if (_matchDecided)
            {
                return;   // the match-end line owns that moment
            }
            if (winner == _fighter)
            {
                // Two ring-outs running: the top read, held twice as long.
                if (_ringOutStreak >= 2) Express(Systems_FighterMood.Happy, 5, expressionSeconds * 2f);
                else Express(Systems_FighterMood.Happy, 4, expressionSeconds);
            }
            else if (loser == _fighter)
            {
                Express(Systems_FighterMood.Angry, 4, expressionSeconds);
            }
        }

        private void OnMatchEnded(Agent_Biped winner)
        {
            _matchDecided = true;
            // The strongest read in the set is reserved for taking or losing the
            // match, and the face holds it until a rematch. Going down hurts;
            // being driven out makes you furious.
            if (winner == _fighter)
            {
                Express(Systems_FighterMood.Happy, 5, float.PositiveInfinity, force: true);
                return;
            }
            bool wentDown = _manager != null && _manager.LastOutcome != Systems_GameMatchManager.RoundOutcome.RingOut;
            Express(wentDown ? Systems_FighterMood.Hurt : Systems_FighterMood.Angry, 5, float.PositiveInfinity, force: true);
        }

        private void OnMatchReset()
        {
            _matchDecided = false;
            _happyArmed = true;
            _angryArmed = true;
            _ringOutStreak = 0;
            ScheduleInsult();
        }

        private void OnImpact(Sensor_Impact sensor, Collision2D collision)
        {
            // Only hits this fighter RECEIVES from the opponent.
            if (_body == null || sensor == null || sensor.owner != _body || _matchDecided)
            {
                return;
            }
            var other = collision.collider.GetComponentInParent<Agent_BipedBody>();
            if (other == null || other == _body)
            {
                return;
            }
            float speed = collision.relativeVelocity.magnitude;
            if (speed < hitMinSpeed)
            {
                return;
            }
            Express(Systems_FighterMood.Hurt, LevelFrom(speed, hitMinSpeed, hitMaxSpeed), expressionSeconds * 0.7f);
        }

        // -------------------------------------------------------------- tick

        private void Update()
        {
            // Above the early returns: a slow-mo finish is exactly when
            // `_matchDecided` is true, and the filter exists for that sequence.
            if (_lowPass != null)
            {
                float scale = Mathf.Clamp01(Time.timeScale);
                float slow = 1f - Mathf.Clamp01(Mathf.InverseLerp(0.2f, 0.85f, scale));
                _lowPass.cutoffFrequency = Mathf.Lerp(LPF_OPEN, LPF_SLOWMO, slow);
            }

            if (_manager == null || _matchDecided || _fighter == null)
            {
                return;
            }

            bool roundLive = _manager.RoundActive;

            // Knockdown: debounced, mid-round only.
            bool down = _fighter.IsDown;
            if (roundLive && down && !_wasDown && Time.unscaledTime >= _downRearmTime)
            {
                Express(Systems_FighterMood.Hurt, 4, expressionSeconds * 0.8f);
                _downRearmTime = Time.unscaledTime + 2f;
            }
            _wasDown = down;

            if (!roundLive)
            {
                return;
            }

            float dominance = _hud != null ? (_fighterIsA ? _hud.DominanceA : _hud.DominanceB) : 50f;

            // Insults only while the round is live, so they never talk over the
            // countdown, the gong or the result card. The fighter ahead taunts
            // harder: level leans on dominance, with a random spread.
            if (Time.time >= _nextInsultTime)
            {
                ScheduleInsult();
                int lean = Mathf.RoundToInt(Mathf.InverseLerp(30f, 80f, dominance) * 2f);  // 0..2
                Express(Systems_FighterMood.Insult, Mathf.Clamp(Random.Range(1, 4) + lean, 1, LEVELS), expressionSeconds);
            }

            if (_hud == null)
            {
                return;
            }

            // One line per surge: he has to come back toward even before gloating
            // (or fuming) again, otherwise a steady lead loops the set forever.
            float angryDominance = 100f - happyDominance;
            if (_happyArmed && dominance >= happyDominance)
            {
                _happyArmed = false;
                Express(Systems_FighterMood.Happy, LevelFrom(dominance, happyDominance, happyDominanceMax), expressionSeconds);
            }
            else if (!_happyArmed && dominance < happyDominance - rearmDistance)
            {
                _happyArmed = true;
            }

            if (_angryArmed && dominance <= angryDominance)
            {
                _angryArmed = false;
                Express(Systems_FighterMood.Angry, LevelFrom(angryDominance - dominance + happyDominance,
                                                             happyDominance, happyDominanceMax), expressionSeconds);
            }
            else if (!_angryArmed && dominance > angryDominance + rearmDistance)
            {
                _angryArmed = true;
            }
        }

        private void ScheduleInsult()
        {
            _nextInsultTime = Time.time + insultInterval + Random.Range(-insultJitter, insultJitter);
        }

        /// Map a measured value onto 1..5.
        private static int LevelFrom(float value, float min, float max)
        {
            float t = Mathf.Clamp01((value - min) / Mathf.Max(0.01f, max - min));
            return Mathf.Clamp(Mathf.FloorToInt(t * LEVELS) + 1, 1, LEVELS);
        }

        // ------------------------------------------------------------ playing

        /// Announce the moment (always), then try to say it (gated).
        private void Express(Systems_FighterMood mood, int level, float holdSeconds, bool force = false)
        {
            level = Mathf.Clamp(level, 1, LEVELS);
            Expressed?.Invoke(_fighter, mood, level, holdSeconds);
            Say(mood, level, force);
        }

        private void Say(Systems_FighterMood mood, int level, bool force)
        {
            if (_source == null)
            {
                return;
            }
            // One voice per fighter: a new line cuts the old one rather than
            // overlapping it. `force` is for the match-decided line, which wins.
            if (!force && (Time.time < _nextAllowedTime || _source.isPlaying))
            {
                return;
            }
            AudioClip clip = Clip(mood, level, PickVariant(mood));
            if (clip == null)
            {
                return;
            }
            _nextAllowedTime = Time.time + Mathf.Max(minGapSeconds, clip.length * 0.6f);
            _source.pitch = pitchScale;
            _source.panStereo = Pan();
            _source.clip = clip;
            _source.volume = volume * (_gains != null ? _gains.GainFor(clip.name) : 1f)
                           * Systems_AudioMix.VoiceLevel;
            _source.Play();
            LineStarted?.Invoke(clip.length);
        }

        /// A different variant from the last line of the same mood, so the same
        /// shout does not fire twice running.
        private int PickVariant(Systems_FighterMood mood)
        {
            int last = _lastVariant[(int)mood];
            int variant = Random.Range(1, VARIANTS + 1);
            if (variant == last)
            {
                variant = variant % VARIANTS + 1;
            }
            _lastVariant[(int)mood] = variant;
            return variant;
        }

        /// Recorded WAV if there is one, otherwise synthesised — each looked up or
        /// built once, on first use.
        private AudioClip Clip(Systems_FighterMood mood, int level, int variant)
        {
            AudioClip[] slot = _bank[(int)mood][level - 1];
            AudioClip clip = slot[variant - 1];
            if (clip != null)
            {
                return clip;
            }
            string name = $"{_prefix}_{mood}_L{level}_{variant}";
            clip = Resources.Load<AudioClip>(VOICE_PATH + name);
            if (clip == null)
            {
                clip = Systems_VoiceSynth.Create(name, _voice, mood, level, variant);
                _synthesised[(int)mood][level - 1][variant - 1] = true;
            }
            slot[variant - 1] = clip;
            return clip;
        }

        /// Pan by position in the ring, relative to the arena centre rather than
        /// the camera, so a fighter on the left stays on the left.
        private float Pan()
        {
            if (_fighter == null || _manager == null)
            {
                return 0f;
            }
            float centerX = _manager.transform.position.x;
            float half = Mathf.Max(0.01f, _manager.ringHalfWidth);
            return Mathf.Clamp((_fighter.TorsoX - centerX) / half, -1f, 1f) * 0.6f;
        }
    }
}
