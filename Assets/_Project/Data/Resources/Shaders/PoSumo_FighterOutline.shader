// Fighter silhouette outline (Systems_FighterOutlineFeature).
//
// Pass 0 "Mask": an OVERRIDE material for each fighter's sprites. Writes the
//   sprite's alpha coverage into one channel (_MaskColor: red = side A, green =
//   side B), additively, into a low-res mask texture.
// Pass 1 "Composite": a fullscreen blit over the camera colour. A pixel that is
//   OUTSIDE a side's silhouette but has a covered neighbour within that side's
//   width is outline. In a clinch each side's outline lies across the other's
//   body at the seam, which is exactly what keeps two overlapping bodies apart.
Shader "PoSumo/FighterOutline"
{
    Properties
    {
        _MainTex ("Sprite", 2D) = "white" {}
        _MaskColor ("Mask Channel", Color) = (1, 0, 0, 1)
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" }
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "Mask"
            Blend One One

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _MaskColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half alpha : TEXCOORD1;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.alpha = input.color.a;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half a = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).a * input.alpha;
                clip(a - 0.35h);
                return _MaskColor;
            }
            ENDHLSL
        }

        Pass
        {
            Name "Composite"
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            // x = A width (mask texels), y = B width, z = A alpha, w = B alpha.
            float4 _PoSumoOutlineParams;
            half4 _PoSumoOutlineColorA;
            half4 _PoSumoOutlineColorB;

            // Max coverage of one channel over 8 directions at radius r.
            half Ring(float2 uv, float2 texel, float r, int channel)
            {
                const float2 dirs[8] = {
                    float2(1, 0), float2(-1, 0), float2(0, 1), float2(0, -1),
                    float2(0.707, 0.707), float2(-0.707, 0.707),
                    float2(0.707, -0.707), float2(-0.707, -0.707) };
                half best = 0;
                [unroll] for (int d = 0; d < 8; d++)
                {
                    half4 m = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + dirs[d] * texel * r);
                    best = max(best, channel == 0 ? m.r : m.g);
                }
                return best;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float2 texel = _BlitTexture_TexelSize.xy;
                half4 centre = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);

                // Two radii per side: inner and full width, so a 2-3 texel line
                // has no holes at the diagonals.
                half nearA = max(Ring(uv, texel, _PoSumoOutlineParams.x * 0.5, 0),
                                 Ring(uv, texel, _PoSumoOutlineParams.x, 0));
                half nearB = max(Ring(uv, texel, _PoSumoOutlineParams.y * 0.5, 1),
                                 Ring(uv, texel, _PoSumoOutlineParams.y, 1));
                half edgeA = saturate(nearA - centre.r) * _PoSumoOutlineParams.z;
                half edgeB = saturate(nearB - centre.g) * _PoSumoOutlineParams.w;

                half4 colour = edgeA >= edgeB
                    ? half4(_PoSumoOutlineColorA.rgb, edgeA)
                    : half4(_PoSumoOutlineColorB.rgb, edgeB);
                return colour;
            }
            ENDHLSL
        }
    }
}
