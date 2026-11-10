using System.Collections.Generic;
using UnityEngine;

namespace PoSumo
{
    /// Drives the fighter silhouette outline (`Systems_FighterOutlineFeature`):
    /// tags each fighter's sprites with a rendering-layer bit so the feature can
    /// draw them into its mask, and publishes per-side colour, width and strength
    /// as shader globals every frame.
    ///
    ///   colour   = the fighter's team colour (a mirror bout's side B is already
    ///              hue-shifted by Systems_MatchRoster, so the two stay distinct)
    ///   width    = 1.5 mask texels fresh, up to 3.5 as stamina runs out — a tiring
    ///              fighter visibly "thickens"
    ///   strength = 0.55 apart, ramping to 1 after 0.5 s of sustained clinch, which
    ///              is exactly when two overlapping bodies stop reading as two
    ///
    /// Presentation only: no physics, no observation. Spawned behind
    /// `GameTuning.enableFighterOutline`. `Active` is what tells the renderer
    /// feature to record anything at all.
    public sealed class Systems_FighterOutline : MonoBehaviour
    {
        /// Rendering-layer bits. Bit 0 is Unity's default and stays set, so
        /// nothing else that filters on rendering layers loses these sprites.
        public const uint LayerA = 1u << 1;
        public const uint LayerB = 1u << 2;

        private const float BASE_WIDTH = 1.5f;
        private const float FATIGUE_WIDTH = 2f;
        private const float BASE_ALPHA = 0.55f;
        private const float CLINCH_RANGE = 0.7f;
        private const float CLINCH_DELAY = 0.5f;
        private const float CLINCH_RAMP = 0.4f;
        /// Systems_BlobShadow names its per-foot contact sprites this way.
        private const string CONTACT_SHADOW_PREFIX = "FootContact";

        private static readonly int ParamsId = Shader.PropertyToID("_PoSumoOutlineParams");
        private static readonly int ColorAId = Shader.PropertyToID("_PoSumoOutlineColorA");
        private static readonly int ColorBId = Shader.PropertyToID("_PoSumoOutlineColorB");

        /// True while a bout scene has an outline driver. Static because the
        /// renderer feature is an asset and cannot hold a scene reference.
        public static bool Active { get; private set; }

        private Systems_GameMatchManager _manager;
        private Agent_BipedBody _bodyA, _bodyB;
        private readonly List<SpriteRenderer> _renderers = new List<SpriteRenderer>(64);
        private float _clinchTime;
        private float _retagAt;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Active = false;

        private void Start()
        {
            _manager = FindAnyObjectByType<Systems_GameMatchManager>();
            if (_manager == null || _manager.wrestlerA == null || _manager.wrestlerB == null)
            {
                enabled = false;
                return;
            }
            _bodyA = _manager.wrestlerA.GetComponent<Agent_BipedBody>();
            _bodyB = _manager.wrestlerB.GetComponent<Agent_BipedBody>();
            Retag();
            Active = true;
        }

        private void OnDisable()
        {
            Active = false;
            Shader.SetGlobalVector(ParamsId, Vector4.zero);
        }

        private void Update()
        {
            // Re-tag twice a second: bruise decals and the blood layer add sprites
            // to a fighter mid-bout, and an untagged sprite would cut a hole in
            // the silhouette.
            if (Time.unscaledTime >= _retagAt)
            {
                Retag();
            }

            float width = BASE_WIDTH + FATIGUE_WIDTH * (1f - Stamina(_bodyA));
            float widthB = BASE_WIDTH + FATIGUE_WIDTH * (1f - Stamina(_bodyB));

            bool clinched = _manager.ScoringLive
                && Mathf.Abs(_manager.wrestlerA.TorsoX - _manager.wrestlerB.TorsoX) < CLINCH_RANGE;
            _clinchTime = clinched ? _clinchTime + Time.deltaTime : 0f;
            float clinch = Mathf.Clamp01((_clinchTime - CLINCH_DELAY) / CLINCH_RAMP);
            float alpha = Mathf.Lerp(BASE_ALPHA, 1f, clinch);

            Shader.SetGlobalVector(ParamsId, new Vector4(width, widthB, alpha, alpha));
            Shader.SetGlobalColor(ColorAId, _bodyA != null ? _bodyA.teamColor : Color.white);
            Shader.SetGlobalColor(ColorBId, _bodyB != null ? _bodyB.teamColor : Color.white);
        }

        private static float Stamina(Agent_BipedBody body) => body != null ? Mathf.Clamp01(body.Stamina) : 1f;

        private void Retag()
        {
            _retagAt = Time.unscaledTime + 0.5f;
            Tag(_manager.wrestlerA, LayerA);
            Tag(_manager.wrestlerB, LayerB);
        }

        private void Tag(Agent_Biped fighter, uint layer)
        {
            if (fighter == null) return;
            fighter.GetComponentsInChildren(true, _renderers);
            for (int index = 0; index < _renderers.Count; index++)
            {
                SpriteRenderer sprite = _renderers[index];
                // Ground-contact shadows ride on the fighter but are not the
                // fighter: outlined, they draw rings on the clay under his feet.
                if (sprite.GetComponent<Systems_BlobShadow>() != null
                    || sprite.name.StartsWith(CONTACT_SHADOW_PREFIX, System.StringComparison.Ordinal))
                {
                    sprite.renderingLayerMask &= ~(LayerA | LayerB);
                    continue;
                }
                sprite.renderingLayerMask = (sprite.renderingLayerMask & ~(LayerA | LayerB)) | layer;
            }
        }
    }
}
