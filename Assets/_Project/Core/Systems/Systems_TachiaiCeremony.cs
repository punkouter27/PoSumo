using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using UnityEngine.UIElements;

namespace PoSumo
{
    /// The tachiai face-off as a TIMELINE (2026-11-10): the camera beats that the
    /// referee's countdown used to hand-code per digit — in on A's face, in on B's,
    /// a tight two-shot "gaze lock" with a tale-of-the-tape card, then the release
    /// wide — sequenced by a `PlayableDirector` over one `Systems_CeremonyTrack`.
    /// After the release it times each fighter OFF THE LINE and posts who was
    /// first, which is the moment a tachiai is actually won.
    ///
    /// Presentation only. It moves the camera and draws two labels; it never
    /// touches a body, an observation or the clock — the referee's countdown is
    /// still what freezes and releases the fighters, so turning this off
    /// (`GameTuning.enableTachiaiCeremony`) restores the old per-digit camera
    /// exactly.
    ///
    /// The referee owns this one directly (`Systems_GameMatchManager._ceremony`):
    /// it starts it from StartCountdown and stops issuing its own camera beats
    /// while `OwnsCamera` is true. That is the one place two systems would
    /// otherwise both steer the camera, so the hand-off is explicit rather than
    /// two writers racing.
    ///
    /// Timeline runs on GAME time (it pauses with the game). Each clip re-issues
    /// its shot every frame, because camera deadlines are realtime.
    public sealed class Systems_TachiaiCeremony : MonoBehaviour
    {
        /// Ortho of the face beats — the referee's introFaceOrtho.
        private const float FACE_ORTHO = 1.2f;
        /// Two-shot ortho for the gaze lock; keepPair floors it at what the pair needs.
        private const float GAZE_ORTHO = 1.6f;
        private const float SHOT_SECONDS = 1.05f;
        private const float BLEND = 18f;
        private const float FACE_CENTERING_Y = 0.82f;
        /// Share of the countdown each beat ends at.
        private const float FACE_A_END = 0.3f, FACE_B_END = 0.6f, GAZE_END = 0.85f;
        /// Torso speed toward the opponent, m/s, that counts as "off the line".
        private const float CHARGE_SPEED = 0.6f;
        /// Give up timing a fighter after this long — he never charged.
        private const float REACTION_CAP_SECONDS = 1.5f;
        private const float READOUT_SECONDS = 2.4f;

        private Systems_GameMatchManager _manager;
        private Systems_CameraFollow _camera;
        private Systems_HudRoot _hud;
        private PlayableDirector _director;
        private TimelineAsset _timeline;
        private float _builtSeconds = -1f;
        private Systems_CeremonyBehaviour.Beat _lastBeat;
        private bool _beatIssued;

        private VisualElement _tapeCard;
        private Label _tapeText;
        private Label _readout;
        private float _readoutHideAt;

        /// Set by Begin, consumed by the release. The Release beat keeps re-issuing
        /// after ScoringLive goes true, so arming off `_beatIssued` timed every
        /// bout twice (the second time as "0 ms").
        private bool _armed;
        private bool _timing;
        private float _releaseTime;
        private float _reactionA, _reactionB;

        /// True while the ceremony is steering the camera.
        public bool OwnsCamera => _director != null && _director.state == PlayState.Playing;

        private void OnEnable()
        {
            if (_manager == null) _manager = FindAnyObjectByType<Systems_GameMatchManager>();
            if (_manager != null)
            {
                _manager.RoundEnded += OnRoundEnded;
                _manager.MatchReset += OnMatchReset;
            }
        }

        private void OnDisable()
        {
            if (_manager != null)
            {
                _manager.RoundEnded -= OnRoundEnded;
                _manager.MatchReset -= OnMatchReset;
            }
            StopCeremony();
        }

        private void OnDestroy()
        {
            if (_timeline != null) Destroy(_timeline);
        }

        /// Starts the face-off over a countdown of `seconds`. Called by the referee
        /// from StartCountdown — which can run before this component's own Start,
        /// so everything is initialised lazily here.
        public void Begin(float seconds)
        {
            if (seconds <= 0f) return;
            EnsureBuilt(seconds);
            _beatIssued = false;
            _timing = false;
            _armed = true;
            HideTape();
            _director.time = 0;
            _director.Play();
        }

