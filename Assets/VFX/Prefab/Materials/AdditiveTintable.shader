Shader "Custom/URP/AdditiveTintable"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _TintColor ("Tint Color", Color) = (1,1,1,1)
        _SoftFactor ("Soft Particles Factor", Range(0.01, 5)) = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "Queue"="Transparent"
            "RenderType"="Transparent"
        }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode"="UniversalForward" }

            // Split blend equation so RGB stays additive but alpha channel is
            // left untouched.  In AR/passthrough the framebuffer alpha encodes
            // "how much passthrough to show" (0 = full passthrough).  The old
            // single-argument form wrote frag.a² + dst.a into alpha, pushing it
            // above 0 and making the passthrough compositor show black instead of
            // the real world.  Zero One on the alpha channel preserves dst.a = 0.
            Blend SrcAlpha One, Zero One
            ZWrite Off
            Cull Back

            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _SOFTPARTICLES_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            TEXTURE2D_X(_CameraDepthTexture);
            SAMPLER(sampler_CameraDepthTexture);

            float4 _MainTex_ST;
            float4 _TintColor;
            float _SoftFactor;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                float4 screenPos : TEXCOORD1;
            };

            Varyings vert (Attributes v)
            {
                Varyings o;

                o.positionHCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color * _TintColor;

                o.screenPos = ComputeScreenPos(o.positionHCS);

                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                half4 col = tex * i.color;

                #ifdef _SOFTPARTICLES_ON
                float sceneDepth = SampleSceneDepth(i.screenPos.xy / i.screenPos.w);
                float sceneZ = LinearEyeDepth(sceneDepth, _ZBufferParams);
                float partZ = LinearEyeDepth(i.screenPos.z / i.screenPos.w, _ZBufferParams);

                float fade = saturate(_SoftFactor * (sceneZ - partZ));
                col.a *= fade;
                #endif

                return col;
            }

            ENDHLSL
        }
    }
}