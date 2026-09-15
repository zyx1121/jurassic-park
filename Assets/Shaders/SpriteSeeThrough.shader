// Translucent sprite quad used while a prop hides the player: alpha falls off toward the
// player's screen position (a soft hole you look through) and the whole thing fades in and out
// over time via _Fade. Simple main-light Lambert plus fog so it sits with the URP Lit sprites.
Shader "JurassicPark/SpriteSeeThrough"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1,1,1,1)
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5
        _Fade ("Fade (0 opaque .. 1 fully faded)", Range(0,1)) = 0
        _HoleAlpha ("Alpha at the hole center", Range(0,1)) = 0.12
        _HoleRadius ("Hole radius in screen height units", Range(0.02,1)) = 0.22
        _HoleSoftness ("Hole edge softness", Range(0.01,1)) = 0.18
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _Cutoff, _Fade, _HoleAlpha, _HoleRadius, _HoleSoftness;
            CBUFFER_END
            float4 _SeeThroughPlayerScreen; // xy: player position in 0..1 screen space, z: aspect

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 normalWS : TEXCOORD1; float3 positionWS : TEXCOORD2; float fog : TEXCOORD3; };

            V vert(A i)
            {
                V o;
                VertexPositionInputs p = GetVertexPositionInputs(i.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.uv = TRANSFORM_TEX(i.uv, _BaseMap);
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag(V i) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;
                clip(tex.a - _Cutoff);

                // Lambert with the main light + ambient, both faces lit
                Light l = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half3 n = normalize(i.normalWS);
                half ndl = abs(dot(n, l.direction));
                half3 color = tex.rgb * (l.color * l.shadowAttenuation * ndl + SampleSH(n));
                color = MixFog(color, i.fog);

                // Soft hole around the player's screen position; aspect-corrected distance.
                // GetNormalizedScreenSpaceUV accounts for the render scale and the platform y flip,
                // so it matches Camera.WorldToViewportPoint (0..1, origin bottom-left).
                float2 sp = GetNormalizedScreenSpaceUV(i.positionCS);
                float2 d = sp - _SeeThroughPlayerScreen.xy;
                d.x *= _SeeThroughPlayerScreen.z;
                float dist = length(d);
                float hole = 1.0 - smoothstep(_HoleRadius, _HoleRadius + _HoleSoftness, dist);
                half alpha = lerp(1.0, _HoleAlpha, hole * _Fade);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
