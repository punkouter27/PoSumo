using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace PoSumo
{
    /// URP renderer feature that draws the fighter silhouette outline. Lives on
    /// `Assets/Settings/Renderer2D.asset` (installed by *PoSumo → Install Fighter
    /// Outline Feature*). It only records its passes while a bout is live —
    /// `Systems_FighterOutline` raises `Active` — so the bracket screen and the
    /// training envs pay nothing for it.
    public sealed class Systems_FighterOutlineFeature : ScriptableRendererFeature
    {
        private const string SHADER_NAME = "PoSumo/FighterOutline";
        private Systems_FighterOutlinePass _pass;

        public override void Create()
        {
            Shader shader = Shader.Find(SHADER_NAME);
            if (shader == null)
            {
                shader = Resources.Load<Shader>("Shaders/PoSumo_FighterOutline");
            }
            if (shader == null)
            {
                Debug.LogWarning($"[OUTLINE] shader '{SHADER_NAME}' not found; the fighter outline is off.");
                return;
            }
            _pass = new Systems_FighterOutlinePass(shader);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (_pass == null || !Systems_FighterOutline.Active) return;
            if (renderingData.cameraData.cameraType != CameraType.Game) return;
            renderer.EnqueuePass(_pass);
        }

        protected override void Dispose(bool disposing)
        {
            if (_pass != null) _pass.Dispose();
            _pass = null;
        }
    }
}
