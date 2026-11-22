using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace PoSumo
{
    /// The two render-graph passes behind `Systems_FighterOutlineFeature`:
    /// (1) draw each fighter's sprites, filtered by rendering layer, into a
    /// half-resolution mask with an override material — red for side A, green for
    /// side B; (2) blit an edge-detect of that mask over the camera colour.
    ///
    /// Cost when live: one half-res RGBA8 target, two renderer lists (14 sprites
    /// each) and one fullscreen pass. When no bout is running
    /// (`Systems_FighterOutline.Active` false) nothing is recorded at all.
    public sealed class Systems_FighterOutlinePass : ScriptableRenderPass
    {
        private static readonly List<ShaderTagId> ShaderTags = new List<ShaderTagId>
        {
            new ShaderTagId("Universal2D"),
            new ShaderTagId("UniversalForward"),
            new ShaderTagId("SRPDefaultUnlit"),
        };
        private static readonly int MaskColorId = Shader.PropertyToID("_MaskColor");

        private readonly Material _maskA;
        private readonly Material _maskB;
        private readonly Material _composite;

        private sealed class MaskData
        {
            public RendererListHandle ListA;
            public RendererListHandle ListB;
        }

        private sealed class CompositeData
        {
            public TextureHandle Mask;
            public Material Composite;
        }

        public Systems_FighterOutlinePass(Shader shader)
        {
            renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
            _maskA = CoreUtils.CreateEngineMaterial(shader);
            _maskA.SetColor(MaskColorId, new Color(1f, 0f, 0f, 0f));
            _maskB = CoreUtils.CreateEngineMaterial(shader);
            _maskB.SetColor(MaskColorId, new Color(0f, 1f, 0f, 0f));
            _composite = CoreUtils.CreateEngineMaterial(shader);
        }

        public void Dispose()
        {
            CoreUtils.Destroy(_maskA);
            CoreUtils.Destroy(_maskB);
            CoreUtils.Destroy(_composite);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalResourceData resources = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
            UniversalLightData lightData = frameData.Get<UniversalLightData>();
            if (resources.isActiveTargetBackBuffer && !resources.activeColorTexture.IsValid()) return;

            RenderTextureDescriptor camDesc = cameraData.cameraTargetDescriptor;
            var desc = new TextureDesc(Mathf.Max(1, camDesc.width / 2), Mathf.Max(1, camDesc.height / 2))
            {
                colorFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm,
                clearBuffer = true,
                clearColor = Color.clear,
                filterMode = FilterMode.Bilinear,
                name = "PoSumo_FighterOutlineMask",
            };
            TextureHandle mask = renderGraph.CreateTexture(desc);

            using (var builder = renderGraph.AddRasterRenderPass<MaskData>("PoSumo Outline Mask", out MaskData data))
            {
                data.ListA = renderGraph.CreateRendererList(ListParams(renderingData, cameraData, lightData,
                    _maskA, Systems_FighterOutline.LayerA));
                data.ListB = renderGraph.CreateRendererList(ListParams(renderingData, cameraData, lightData,
                    _maskB, Systems_FighterOutline.LayerB));
                builder.UseRendererList(data.ListA);
                builder.UseRendererList(data.ListB);
                builder.SetRenderAttachment(mask, 0);
                builder.AllowGlobalStateModification(false);
                builder.SetRenderFunc((MaskData pass, RasterGraphContext context) =>
                {
                    context.cmd.DrawRendererList(pass.ListA);
                    context.cmd.DrawRendererList(pass.ListB);
                });
            }

            using (var builder = renderGraph.AddRasterRenderPass<CompositeData>("PoSumo Outline Composite",
                       out CompositeData data))
            {
                data.Mask = mask;
                data.Composite = _composite;
                builder.UseTexture(mask);
                builder.SetRenderAttachment(resources.activeColorTexture, 0);
                builder.SetRenderFunc((CompositeData pass, RasterGraphContext context) =>
                {
                    Blitter.BlitTexture(context.cmd, pass.Mask, new Vector4(1f, 1f, 0f, 0f), pass.Composite, 1);
                });
            }
        }

        private static RendererListParams ListParams(UniversalRenderingData renderingData,
            UniversalCameraData cameraData, UniversalLightData lightData, Material overrideMaterial, uint layer)
        {
            DrawingSettings drawing = RenderingUtils.CreateDrawingSettings(ShaderTags, renderingData, cameraData,
                lightData, SortingCriteria.CommonTransparent);
            drawing.overrideMaterial = overrideMaterial;
            drawing.overrideMaterialPassIndex = 0;
            var filtering = new FilteringSettings(RenderQueueRange.all, -1, layer);
            return new RendererListParams(renderingData.cullResults, drawing, filtering);
        }
    }
}