        /// One beat's shot, re-issued every frame the clip is live.
        public void ApplyBeat(Systems_CeremonyBehaviour.Beat beat)
        {
            if (_manager == null || _camera == null) return;
            if (!_beatIssued || beat != _lastBeat)
            {
                // Cancel the outgoing shot first: a live wide OUTRANKS a punch-in
                // in Systems_CameraFollow and would swallow it.
                _camera.ClearShots();
                _lastBeat = beat;
                _beatIssued = true;
                if (beat == Systems_CeremonyBehaviour.Beat.GazeLock) ShowTape();
                else HideTape();
            }

            Agent_Biped a = _manager.wrestlerA;
            Agent_Biped b = _manager.wrestlerB;
            switch (beat)
            {
                case Systems_CeremonyBehaviour.Beat.FaceA:
                    if (a != null)
                    {
                        _camera.PunchIn(Systems_CameraFollow.FocusPoint(a), FACE_ORTHO,
                                        SHOT_SECONDS, BLEND, 1f, FACE_CENTERING_Y);
                    }
                    break;
                case Systems_CeremonyBehaviour.Beat.FaceB:
                    if (b != null)
                    {
                        _camera.PunchIn(Systems_CameraFollow.FocusPoint(b), FACE_ORTHO,
                                        SHOT_SECONDS, BLEND, 1f, FACE_CENTERING_Y);
                    }
                    break;
                case Systems_CeremonyBehaviour.Beat.GazeLock:
                    if (a != null)
                    {
                        _camera.PunchIn(Systems_CameraFollow.FocusPoint(a), GAZE_ORTHO,
                                        SHOT_SECONDS, BLEND, 0.5f, FACE_CENTERING_Y, true);
                    }
                    break;
                default:
                    _camera.PullBackWide(SHOT_SECONDS, BLEND);
                    break;
            }
        }

        private void Update()
        {
            if (_readout != null && _readout.style.display == DisplayStyle.Flex
                && Time.realtimeSinceStartup >= _readoutHideAt)
            {
                _readout.style.display = DisplayStyle.None;
            }
        }

        private void FixedUpdate()
        {
            if (_manager == null) return;

            // The release is the moment the referee's countdown lets go. The
            // director usually finishes on the same step; the referee's ScoringLive
            // is the authority, not the Timeline's clock.
            if (_armed && _manager.ScoringLive)
            {
                _armed = false;
                _timing = true;
                _releaseTime = Time.fixedTime;
                _reactionA = _reactionB = -1f;
            }
            if (!_timing) return;

            float since = Time.fixedTime - _releaseTime;
            if (_reactionA < 0f && Charging(_manager.wrestlerA, _manager.wrestlerB)) _reactionA = since;
            if (_reactionB < 0f && Charging(_manager.wrestlerB, _manager.wrestlerA)) _reactionB = since;
            if ((_reactionA >= 0f && _reactionB >= 0f) || since >= REACTION_CAP_SECONDS)
            {
                _timing = false;
                PostReaction();
            }
        }

        private static bool Charging(Agent_Biped fighter, Agent_Biped opponent)
        {
            if (fighter == null || opponent == null || fighter.Torso == null) return false;
            float toward = Mathf.Sign(opponent.TorsoX - fighter.TorsoX);
            return fighter.Torso.linearVelocity.x * toward > CHARGE_SPEED;
        }

        private void PostReaction()
        {
            string nameA = DisplayName(_manager.wrestlerA);
            string nameB = DisplayName(_manager.wrestlerB);
            Systems_Log.Info($"[TACHIAI] {nameA} {Ms(_reactionA)}, {nameB} {Ms(_reactionB)}");
            if (_readout == null) return;

            string line;
            if (_reactionA < 0f && _reactionB < 0f)
            {
                line = "NEITHER MAN CHARGES";
            }
            else if (_reactionB < 0f || (_reactionA >= 0f && _reactionA < _reactionB))
            {
                line = $"{nameA} FIRST OFF THE LINE · {Ms(_reactionA)}";
            }
            else if (_reactionA < 0f || _reactionB < _reactionA)
            {
                line = $"{nameB} FIRST OFF THE LINE · {Ms(_reactionB)}";
            }
            else
            {
                line = $"DEAD EVEN TACHIAI · {Ms(_reactionA)}";
            }
            _readout.text = line;
            _readout.style.display = DisplayStyle.Flex;
            _readoutHideAt = Time.realtimeSinceStartup + READOUT_SECONDS;
        }

        private static string Ms(float seconds) =>
            seconds < 0f ? "no charge" : $"{Mathf.RoundToInt(seconds * 1000f)} ms";

        private void OnRoundEnded(Agent_Biped winner, Agent_Biped loser)
        {
            _timing = false;
            StopCeremony();
        }

        private void OnMatchReset()
        {
            _timing = false;
            StopCeremony();
        }

        private void StopCeremony()
        {
            if (_director != null && _director.state == PlayState.Playing) _director.Stop();
            _beatIssued = false;
            _armed = false;
            HideTape();
        }

