// Additive ribbon for LineRenderers and TrailRenderers: soft across its width, with optional
// pulses of light travelling along its length. Tint and fade come from the vertex colours.
Shader "Arena/Beam"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (1.0, 1.0, 1.0, 1.0)
        _Edge ("Edge Softness", Range(0.25, 6)) = 1.6
        _Flow ("Pulse Amount", Range(0, 1)) = 0.6
        _Tiling ("Pulses Along Length", Float) = 4.0
        _Speed ("Pulse Speed", Float) = 1.0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Beam"
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Edge;
                float _Flow;
                float _Tiling;
                float _Speed;
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
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                output.uv = input.uv;
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float across = pow(saturate(sin(saturate(input.uv.y) * 3.14159265)), _Edge);

                float phase = frac(input.uv.x * _Tiling - _Time.y * _Speed);
                float pulse = smoothstep(0.0, 0.25, phase) * smoothstep(1.0, 0.55, phase);
                float along = lerp(1.0, 0.25 + pulse * 1.5, _Flow);

                float4 tint = _Color * input.color;
                return float4(tint.rgb * (tint.a * across * along), 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
