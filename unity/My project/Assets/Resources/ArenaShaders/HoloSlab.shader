// Ledger block: a matte graphite hexagonal slab with a few fine lines machined into it, and a
// noise dissolve for blocks that are rejected.
Shader "Arena/HoloSlab"
{
    Properties
    {
        [HDR] _Color ("Tint", Color) = (0.36, 0.74, 1.0, 1.0)
        _Reveal ("Reveal", Range(0, 1)) = 1.0
        _Flash ("Flash", Range(0, 3)) = 0.0
        _Dissolve ("Dissolve", Range(0, 1)) = 0.0
        _Radius ("Hex Radius", Float) = 1.3
        _HalfHeight ("Half Height", Float) = 0.2
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
            float _Reveal;
            float _Flash;
            float _Dissolve;
            float _Radius;
            float _HalfHeight;
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
            float3 normalOS : TEXCOORD3;
        };

        Varyings Vert(Attributes input)
        {
            Varyings output;
            output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
            output.positionCS = TransformWorldToHClip(output.positionWS);
            output.normalWS = TransformObjectToWorldNormal(input.normalOS);
            output.positionOS = input.positionOS.xyz;
            output.normalOS = input.normalOS;
            return output;
        }

        // Positive while the surface still exists; shrinks to zero as _Dissolve rises
        float DissolveMargin(float3 positionOS)
        {
            float noise = ArenaNoise3(positionOS * 4.0 + _Seed);
            return noise + 0.05 - _Dissolve * 1.1;
        }
        ENDHLSL

        Pass
        {
            Name "HoloSlab"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            float4 Frag(Varyings input) : SV_Target
            {
                float margin = DissolveMargin(input.positionOS);
                clip(margin);

                float3 n = normalize(input.normalWS);
                float3 nOS = normalize(input.normalOS);
                float3 v = normalize(GetCameraPositionWS() - input.positionWS);
                float fresnel = pow(1.0 - saturate(dot(n, v)), 3.0);

                float3 po = input.positionOS;
                float topFace = saturate(abs(nOS.y) * 2.0 - 1.0);
                float sideFace = 1.0 - topFace;

                // Top face: a fine line just inside the rim
                float2 q = abs(po.xz);
                float edgeDist = _Radius * 0.8660254 - max(q.x * 0.8660254 + q.y * 0.5, q.y);
                float insetShape = (edgeDist - 0.14) * 60.0;
                float inset = exp(-insetShape * insetShape);
                float topLines = inset * 0.5;

                // Side faces: a single fine line around the middle and softly caught edges
                float height = po.y / max(_HalfHeight, 0.0001);
                float bandShape = height * 14.0;
                float band = exp(-bandShape * bandShape);
                // Clamped: on a face seen almost edge-on, multisampling can evaluate this slightly
                // outside the slab, where an unclamped falloff would explode into a bright flare
                float edges = exp(-saturate(1.0 - abs(height)) * 14.0);
                float sideLines = band * 0.4 + edges * 0.12;

                // Graphite body with a very fine grain, under the same studio key as everything else
                float3 tint = _Color.rgb;
                float grain = ArenaFbm3(po * 30.0 + _Seed);
                float3 albedo = (tint * 0.10 + float3(0.062, 0.066, 0.072)) * (0.94 + (grain - 0.47) * 0.16);
                float3 keyDir = normalize(float3(0.45, 0.65, -0.6));
                float3 color = albedo * (0.32 + saturate(dot(n, keyDir)));

                color += tint * (topLines * topFace + sideLines * sideFace + fresnel * 0.06) * _Reveal;
                color += tint * _Flash * (0.25 + topFace * 0.25);

                // A thin bright line along the dissolve front
                float burn = (1.0 - smoothstep(0.0, 0.08, margin)) * step(0.001, _Dissolve);
                color += lerp(tint, float3(1.0, 1.0, 1.0), 0.4) * burn * 1.2;

                // Nothing on a ledger block is ever allowed to be brighter than plain white
                return float4(clamp(color, 0.0, 1.0), 1.0);
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
                clip(DissolveMargin(input.positionOS));
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
                clip(DissolveMargin(input.positionOS));
                return float4(normalize(input.normalWS), 0.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
