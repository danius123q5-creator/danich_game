// Vertex-colour shader with a gentle WIND SWAY for grass/foliage. Same reliable base as
// VertexColorTrees (only Core.hlsl, no fog) + a sine displacement on the upper vertices so
// the tips wave while the base stays planted. Kept minimal so it compiles in a build.
Shader "Custom/VertexColorWind"
{
    Properties
    {
        _WindStrength ("Wind Strength", Float) = 0.25
        _WindSpeed ("Wind Speed", Float) = 1.8
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float _WindStrength;
            float _WindSpeed;

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
                float3 pos = IN.positionOS.xyz;
                float3 wp = TransformObjectToWorld(pos);
                // Tips (higher local Y) sway; base (Y≈0) stays put. Phase varies by world pos.
                float phase = _Time.y * _WindSpeed + wp.x * 0.25 + wp.z * 0.25;
                pos.x += sin(phase) * _WindStrength * max(0.0, pos.y);
                pos.z += cos(phase * 0.8) * _WindStrength * 0.5 * max(0.0, pos.y);
                OUT.positionHCS = TransformObjectToHClip(pos);
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
