// Additive energy shell: fresnel rim, a faint hexagonal lattice, cells that breathe
// at random and a slow band of light sweeping upward. Used for node shields and the core.
Shader "Arena/Shell"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (0.36, 0.74, 1.0, 1.0)
        _Alpha ("Strength", Range(0, 2)) = 0.5
        _HexScale ("Hex Scale", Float) = 5.0
        _Power ("Fresnel Power", Range(0.5, 6)) = 2.2
        _Seed ("Seed", Float) = 0.0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Shell"
            Blend One One
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "ArenaCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Alpha;
                float _HexScale;
                float _Power;
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

            float4 Frag(Varyings input) : SV_Target
            {
                float3 n = normalize(input.normalWS);
                float3 v = normalize(GetCameraPositionWS() - input.positionWS);
                float fresnel = pow(1.0 - saturate(dot(n, v)), _Power);

                // Cube-style projection of the direction keeps hexagons evenly sized over the sphere
                float3 d = normalize(input.positionOS);
                float3 a = abs(d);
                float2 uv = d.xy / max(a.z, 0.0001);
                if (a.x > a.y && a.x > a.z)
                {
                    uv = d.yz / a.x;
                }
                else if (a.y > a.z)
                {
                    uv = d.xz / a.y;
                }

                float2 hex = ArenaHex(uv * _HexScale + _Seed);
                float lattice = 1.0 - smoothstep(0.0, 0.06, hex.x);
                float breathing = pow(saturate(sin(_Time.y * 0.8 + hex.y * 6.2831853) * 0.5 + 0.5), 8.0) * hex.x * 2.0;

                float sweepPos = frac(_Time.y * 0.12 + _Seed * 0.37) * 2.6 - 1.3;
                float sweepShape = (d.y - sweepPos) * 6.0;
                float sweep = exp(-sweepShape * sweepShape);

                float strength = fresnel * 0.9
                               + lattice * (0.05 + fresnel * 0.35)
                               + breathing * 0.06
                               + sweep * 0.12 * (lattice + 0.25);

                return float4(_Color.rgb * strength * _Alpha, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
