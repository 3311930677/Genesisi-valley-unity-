// Day/night skybox: equirectangular day HDRI (Poly Haven, CC0) blended with a procedural night sky, sunset glow,
// sun disc and stars. All parameters are driven by GenesisValley.Presentation.DayNightCycle.
Shader "GenesisValley/Sky"
{
    Properties
    {
        [NoScaleOffset] _DayTex("Day Panorama (equirect HDR)", 2D) = "grey" {}
        _Rotation("Rotation", Range(0,360)) = 0
        _DayExposure("Day Exposure", Float) = 1
        _DayTint("Day Tint", Color) = (1,1,1,1)
        _DayBlend("Day Blend", Range(0,1)) = 1
        _NightTop("Night Zenith", Color) = (0.015,0.025,0.06,1)
        _NightHorizon("Night Horizon", Color) = (0.06,0.08,0.14,1)
        _StarIntensity("Stars", Float) = 1.2
        _SunDir("Sun Direction", Vector) = (0.3,0.6,0.2,0)
        _SunColor("Sun Colour", Color) = (1,0.9,0.75,1)
        _SunSize("Sun Size", Range(0.0001,0.02)) = 0.0025
        _GlowColor("Sunset Glow", Color) = (1,0.55,0.25,1)
        _GlowStrength("Sunset Strength", Range(0,2)) = 0
        _MoonDir("Moon Direction", Vector) = (-0.3,0.5,-0.2,0)
        _GroundColor("Below Horizon", Color) = (0.45,0.47,0.44,1)
    }

    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Rotation;
                half _DayExposure;
                half4 _DayTint;
                half _DayBlend;
                half4 _NightTop;
                half4 _NightHorizon;
                half _StarIntensity;
                float4 _SunDir;
                half4 _SunColor;
                float _SunSize;
                half4 _GlowColor;
                half _GlowStrength;
                float4 _MoonDir;
                half4 _GroundColor;
            CBUFFER_END

            TEXTURE2D(_DayTex); SAMPLER(sampler_DayTex);

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.dir = input.positionOS.xyz;
                return o;
            }

            float Hash31(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.zyx + 31.32);
                return frac((p.x + p.y) * p.z);
            }

            half3 Stars(float3 dir)
            {
                float3 cell = floor(dir * 180.0);
                float h = Hash31(cell);
                float3 local = frac(dir * 180.0) - 0.5;
                float star = step(0.9965, h) * saturate(1.0 - length(local) * 2.4);
                float twinkle = 0.65 + 0.35 * sin(_Time.y * (1.5 + h * 4.0) + h * 40.0);
                half3 tint = lerp(half3(0.8, 0.85, 1.0), half3(1.0, 0.92, 0.8), frac(h * 13.7));
                return tint * star * twinkle;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 dir = normalize(input.dir);

                // Equirectangular lookup with rotation about Y.
                float rot = radians(_Rotation);
                float s = sin(rot), c = cos(rot);
                float3 rd = float3(c * dir.x - s * dir.z, dir.y, s * dir.x + c * dir.z);
                float2 uv = float2(atan2(rd.x, rd.z) / (2.0 * PI) + 0.5, asin(clamp(rd.y, -1.0, 1.0)) / PI + 0.5);
                half3 day = SAMPLE_TEXTURE2D_LOD(_DayTex, sampler_DayTex, uv, 0).rgb * _DayExposure * _DayTint.rgb;

                half up = saturate(dir.y);
                half3 night = lerp(_NightHorizon.rgb, _NightTop.rgb, pow(up, 0.6));
                night += Stars(dir) * _StarIntensity * smoothstep(0.0, 0.25, dir.y) * (1.0 - _DayBlend);

                float3 sunDir = normalize(_SunDir.xyz);
                float sunDot = dot(dir, sunDir);
                half3 sky = lerp(night, day, _DayBlend);

                // Sunset / sunrise glow hugging the horizon around the sun.
                half horizon = 1.0 - abs(dir.y);
                half glow = pow(saturate(sunDot * 0.5 + 0.5), 6.0) * pow(horizon, 3.0) * _GlowStrength;
                sky += _GlowColor.rgb * glow;

                // Sun disc + halo.
                half disc = smoothstep(1.0 - _SunSize, 1.0 - _SunSize * 0.5, sunDot) * step(-0.02, sunDir.y);
                half halo = pow(saturate(sunDot), 350.0) * 0.6 + pow(saturate(sunDot), 24.0) * 0.08;
                sky += _SunColor.rgb * (disc * 12.0 + halo * _DayBlend);

                // Moon.
                float3 moonDir = normalize(_MoonDir.xyz);
                float moonDot = dot(dir, moonDir);
                half moon = smoothstep(0.99935, 0.9996, moonDot) * (1.0 - _DayBlend) * step(0.0, moonDir.y);
                sky += half3(0.85, 0.9, 1.0) * (moon * 2.5 + pow(saturate(moonDot), 600.0) * 0.25 * (1.0 - _DayBlend));

                // Fade into the fog/ground colour below the horizon so the world edge never shows a seam.
                half below = smoothstep(0.02, -0.12, dir.y);
                sky = lerp(sky, _GroundColor.rgb, below);
                return half4(sky, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
