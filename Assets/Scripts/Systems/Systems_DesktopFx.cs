using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PoSumo
{
    /// The desktop quality tier. A desktop GPU has an order of magnitude more
    /// headroom than the phone this game ships to, so on a non-mobile platform the
    /// arena spends some of it:
    ///
    ///  - BLOOM: higher intensity and scatter with high-quality filtering, so the
    ///    lanterns and the key light bleed into the haze instead of clipping.
    ///  - CHROMATIC DISPERSION: a small permanent lens-edge aberration. It is a
    ///    BASELINE that Systems_PostFx reads on wake and adds its slow-motion and
    ///    knockout punches on top of — so this must be spawned before it.
    ///  - FILM GRAIN: the finer Thin1 lookup; Medium1 reads as noise at 1440p.
    ///  - PARTICLES: burst counts and caps doubled (Systems_DustPuff.SetDensity).
    ///
    /// What it deliberately does NOT do, so nobody re-derives it: screen-space GI,
    /// SSR, contact shadows and refraction have nothing to work on in URP's 2D
    /// renderer (no depth from sprites, no reliable opaque texture, and the only
    /// floor in frame is the dohyo's front face seen edge-on), and cast shadows
    /// are off for the geometric reason recorded in Systems_ArenaLighting.
    ///
    /// Mobile is untouched: the component disables itself on
    /// Application.isMobilePlatform. Presentation only, no brain affected.
    public sealed class Systems_DesktopFx : MonoBehaviour
    {
        [SerializeField] private float _bloomGain = 1.35f;
        [SerializeField] private float _bloomScatter = 0.72f;
        [SerializeField] private float _edgeDispersion = 0.06f;
        [SerializeField] private float _grainIntensity = 0.16f;
        [SerializeField] private float _particleDensity = 2f;

        /// True once the tier has been applied this session. Read by the debug
        /// panel and by the report tooling; never written from outside.
        public static bool Active { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlaySessionStart() => Active = false;

        private void Awake()
        {
            if (Application.isMobilePlatform)
            {
                enabled = false;
                return;
            }

            Systems_DustPuff.SetDensity(_particleDensity);

            Systems_ArenaLighting lighting = Systems_ArenaLighting.Instance;
            VolumeProfile profile = lighting != null ? lighting.PostProfile : null;
            if (profile != null)
            {
                if (profile.TryGet(out Bloom bloom))
                {
                    bloom.intensity.Override(bloom.intensity.value * _bloomGain);
                    bloom.scatter.Override(_bloomScatter);
                    bloom.highQualityFiltering.Override(true);
                }
                if (profile.TryGet(out ChromaticAberration aberration))
                {
                    aberration.intensity.Override(_edgeDispersion);
                }
                if (profile.TryGet(out FilmGrain grain))
                {
                    grain.type.Override(FilmGrainLookup.Thin1);
                    grain.intensity.Override(_grainIntensity);
                }
            }

            Active = true;
            Systems_Log.Info($"[DESKTOPFX] tier on — bloom x{_bloomGain}, dispersion {_edgeDispersion}, particles x{_particleDensity}, post {(profile != null ? "yes" : "none")}");
        }
    }
}