        private void EnsureBuilt(float seconds)
        {
            if (_manager == null) _manager = FindAnyObjectByType<Systems_GameMatchManager>();
            if (_camera == null) _camera = FindAnyObjectByType<Systems_CameraFollow>();
            if (_hud == null)
            {
                _hud = FindAnyObjectByType<Systems_HudRoot>();
                BuildUi();
            }
            if (_director == null)
            {
                _director = gameObject.AddComponent<PlayableDirector>();
                _director.playOnAwake = false;
                _director.timeUpdateMode = DirectorUpdateMode.GameTime;
                _director.extrapolationMode = DirectorWrapMode.None;
            }
            if (Mathf.Approximately(seconds, _builtSeconds)) return;

            if (_timeline != null) Destroy(_timeline);
            _timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            _timeline.name = "TachiaiCeremony";
            var track = _timeline.CreateTrack<Systems_CeremonyTrack>(null, "Beats");
            AddBeat(track, Systems_CeremonyBehaviour.Beat.FaceA, 0f, FACE_A_END * seconds);
            AddBeat(track, Systems_CeremonyBehaviour.Beat.FaceB, FACE_A_END * seconds, FACE_B_END * seconds);
            AddBeat(track, Systems_CeremonyBehaviour.Beat.GazeLock, FACE_B_END * seconds, GAZE_END * seconds);
            AddBeat(track, Systems_CeremonyBehaviour.Beat.Release, GAZE_END * seconds, seconds);
            _director.playableAsset = _timeline;
            _director.RebuildGraph();
            _builtSeconds = seconds;
        }

        private static void AddBeat(Systems_CeremonyTrack track, Systems_CeremonyBehaviour.Beat beat,
                                    float start, float end)
        {
            TimelineClip clip = track.CreateClip<Systems_CeremonyClip>();
            clip.start = start;
            clip.duration = Mathf.Max(0.05f, end - start);
            clip.displayName = beat.ToString();
            ((Systems_CeremonyClip)clip.asset).beat = beat;
        }

        private void BuildUi()
        {
            if (_hud == null || _hud.Stage == null) return;

            _tapeCard = Systems_UiKit.Card(Systems_UiKit.Panel).NoPick();
            _tapeCard.style.position = Position.Absolute;
            _tapeCard.style.left = Systems_UiKit.SPACE_5;
            _tapeCard.style.right = Systems_UiKit.SPACE_5;
            _tapeCard.style.top = Systems_UiKit.SPACE_5;
            _tapeCard.style.paddingTop = Systems_UiKit.SPACE_3;
            _tapeCard.style.paddingBottom = Systems_UiKit.SPACE_3;
            _tapeText = Systems_UiKit.Text("", Systems_UiKit.FONT_BODY, Systems_UiKit.TextHi, true);
            _tapeText.style.unityTextAlign = TextAnchor.MiddleCenter;
            _tapeText.style.whiteSpace = WhiteSpace.Normal;
            _tapeText.NoPick();
            _tapeCard.Add(_tapeText);
            _tapeCard.style.display = DisplayStyle.None;
            _hud.Stage.Add(_tapeCard);

            _readout = Systems_UiKit.Text("", Systems_UiKit.FONT_LEAD, Systems_UiKit.Gold, true);
            _readout.style.position = Position.Absolute;
            _readout.style.left = 0;
            _readout.style.right = 0;
            _readout.style.top = Systems_UiKit.SPACE_5;
            _readout.style.unityTextAlign = TextAnchor.MiddleCenter;
            _readout.style.whiteSpace = WhiteSpace.Normal;
            _readout.NoPick();
            _readout.style.display = DisplayStyle.None;
            _hud.Stage.Add(_readout);
        }

        private void ShowTape()
        {
            if (_tapeCard == null || _manager == null) return;
            _tapeText.text = $"{Tape(_manager.wrestlerA)}\nvs\n{Tape(_manager.wrestlerB)}";
            _tapeCard.style.display = DisplayStyle.Flex;
        }

        private void HideTape()
        {
            if (_tapeCard != null) _tapeCard.style.display = DisplayStyle.None;
        }

        /// "MATT · ELO 1043 · 5-2" — tournament Elo (the bracket's ladder) and the
        /// career match record. Built once per bout, at the gaze lock.
        private static string Tape(Agent_Biped fighter)
        {
            if (fighter == null) return "—";
            string key = fighter.behaviorName;
            Systems_CareerStats.Record record = Systems_CareerStats.Get(key);
            int elo = Mathf.RoundToInt(Systems_TournamentElo.Get(key));
            string wl = record != null ? $"{record.matchWins}-{record.matchLosses}" : "0-0";
            return $"{DisplayName(fighter)} · ELO {elo} · {wl}";
        }

        private static string DisplayName(Agent_Biped fighter)
        {
            if (fighter == null) return "—";
            if (!string.IsNullOrEmpty(fighter.displayNameOverride)) return fighter.displayNameOverride.ToUpperInvariant();
            string name = fighter.character != null ? fighter.character.behaviorName : fighter.name;
            return name.ToUpperInvariant();
        }
    }
}
