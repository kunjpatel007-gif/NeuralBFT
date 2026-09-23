Shader "Custom/InfiniteVoidBackground"
{
    Properties
    {
        _MainTint ("Void Base Color", Color) = (0.02, 0.02, 0.06, 1)
        _AccentColor ("Accent Glow Color", Color) = (0.4, 0.85, 1, 1)
        _SwirlSpeed ("Swirl Speed", Range(0, 5)) = 0.6
        _SwirlStrength ("Swirl Strength", Range(0, 10)) = 3.0
        _RingCount ("Ring Count", Range(1, 40)) = 14
        _RingSpeed ("Ring Pulse Speed", Range(0, 5)) = 1.5
        _StarDensity ("Star Density", Range(10, 200)) = 60
        _StarSpeed ("Star Twinkle Speed", Range(0, 10)) = 3.0
        _VignetteInner ("Vignette Inner Radius", Range(0, 1)) = 0.15
        _VignetteOuter ("Vignette Outer Radius", Range(0, 1.5)) = 0.9
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry+1" }
        Cull Off
        ZWrite On
        ZTest LEqual

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            fixed4 _MainTint;
            fixed4 _AccentColor;
            float _SwirlSpeed, _SwirlStrength, _RingCount, _RingSpeed;
            float _StarDensity, _StarSpeed;
            float _VignetteInner, _VignetteOuter;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            // Cheap hash-based value noise, good enough for a starfield
            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 uv = i.uv - 0.5;
                float dist = length(uv);
                float angle = atan2(uv.y, uv.x);

                // We keep the swirl math just so the stars slowly rotate in a cool pattern
                float swirl = angle * 3.0 + dist * _SwirlStrength - _Time.y * _SwirlSpeed;

                // REMOVED the pulsing central light/rings entirely per user request.
                // The background is now a perfectly clean, uninterrupted dark void.
                fixed3 col = _MainTint.rgb;

                // CRITICAL FIX: The stars were massive rectangles because they were based on 0-1 mesh UVs!
                // We map them to screen pixels. Multiplying by 0.25 makes each star block exactly 4x4 pixels wide.
                float2 pixelUV = uv * _ScreenParams.xy * 0.25; 
                float2 starUV = pixelUV + swirl * 15.0; 
                float star = hash21(floor(starUV));
                float twinkle = sin(_Time.y * _StarSpeed + star * 62.83) * 0.5 + 0.5;
                
                // Doubled star brightness from previous (multiplier from 0.0375 -> 0.075)
                float starMask = step(0.995, star) * twinkle * 0.075; 
                
                // User requested dark glowy blue stars instead of white/cyan!
                fixed3 starColor = fixed3(0.15, 0.35, 1.0); // Rich, deep glowy blue
                col += starMask * starColor;

                // Vignette so the edges dissolve to pure black — endless darkness
                float vig = smoothstep(_VignetteOuter, _VignetteInner, dist);
                col *= vig;

                return fixed4(col, 1.0);
            }
            ENDCG
        }
    }
}
