// Simple particle shader (smoke, dust, sparks, embers, leaves). Texture * vertex colour * tint. Alpha-blended or
// additive (set _SrcBlend/_DstBlend). _Lit blends the colour toward the global ambient so smoke darkens at night.
Shader "GenesisValley/Particle"
{
    Properties
    {
        _BaseMap("Texture", 2D) = "white" {}
        _BaseColor("Tint", Color) = (1,1,1,1)
        _Lit("Ambient Lit", Range(0,1)) = 1
        _SoftFade("Soft Particle Fade (m)", Float) = 0.6
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend("Src Blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend("Dst Blend", Float) = 10
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" "PreviewType"="Plane" }
        Blend [_SrcBlend] [_DstBlend]
        ZWrite Off
        Cull Off

        Pass
        {
            Name "Particle"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _Lit;
                float _SoftFade;
                float _SrcBlend;
                float _DstBlend;
            CBUFFER_END
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            half4 _GV_AmbientLight; // set by DayNightCycle (rgb = light colour reaching particles)

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; float4 screenPos : TEXCOORD1; half fog : TEXCOORD2; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                o.color = input.color;
                o.screenPos = ComputeScreenPos(o.positionCS);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 c = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor * input.color;
                half3 ambient = _GV_AmbientLight.a > 0 ? _GV_AmbientLight.rgb : half3(1, 1, 1);
                c.rgb *= lerp(half3(1, 1, 1), ambient, _Lit);
                float2 suv = input.screenPos.xy / max(input.screenPos.w, 1e-5);
                float raw = SampleSceneDepth(suv);
                if (raw > 0.0 && raw < 1.0)
                {
                    float sceneEye = LinearEyeDepth(raw, _ZBufferParams);
                    c.a *= saturate((sceneEye - input.screenPos.w) / max(_SoftFade, 0.01));
                }
                c.rgb = MixFog(c.rgb, input.fog);
                return c;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
