// Lighting environment for reflections only (never seen directly): a dim graphite studio with
// three soft rectangular light panels placed exactly where the scene's lights come from. A
// reflection probe renders this once, so polished surfaces pick up believable, studio-style
// highlights that agree with the real light directions.
Shader "Arena/StudioSky"
{
    Properties
    {
        _Sky ("Upper", Color) = (0.030, 0.032, 0.036, 1.0)
        _Ground ("Lower", Color) = (0.006, 0.006, 0.007, 1.0)
        _KeyDir ("Key Direction", Vector) = (0.4, 0.7, -0.5, 0.0)
        _KeySize ("Key Half Size", Vector) = (0.55, 0.32, 0.0, 0.0)
        _KeyColor ("Key Radiance", Vector) = (2.2, 2.15, 2.05, 0.0)
        _RimDir ("Rim Direction", Vector) = (-0.3, 0.3, 0.9, 0.0)
        _RimSize ("Rim Half Size", Vector) = (0.08, 0.6, 0.0, 0.0)
        _RimColor ("Rim Radiance", Vector) = (1.1, 1.25, 1.45, 0.0)
        _FillDir ("Fill Direction", Vector) = (-0.8, 0.2, -0.4, 0.0)
        _FillSize ("Fill Half Size", Vector) = (0.7, 0.45, 0.0, 0.0)
        _FillColor ("Fill Radiance", Vector) = (0.16, 0.17, 0.19, 0.0)
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "RenderPipeline" = "UniversalPipeline" "PreviewType" = "Skybox" }

        Pass
        {
            Name "StudioSky"
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Sky;
                float4 _Ground;
                float4 _KeyDir;
                float4 _KeySize;
                float4 _KeyColor;
                float4 _RimDir;
                float4 _RimSize;
                float4 _RimColor;
                float4 _FillDir;
                float4 _FillSize;
                float4 _FillColor;
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

            // A rectangular panel facing the origin, measured on the plane one unit along its axis
            float Panel(float3 dir, float3 axis, float2 halfSize)
            {
                axis = normalize(axis);
                float facing = dot(dir, axis);
                float3 helper = abs(axis.y) > 0.95 ? float3(1.0, 0.0, 0.0) : float3(0.0, 1.0, 0.0);
                float3 right = normalize(cross(helper, axis));
                float3 up = cross(axis, right);
                float2 p = float2(dot(dir, right), dot(dir, up)) / max(facing, 0.0001);
                float2 outside = abs(p) - halfSize;
                float edge = max(outside.x, outside.y);
                return smoothstep(0.03, -0.03, edge) * step(0.0, facing);
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float3 dir = normalize(input.direction);
                float3 color = lerp(_Ground.rgb, _Sky.rgb, smoothstep(-0.35, 0.6, dir.y));
                color += Panel(dir, _KeyDir.xyz, _KeySize.xy) * _KeyColor.rgb;
                color += Panel(dir, _RimDir.xyz, _RimSize.xy) * _RimColor.rgb;
                color += Panel(dir, _FillDir.xyz, _FillSize.xy) * _FillColor.rgb;
                return float4(color, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
