// The arena floor: a fine, perfectly still reference grid with heavier lines every fifth cell.
// Additive, anti-aliased with screen-space derivatives, and faded out long before it can shimmer.
Shader "Arena/Grid"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (0.72, 0.76, 0.80, 1.0)
        _Intensity ("Intensity", Range(0, 2)) = 0.13
        _Cell ("Cell Size", Float) = 2.0
        _Radius ("Fade Radius", Float) = 90.0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Grid"
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Intensity;
                float _Cell;
                float _Radius;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 centerWS : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.centerWS = TransformObjectToWorld(float3(0.0, 0.0, 0.0));
                return output;
            }

            float GridLines(float2 coord)
            {
                float2 width = max(fwidth(coord), 0.0001);
                float2 dist = abs(frac(coord - 0.5) - 0.5) / width;
                float lines = 1.0 - saturate(min(dist.x, dist.y));
                // Once a cell is only a few pixels wide the lines would shimmer, so let them go
                return lines * saturate(1.0 - max(width.x, width.y) * 2.5);
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float2 p = input.positionWS.xz - input.centerWS.xz;

                float minor = GridLines(p / _Cell);
                float major = GridLines(p / (_Cell * 5.0));

                float fade = smoothstep(_Radius, _Radius * 0.12, length(p));
                float strength = minor * 0.25 + major * 0.7;
                return float4(_Color.rgb * (strength * fade * _Intensity), 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
