using System.Globalization;
using Unity.MLAgents;
using UnityEngine;

namespace PoSumo
{
    /// The diagnostics overlay's BENCHMARK SIM SPEED button: runs the arena that
    /// is on screen — every fighter observing, running its policy and driving its 13
    /// motors, Physics2D on its fixed 0.02 s step — as fast as this device manages for
    /// `DemoModeSettings.benchmarkSeconds` real seconds, and counts the physics ticks
    /// and the ML-Agents environment steps it got through.
    ///
    /// WHAT IT IS AND IS NOT. It is the environment side of training (what a headless
    /// training env does each step), sped up with Time.timeScale — fixedDeltaTime is
    /// untouched, so the dynamics are the ones every brain was trained against. It is
    /// NOT a training run: no trainer, no gradient step. The result says so.
    ///
    /// Time.timeScale belongs to Systems_MatchPresentation (hit-stop, slow motion). The
    /// benchmark borrows it for a few seconds and hands back whatever value it found;
    /// a hit-stop landing mid-run only makes the measured number lower, never higher.
    public sealed class Systems_SimBenchmark : MonoBehaviour
    {
        private Systems_DemoModeSettings _settings;
        private bool _running;
        private int _ticks;
        private int _startSteps;
        private int _fighters;
        private int _decisionPeriod;
        private float _realElapsed;
        private float _savedTimeScale;
        private float _savedMaximumDeltaTime;

        /// The last result in words; empty before the first run.
        public string Result { get; private set; } = string.Empty;

        /// Physics ticks per real second of the last run; 0 before the first.
        public float TicksPerSecond { get; private set; }

        /// ML-Agents environment steps per real second of the last run.
        public float EnvStepsPerSecond { get; private set; }

        public bool IsRunning => _running;

        private void Awake()
        {
            _settings = Resources.Load<Systems_DemoModeSettings>(Systems_DemoModeSettings.RESOURCE_PATH);
            if (_settings == null)
            {
                _settings = ScriptableObject.CreateInstance<Systems_DemoModeSettings>();
            }
        }

        /// Starts a run. False, with Result saying why, when there is nothing to time.
        public bool Begin()
        {
            if (_running)
            {
                return false;
            }
            Agent_Biped[] fighters = FindObjectsByType<Agent_Biped>();
            if (fighters.Length == 0)
            {
                Result = "Start a bout first: the bracket screen has no simulation to time.";
                return false;
            }

            _fighters = fighters.Length;
            _decisionPeriod = 1;
            var requester = fighters[0].GetComponent<DecisionRequester>();
            if (requester != null)
            {
                _decisionPeriod = Mathf.Max(1, requester.DecisionPeriod);
            }
            _running = true;
            _ticks = 0;
            _realElapsed = 0f;
            _startSteps = Academy.IsInitialized ? Academy.Instance.TotalStepCount : 0;
            _savedTimeScale = Time.timeScale;
            _savedMaximumDeltaTime = Time.maximumDeltaTime;
            // One frame may carry up to a second of simulation, so the count is bounded
            // by this device's CPU rather than by Unity's catch-up cap.
            Time.maximumDeltaTime = 1f;
            Time.timeScale = _settings.benchmarkTimeScale;
            Result = "Benchmark running...";
            return true;
        }

        private void FixedUpdate()
        {
            if (_running)
            {
                _ticks++;
            }
        }

        private void Update()
        {
            if (!_running)
            {
                return;
            }
            _realElapsed += Time.unscaledDeltaTime;
            if (_realElapsed >= _settings.benchmarkSeconds)
            {
                Stop(true);
            }
        }

        private void OnDisable() => Stop(false);

        private void Stop(bool report)
        {
            if (!_running)
            {
                return;
            }
            _running = false;
            Time.timeScale = _savedTimeScale;
            Time.maximumDeltaTime = _savedMaximumDeltaTime;
            if (!report)
            {
                return;
            }

            int steps = Academy.IsInitialized ? Academy.Instance.TotalStepCount - _startSteps : 0;
            TicksPerSecond = _realElapsed > 0f ? _ticks / _realElapsed : 0f;
            EnvStepsPerSecond = _realElapsed > 0f ? steps / _realElapsed : 0f;
            float decisions = EnvStepsPerSecond / _decisionPeriod * _fighters;
            Result = string.Format(CultureInfo.InvariantCulture,
                "{0:N0} physics ticks/s · {1:N0} env steps/s · {2:N0} decisions/s ({3} fighters, {4:0.0} s, this device). " +
                "Local simulation speed, not a training run.",
                TicksPerSecond, EnvStepsPerSecond, decisions, _fighters, _realElapsed);
            Systems_Log.Info("[BENCH] " + Result);
        }
    }
}
