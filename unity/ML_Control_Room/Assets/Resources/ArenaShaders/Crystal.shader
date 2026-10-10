// Matte machined solid: a faceted body with a very fine surface grain, under soft neutral studio
// lighting. The status colour is carried in the material itself, with only a trace of its own
// light. No glow, no moving patterns.
Shader "Arena/Crystal"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (0.45, 0.62, 0.78, 1.0)
        _Energy ("Inner Light", Range(0, 3)) = 1.0
        _Pulse ("Pulse", Range(0, 2)) = 0.0
        _Flow ("Reserved", Range(0, 2)) = 0.25
        _VeinScale ("Grain Scale", Float) = 6.0
        _Body ("Ambient", Range(0, 1)) = 0.16
        _Seed ("Seed", Float) = 0.0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "ArenaCommon.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _Color;
            float _Energy;
            float _Pulse;
            float _Flow;
            float _VeinScale;
            float _Body;
            float _Seed;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            float3 normalWS : TEXCOORD1;
            float3 positionOS : TEXCOORD2;
        };

        Varyings Vert(Attributes input)
        {
            Varyings output;
            output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
            output.positionCS = TransformWorldToHClip(output.positionWS);
            output.normalWS = TransformObjectToWorldNormal(input.normalOS);
            output.positionOS = input.positionOS.xyz;
            return output;
        }
        ENDHLSL

        Pass
        {
            Name "Crystal"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            float4 Frag(Varyings input) : SV_Target
            {
                float3 n = normalize(input.normalWS);
                float3 v = normalize(GetCameraPositionWS() - input.positionWS);
                float ndv = saturate(dot(n, v));

                // Fixed studio lighting: one soft neutral key and a faint cool fill opposite it
                float3 keyDir = normalize(float3(0.45, 0.65, -0.6));
                float key = saturate(dot(n, keyDir));
                float fill = saturate(dot(n, normalize(float3(-0.4, 0.3, 0.6))));

                // A very fine, even grain, like bead-blasted metal
                float grain = ArenaFbm3(input.positionOS * _VeinScale * 5.0 + _Seed);
                float surface = 0.94 + (grain - 0.47) * 0.16;

                float3 tint = _Color.rgb;
                float3 albedo = lerp(float3(0.085, 0.090, 0.098), tint, 0.6) * surface;

                float3 light = key * float3(1.0, 0.99, 0.97) * 1.05
                             + fill * float3(0.30, 0.34, 0.40) * 0.5
                             + _Body;
                float3 color = albedo * light;

                // A soft sheen, a faint edge lift, and a trace of the material's own light
                color += pow(saturate(dot(n, normalize(keyDir + v))), 24.0) * 0.06;
                color += tint * pow(1.0 - ndv, 3.0) * 0.08;
                color += tint * _Energy * 0.18;
                color += tint * _Pulse * 0.5;

                return float4(color, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ColorMask R

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragDepth

            float4 FragDepth(Varyings input) : SV_Target
            {
                return float4(input.positionCS.z, 0.0, 0.0, 0.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragDepthNormals

            float4 FragDepthNormals(Varyings input) : SV_Target
            {
                return float4(normalize(input.normalWS), 0.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
