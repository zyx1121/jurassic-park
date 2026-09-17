// Unlit vertex colour with one directional light's lambert term and fog. The terrain is one mesh coloured per vertex,
// so this single cheap pass draws all of it in one call.
Shader "JurassicPark/VertexColor"
{
    Properties
    {
        _Ambient ("Ambient", Range(0, 1)) = 0.55
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _Ambient;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; half3 normalOS : NORMAL; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; half4 color : COLOR; half fog : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                half3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                Light light = GetMainLight();
                half lambert = saturate(dot(normalWS, light.direction));
                output.positionCS = position.positionCS;
                output.color = half4(input.color.rgb * (_Ambient + (1 - _Ambient) * lambert * light.color), 1);
                output.fog = ComputeFogFactor(position.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                return half4(MixFog(input.color.rgb, input.fog), 1);
            }
            ENDHLSL
        }
    }
}
