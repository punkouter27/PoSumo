using System.Diagnostics;
using Unity.MLAgents;

namespace PoSumo
{
    /// Measures how long a DECISION step takes: from the moment the Academy starts
    /// the step (`Academy.AgentPreStep`) to the first `OnActionReceived` of that
    /// step. In between, ML-Agents collects every agent's observations and runs the
    /// policies (ONNX through the Inference Engine) — that wall-clock time is what a
    /// player feels as "inference latency", and it is what the TAB overlay shows.
    ///
    /// Only steps on which some agent actually requested a decision are sampled
    /// (`NoteDecisionRequested`, called from `Agent_Biped.CollectObservations`):
    /// with a decision period of 3, two steps in three run no model at all and
    /// would drag the average toward zero.
    ///
    /// ML-Agents' own TimerStack measures the same block, but it is internal and
    /// compiled only for the Editor and Standalone, not WebGL — hence this clock.
    public static class Systems_InferenceClock
    {
        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private static Academy _hookedTo;
        private static long _stepStartTicks;
        private static bool _decisionThisStep;
        private static bool _sampledThisStep;

        private static float[] _ring = new float[30];
        private static int _head;
        private static int _filled;

        /// Rolling average over the last `Window` decision steps, in milliseconds;
        /// 0 until the first sample.
        public static float AverageMs { get; private set; }

        /// The newest sample, in milliseconds.
        public static float LastMs { get; private set; }

        /// Decision steps measured since the clock was hooked.
        public static int Samples { get; private set; }

        public static int Window
        {
            get => _ring.Length;
            set
            {
                int size = value < 1 ? 1 : value;
                if (size == _ring.Length) return;
                _ring = new float[size];
                _head = 0;
                _filled = 0;
            }
        }

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _hookedTo = null;
            _head = 0;
            _filled = 0;
            AverageMs = 0f;
            LastMs = 0f;
            Samples = 0;
        }

        /// Subscribes to the live Academy. Safe to call every frame: it never
        /// creates an Academy (Systems_AcademyLifecycle owns that) and re-hooks
        /// after the Academy is disposed and rebuilt between scenes.
        public static void EnsureHooked()
        {
            if (!Academy.IsInitialized)
            {
                _hookedTo = null;
                return;
            }
            Academy academy = Academy.Instance;
            if (ReferenceEquals(academy, _hookedTo)) return;
            academy.AgentPreStep += OnPreStep;
            _hookedTo = academy;
        }

        private static void OnPreStep(int step)
        {
            _stepStartTicks = Clock.ElapsedTicks;
            _decisionThisStep = false;
            _sampledThisStep = false;
        }

        /// An agent is collecting observations: this step runs a policy.
        public static void NoteDecisionRequested() => _decisionThisStep = true;

        /// An agent received its action: the decision block of this step is over.
        public static void NoteActed()
        {
            if (!_decisionThisStep || _sampledThisStep || _hookedTo == null) return;
            _sampledThisStep = true;
            float ms = (Clock.ElapsedTicks - _stepStartTicks) * 1000f / Stopwatch.Frequency;
            Add(ms);
        }

        /// Adds one measurement to the rolling window (public for the EditMode test).
        public static void Add(float ms)
        {
            if (!float.IsFinite(ms) || ms < 0f) return;
            LastMs = ms;
            Samples++;
            _ring[_head] = ms;
            _head = (_head + 1) % _ring.Length;
            if (_filled < _ring.Length) _filled++;
            float sum = 0f;
            for (int i = 0; i < _filled; i++) sum += _ring[i];
            AverageMs = sum / _filled;
        }

        /// Clears the window (public for the EditMode test).
        public static void ResetWindow()
        {
            _head = 0;
            _filled = 0;
            AverageMs = 0f;
            LastMs = 0f;
            Samples = 0;
        }
    }
}
