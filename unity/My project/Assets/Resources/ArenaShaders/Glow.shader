// Additive soft light: a disc with a hot centre, or a thin ring. Works on quads, where it can
// billboard itself toward the camera, and on particle systems, which already face the camera.
Shader "Arena/Glow"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (1.0, 1.0, 1.0, 1.0)
        _Intensity ("Intensity", Range(0, 8)) = 1.0
        _Falloff ("Falloff", Range(0.5, 8)) = 2.2
        _Core ("Hot Core", Range(0, 4)) = 0.0
        _Ring ("Ring Blend", Range(0, 1)) = 0.0
        _RingRadius ("Ring Radius", Range(0, 1)) = 0.8
        _RingWidth ("Ring Width", Range(0.005, 0.5)) = 0.06
        [Toggle] _Billboard ("Face Camera", Float) = 0.0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" "PreviewType" = "Plane" }

        Pass
        {
            Name "Glow"
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
                float _Falloff;
                float _Core;
                float _Ring;
                float _RingRadius;
                float _RingWidth;
                float _Billboard;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                if (_Billboard > 0.5)
                {
                    float4x4 objectToWorld = GetObjectToWorldMatrix();
                    float2 scale = float2(length(objectToWorld._m00_m10_m20), length(objectToWorld._m01_m11_m21));
                    float3 centerVS = TransformWorldToView(TransformObjectToWorld(float3(0.0, 0.0, 0.0)));
                    float3 positionVS = centerVS + float3(input.positionOS.xy * scale, 0.0);
                    output.positionCS = TransformWViewToHClip(positionVS);
                }
                else
                {
                    output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                }
                output.color = input.color;
                output.uv = input.uv;
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float dist = length(input.uv * 2.0 - 1.0);
                float inside = saturate(1.0 - dist);

                float disc = pow(inside, _Falloff) + pow(inside, _Falloff * 4.0) * _Core;
                float ringShape = (dist - _RingRadius) / _RingWidth;
                float ring = exp(-ringShape * ringShape) * saturate(inside * 12.0);

                float4 tint = _Color * input.color;
                float strength = lerp(disc, ring, _Ring) * tint.a * _Intensity;
                return float4(tint.rgb * strength, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
