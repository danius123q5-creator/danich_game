// Minimal URP unlit shader that shows a mesh's baked VERTEX COLORS. Kenney Nature Kit
// trees are coloured this way (green canopy + brown trunk live in the vertices, not a
// texture). Kept as small as possible — only Core.hlsl — so it reliably compiles in a
// build (a failed shader shows up as magenta/pink, which is what we're fixing here).
Shader "Custom/VertexColorTrees"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4  color      : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                half4  color       : COLOR;
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.color = IN.color;
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                return half4(IN.color.rgb, 1);
            }
            ENDHLSL
        }
    }
}
