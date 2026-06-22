// Minimal URP unlit shader that displays a mesh's baked VERTEX COLORS (Kenney Nature
// Kit models are coloured this way, not by a texture). Includes fog so trees melt into
// the sunset haze. Kept unlit + simple so it reliably compiles in a build.
Shader "Custom/VertexColorTrees"
{
    Properties
    {
        _Tint ("Tint", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float4 color       : COLOR;
                float  fogCoord    : TEXCOORD0;
            };

            float4 _Tint;

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.color = IN.color * _Tint;
                OUT.fogCoord = ComputeFogFactor(OUT.positionHCS.z);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                half3 c = IN.color.rgb;
                c = MixFog(c, IN.fogCoord);
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
