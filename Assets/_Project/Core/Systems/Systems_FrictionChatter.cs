using UnityEngine;

namespace PoSumo
{
    /// Stick-slip modulation for a friction loop, applied on the AUDIO thread.
    ///
    /// A foot dragged slowly under load does not slide smoothly: it sticks, the
    /// shear builds, it slips, and it sticks again. That is a train of short
    /// bursts whose RATE rises with sliding speed and whose DEPTH falls as the
    /// slide becomes continuous. The frame rate (60 Hz) cannot draw a 10-40 Hz
    /// burst train with AudioSource.volume, so the envelope is applied per sample
    /// here.
    ///
    /// The main thread writes `Rate` and `Depth`; this reads them. It touches no
    /// Unity API off the main thread — the sample rate is captured in Awake.
    /// Presentation only.
    public sealed class Systems_FrictionChatter : MonoBehaviour
    {
        /// Slip events per second. Written by Systems_FootScrape.
        public volatile float Rate = 12f;
        /// 0 = smooth slide, 1 = fully gated bursts. Written by Systems_FootScrape.
        public volatile float Depth;

        private int _sampleRate = 48000;
        private double _phase;
        private float _jitter = 1f;
        private readonly System.Random _rng = new System.Random(5519);

        private void Awake()
        {
            _sampleRate = Mathf.Max(8000, AudioSettings.outputSampleRate);
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            float depth = Depth;
            if (depth <= 0.001f)
            {
                return;
            }
            double step = Rate * _jitter / _sampleRate;
            for (int frameStart = 0; frameStart < data.Length; frameStart += channels)
            {
                _phase += step;
                if (_phase >= 1.0)
                {
                    _phase -= 1.0;
                    // Real stick-slip is irregular; an exact period reads as a motor.
                    _jitter = 0.7f + 0.6f * (float)_rng.NextDouble();
                    step = Rate * _jitter / _sampleRate;
                }
                // Sharp release at the slip, decaying back toward the stick.
                float decay = 1f - (float)_phase;
                float envelope = 1f - depth * (1f - decay * decay * decay);
                for (int channelIndex = 0; channelIndex < channels; channelIndex++)
                {
                    data[frameStart + channelIndex] *= envelope;
                }
            }
        }
    }
}
