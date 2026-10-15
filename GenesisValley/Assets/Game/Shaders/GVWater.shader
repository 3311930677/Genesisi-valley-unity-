// Stylised river / pond water for URP 14. Mesh UVs: u = across the channel (0..1), v = metres along the flow.
// Uses the camera depth texture (enabled on the URP assets) for depth tint, soft edges and shoreline foam; if the
// depth texture is unavailable it falls back to a uniform tint.
Shader "GenesisValley/Water"
{
    Properties
    {
        _ShallowColor("Shallow", Color) = (0.36, 0.62, 0.62, 0.55)
        _DeepColor("Deep", Color) = (0.08, 0.26, 0.33, 0.92)
        _DepthDistance("Depth Fade (m)", Float) = 1.8
        _EdgeSoftness("Edge Softness (m)", Float) = 0.25
        _FoamColor("Foam", Color) = (0.94, 0.97, 0.95, 1)
        _FoamDistance("Shore Foam (m)", Float) = 0.55
        _NormalMap("Ripple Normal", 2D) = "bump" {}
        _NormalStrength("Ripple Strength", Range(0,2)) = 0.6
        _RippleScale("Ripple Scale", Float) = 0.18
        _FlowSpeed("Flow Speed (m/s)", Float) = 0.7
        _Smoothness("Smoothness", Range(0,1)) = 0.93
        _SkyColor("Sky Reflection", Color) = (0.62, 0.74, 0.84, 1)
        _FresnelPower("Fresnel Power", Float) = 4
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-10" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        Pass
        {
            Name "Water"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back
            // Each pixel is blended once even where the river ribbon and the pond disc overlap.
            Stencil { Ref 64 ReadMask 64 WriteMask 64 Comp NotEqual Pass Replace }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor;
                half4 _DeepColor;
                float _DepthDistance;
                float _EdgeSoftness;
                half4 _FoamColor;
                float _FoamDistance;
                float4 _NormalMap_ST;
                half _NormalStrength;
                float _RippleScale;
                float _FlowSpeed;
                half _Smoothness;
                half4 _SkyColor;
                half _FresnelPower;
            CBUFFER_END

            TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;   // r = flow speed scale, g = foam boost, a = opacity
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv         : TEXCOORD1;
                float4 screenPos  : TEXCOORD2;
                half3 normalWS    : TEXCOORD3;
                half3 tangentWS   : TEXCOORD4;
                half3 bitangentWS : TEXCOORD5;
                half4 color       : TEXCOORD6;
                half fog          : TEXCOORD7;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                // Gentle swell so the surface never looks frozen.
                positionWS.y += sin(_Time.y * 1.3 + positionWS.x * 0.35 + positionWS.z * 0.27) * 0.025;
                o.positionWS = positionWS;
                o.positionCS = TransformWorldToHClip(positionWS);
                o.screenPos = ComputeScreenPos(o.positionCS);
                VertexNormalInputs n = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                o.normalWS = n.normalWS;
                o.tangentWS = n.tangentWS;
                o.bitangentWS = n.bitangentWS;
                o.uv = input.uv;
                o.color = input.color;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half3 SampleRipple(float2 uv)
            {
                return UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv), _NormalStrength);
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 screenUV = input.screenPos.xy / max(input.screenPos.w, 1e-5);
                float rawDepth = SampleSceneDepth(screenUV);
                float sceneEye = LinearEyeDepth(rawDepth, _ZBufferParams);
                float surfaceEye = input.screenPos.w;
                float depth = max(0.0, sceneEye - surfaceEye);
                // No depth texture bound -> raw depth is 0/1 at the far plane: treat as moderately deep.
                if (rawDepth <= 0.0 || rawDepth >= 1.0) depth = min(depth, _DepthDistance * 1.5);

                // Flowing ripples: two layers scrolled along the channel at different speeds.
                float flow = _FlowSpeed * max(input.color.r, 0.05);
                float t = _Time.y;
                float2 baseUV = float2(input.uv.x * 6.0, input.uv.y) * _RippleScale;
                float2 worldUV = input.positionWS.xz * _RippleScale;
                half3 r1 = SampleRipple(baseUV + float2(0.0, -t * flow * _RippleScale));
                half3 r2 = SampleRipple(worldUV * 1.73 + float2(t * 0.031, -t * flow * _RippleScale * 0.61));
                half3 rippleTS = normalize(half3(r1.xy + r2.xy, r1.z * r2.z));
                half3x3 tbn = half3x3(normalize(input.tangentWS), normalize(input.bitangentWS), normalize(input.normalWS));
                half3 n = normalize(mul(rippleTS, tbn));

                half3 viewDir = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                half shadow = lerp(0.55, 1.0, mainLight.shadowAttenuation);

                // Depth tint (Beer-Lambert style) and soft edge.
                half deepness = 1.0 - exp(-depth / max(_DepthDistance, 0.01));
                half4 water = lerp(_ShallowColor, _DeepColor, deepness);
                half3 ambient = SampleSH(n);
                half3 diffuse = water.rgb * (ambient + mainLight.color * saturate(dot(n, mainLight.direction)) * shadow * 0.45);

                // Sky reflection + sun glint.
                half fresnel = pow(1.0 - saturate(dot(n, viewDir)), _FresnelPower);
                half3 sky = _SkyColor.rgb * (0.35 + 0.65 * saturate(mainLight.color.r + mainLight.color.g + mainLight.color.b) / 3.0);
                half3 color = lerp(diffuse, sky, fresnel * 0.75);
                half3 h = normalize(mainLight.direction + viewDir);
                half spec = pow(saturate(dot(n, h)), lerp(32.0, 900.0, _Smoothness)) * _Smoothness * 3.0;
                color += mainLight.color * spec * shadow;

                // Shoreline foam broken up by the ripple texture.
                half foamNoise = saturate(r1.x * 0.5 + r2.y * 0.5 + 0.5);
                half shore = 1.0 - saturate(depth / max(_FoamDistance * (1.0 + input.color.g), 0.01));
                half foam = saturate(smoothstep(0.35, 0.85, shore * (0.65 + foamNoise * 0.7)));
                color = lerp(color, _FoamColor.rgb * (ambient * 0.6 + mainLight.color * 0.6 * shadow), foam * _FoamColor.a);

                half alpha = saturate(lerp(water.a, 1.0, fresnel * 0.5) + foam * 0.6);
                alpha *= saturate(depth / max(_EdgeSoftness, 0.001));
                alpha *= input.color.a;

                color = MixFog(color, input.fog);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
