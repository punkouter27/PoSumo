using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;
#endif

namespace PoSumo
{
    /// Idle demo mode (attract mode). After `DemoModeSettings.idleSeconds` with no
    /// input at all — keyboard, mouse (buttons, wheel, movement), touch, gamepad —
    /// counted from load or from the last input:
    ///
    /// - on the bracket screen the tournament is started on auto-play, so the
    ///   showcase is real bouts (every fighter is an RL policy running inference;
    ///   PoSumo has no human-driven or heuristic fighter to switch off);
    /// - in the arena every HUD document is hidden (the diagnostics overlay stays
    ///   if it is open) and the camera cycles the showcase shots on the asset,
    ///   `cameraDwellSeconds` each;
    /// - any input ends it at once: HUD back, camera back to the mode it had.
    ///
    /// Built by Systems_DiagOverlay.Bootstrap on the same DontDestroyOnLoad object.
    public sealed class Systems_DemoMode : MonoBehaviour
    {
        public static Systems_DemoMode Instance { get; private set; }

        /// True while demo mode runs.
        public bool Active { get; private set; }

        /// Seconds of further idleness before demo mode starts (0 while active).
        public float SecondsUntilDemo =>
            Active ? 0f : Mathf.Max(0f, _settings.idleSeconds - _idleSeconds);

        private Systems_DemoModeSettings _settings;
        /// Idle time counted so far. Accumulated per frame rather than read off the
        /// clock, so a long frame (the WebGL shader warm-up, a tab in the background,
        /// a scene load) does not count as the visitor being idle.
        private float _idleSeconds;
        private float _shotTimer;
        private float _bracketTimer;
        private int _shotIndex;
        private Systems_CameraFollow.CameraMode _cameraBefore;
        private readonly List<UIDocument> _hidden = new List<UIDocument>();
        private Vector2 _lastMouse;
#if ENABLE_INPUT_SYSTEM
        private IDisposable _anyButton;
#endif

        private void Awake()
        {
            Instance = this;
            _settings = Resources.Load<Systems_DemoModeSettings>(Systems_DemoModeSettings.RESOURCE_PATH);
            if (_settings == null)
            {
                _settings = ScriptableObject.CreateInstance<Systems_DemoModeSettings>();
            }
            _idleSeconds = 0f;
        }

        private void OnEnable()
        {
#if ENABLE_INPUT_SYSTEM
            // Every button-like control on every device: keys, mouse buttons,
            // touch presses, gamepad buttons and triggers.
            _anyButton = InputSystem.onAnyButtonPress.Call(_ => NoteInput());
#endif
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDisable()
        {
#if ENABLE_INPUT_SYSTEM
            _anyButton?.Dispose();
            _anyButton = null;
#endif
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // The documents hidden in the old scene are gone with it.
            _hidden.Clear();
            _shotTimer = 0f;
            _bracketTimer = 0f;
            if (Active) EnterScene();
        }

        private void NoteInput()
        {
            _idleSeconds = 0f;
            if (Active) Exit();
        }

        private void Update()
        {
            PollContinuousInput();

            if (!Active)
            {
                _idleSeconds = AccumulateIdle(_idleSeconds, Time.unscaledDeltaTime, _settings.maxCountedFrameSeconds);
                if (_idleSeconds >= _settings.idleSeconds) Enter();
                return;
            }

            Systems_TournamentBracket bracket = FindAnyObjectByType<Systems_TournamentBracket>();
            if (bracket != null && _settings.startTournamentFromMenu)
            {
                // Re-checked every few seconds: a finished bracket resets, and the
                // next pass starts the new one.
                _bracketTimer -= Time.unscaledDeltaTime;
                if (_bracketTimer <= 0f)
                {
                    _bracketTimer = 3f;
                    bracket.BeginDemo();
                }
            }

            if (FindAnyObjectByType<Systems_CameraFollow>() != null && _settings.cameraShots.Length > 0)
            {
                _shotTimer += Time.unscaledDeltaTime;
                if (_shotTimer >= _settings.cameraDwellSeconds)
                {
                    _shotTimer = 0f;
                    _shotIndex = (_shotIndex + 1) % _settings.cameraShots.Length;
                    Systems_CameraFollow.Mode = _settings.cameraShots[_shotIndex];
                }
            }
        }

        /// Adds one frame to the idle count. A frame longer than maxFrameSeconds is a
        /// hitch (shader warm-up, background tab, scene load), not a visitor looking
        /// at the screen, so it adds nothing.
        public static float AccumulateIdle(float idleSeconds, float frameSeconds, float maxFrameSeconds)
        {
            if (!float.IsFinite(frameSeconds) || frameSeconds <= 0f || frameSeconds > maxFrameSeconds) return idleSeconds;
            return idleSeconds + frameSeconds;
        }

        /// Movement and wheel are not "button presses", so they are polled.
        private void PollContinuousInput()
        {
#if ENABLE_INPUT_SYSTEM
            Mouse mouse = Mouse.current;
            if (mouse != null)
            {
                Vector2 position = mouse.position.ReadValue();
                bool moved = (position - _lastMouse).sqrMagnitude > 4f && _lastMouse != Vector2.zero;
                _lastMouse = position;
                if (moved || mouse.scroll.ReadValue().sqrMagnitude > 0.01f) NoteInput();
            }
            Gamepad pad = Gamepad.current;
            if (pad != null && (pad.leftStick.ReadValue().sqrMagnitude > 0.04f
                                || pad.rightStick.ReadValue().sqrMagnitude > 0.04f))
            {
                NoteInput();
            }
            Touchscreen touch = Touchscreen.current;
            if (touch != null && touch.primaryTouch.press.isPressed) NoteInput();
#endif
        }

        private void Enter()
        {
            Active = true;
            _cameraBefore = Systems_CameraFollow.Mode;
            _shotIndex = 0;
            _shotTimer = 0f;
            _bracketTimer = 0f;
            Systems_Log.Info("[DEMO] idle " + _settings.idleSeconds + " s: demo mode on");
            EnterScene();
        }

        /// Applies demo mode to whatever scene is loaded now.
        private void EnterScene()
        {
            Systems_DiagOverlay overlay = Systems_DiagOverlay.Instance;
            if (overlay != null) overlay.SetButtonShown(false);

            // Only the arena's HUD is "non-essential": on the bracket the bracket
            // IS the showcase between bouts.
            if (FindAnyObjectByType<Systems_CameraFollow>() == null) return;
            if (_settings.cameraShots.Length > 0) Systems_CameraFollow.Mode = _settings.cameraShots[0];
            UIDocument mine = overlay != null ? overlay.Document : null;
            foreach (UIDocument doc in FindObjectsByType<UIDocument>())
            {
                if (doc == mine || doc.rootVisualElement == null) continue;
                if (doc.rootVisualElement.style.display == DisplayStyle.None) continue;
                doc.rootVisualElement.style.display = DisplayStyle.None;
                _hidden.Add(doc);
            }
        }

        private void Exit()
        {
            Active = false;
            Systems_Log.Info("[DEMO] input: demo mode off");
            foreach (UIDocument doc in _hidden)
            {
                if (doc != null && doc.rootVisualElement != null)
                {
                    doc.rootVisualElement.style.display = DisplayStyle.Flex;
                }
            }
            _hidden.Clear();
            Systems_CameraFollow.Mode = _cameraBefore;
            Systems_DiagOverlay overlay = Systems_DiagOverlay.Instance;
            if (overlay != null) overlay.SetButtonShown(true);
        }
    }
}
