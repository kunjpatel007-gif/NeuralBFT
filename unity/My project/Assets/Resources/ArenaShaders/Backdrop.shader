// A quiet studio void, drawn on the inside of a sphere that follows the camera: near-black
// graphite with a barely lighter band at eye level, so the scene has depth without any scenery.
Shader "Arena/Backdrop"
{
    Properties
    {
        _Top ("Above", Color) = (0.028, 0.030, 0.034, 1.0)
        _Bottom ("Below", Color) = (0.022, 0.024, 0.027, 1.0)
        _Horizon ("Eye Level", Color) = (0.070, 0.076, 0.086, 1.0)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Background" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" "PreviewType" = "Skybox" }

        Pass
        {
            Name "Backdrop"
            ZWrite Off
            Cull Front

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "ArenaCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Top;
                float4 _Bottom;
                float4 _Horizon;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 direction : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.direction = input.positionOS.xyz;
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float3 dir = normalize(input.direction);

                float up = pow(saturate(dir.y), 0.5);
                float down = pow(saturate(-dir.y), 0.5);
                float3 color = lerp(_Horizon.rgb, _Top.rgb, up);
                color = lerp(color, _Bottom.rgb, down);

                // A very broad, static variation so the gradient is not perfectly uniform
                color *= 0.92 + 0.16 * ArenaNoise3(dir * 1.3);

                return float4(color, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
