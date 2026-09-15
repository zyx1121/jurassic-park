// Sea surface for the HD-2D island: a four-frame pixel wave flipbook tiled in world space and
// scrolled slowly, with a foam band where the water is shallow (scene depth against the terrain).
// Numbers follow the art handoff: 2 m tiles, 5 fps, 0.018 m/s drift, 0.45 m nominal foam width.
Shader "JurassicPark/SeaWaves"
{
    Properties
    {
        _Wave0 ("Wave frame 0", 2D) = "black" {}
        _Wave1 ("Wave frame 1", 2D) = "black" {}
        _Wave2 ("Wave frame 2", 2D) = "black" {}
        _Wave3 ("Wave frame 3", 2D) = "black" {}
        _FoamMap ("Foam band (v = distance from shore)", 2D) = "black" {}
        _Tint ("Tint", Color) = (1,1,1,1)
        _BaseColor ("Shore wash (0 - 0.7 m)", Color) = (0.235294,0.419608,0.333333,1)
        _ShelfColor ("Shallow shelf (0.7 - 1.5 m)", Color) = (0.176471,0.352941,0.313725,1)
        _OffshoreColor ("Offshore teal (1.5 - 3 m)", Color) = (0.129412,0.278431,0.290196,1)
        _DeepTealColor ("Deep teal (3 - 6 m)", Color) = (0.090196,0.196078,0.227451,1)
        _DeepColor ("Deep cold water (6 - 12 m)", Color) = (0.141176,0.129412,0.211765,1)
        _AbyssColor ("Abyss (over 12 m / no seabed)", Color) = (0.090196,0.078431,0.121569,1)
        _DepthThresholds ("Depth band boundaries in meters", Vector) = (0.7,1.5,3,6)
        _AbyssDepth ("Abyss starts at depth in meters", Float) = 12
        _DepthTintStrength ("Depth tint strength", Range(0,1)) = 0.8
        _FoamColor ("Foam colour", Color) = (0.760784,0.721569,0.631373,1)
        _Alpha ("Alpha", Range(0,1)) = 1
        _TileMeters ("Tile size in meters", Float) = 2
        _PixelsPerTile ("Pixel grid per tile", Float) = 128
        _FrameRate ("Flipbook frames per second", Float) = 5
        _ScrollSpeed ("Drift in meters per second", Float) = 0.018
        _CrossDrift ("Cross drift in meters per second", Float) = 0.006
        _FoamWidth ("Foam band width in meters", Float) = 0.45
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
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_Wave0); TEXTURE2D(_Wave1); TEXTURE2D(_Wave2); TEXTURE2D(_Wave3); TEXTURE2D(_FoamMap);
            SAMPLER(sampler_Wave0); SAMPLER(sampler_FoamMap);
            CBUFFER_START(UnityPerMaterial)
                half4 _Tint, _BaseColor, _FoamColor;
                half4 _ShelfColor, _OffshoreColor, _DeepTealColor, _DeepColor, _AbyssColor;
                half _Alpha, _DepthTintStrength;
                float4 _DepthThresholds;
                float _AbyssDepth, _TileMeters, _PixelsPerTile, _FrameRate, _ScrollSpeed, _CrossDrift, _FoamWidth;
            CBUFFER_END

            struct A { float4 positionOS : POSITION; };
            struct V { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float fog : TEXCOORD1; };

            V vert(A i)
            {
                V o;
                VertexPositionInputs p = GetVertexPositionInputs(i.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 SampleWave(int frame, float2 uv)
            {
                if (frame == 0) return SAMPLE_TEXTURE2D(_Wave0, sampler_Wave0, uv);
                if (frame == 1) return SAMPLE_TEXTURE2D(_Wave1, sampler_Wave0, uv);
                if (frame == 2) return SAMPLE_TEXTURE2D(_Wave2, sampler_Wave0, uv);
                return SAMPLE_TEXTURE2D(_Wave3, sampler_Wave0, uv);
            }

            float4 DepthThresholds()
            {
                float4 thresholds;
                thresholds.x = max(_DepthThresholds.x, 0.001);
                thresholds.y = max(_DepthThresholds.y, thresholds.x + 0.001);
                thresholds.z = max(_DepthThresholds.z, thresholds.y + 0.001);
                thresholds.w = max(_DepthThresholds.w, thresholds.z + 0.001);
                return thresholds;
            }

            half3 DepthColor(float depth, float4 thresholds, float abyssDepth)
            {
                if (depth < thresholds.x) return _BaseColor.rgb;
                if (depth < thresholds.y) return _ShelfColor.rgb;
                if (depth < thresholds.z) return _OffshoreColor.rgb;
                if (depth < thresholds.w) return _DeepTealColor.rgb;
                if (depth < abyssDepth) return _DeepColor.rgb;
                return _AbyssColor.rgb;
            }

            float WaterDepth(float3 surfaceWS, float abyssDepth, out half hasSeabed)
            {
                // Reproject the world-pixel centre so depth/foam also have hard, camera-independent pixels.
                hasSeabed = 0;
                float4 surfaceCS = TransformWorldToHClip(surfaceWS);
                if (surfaceCS.w <= 0.0001) return abyssDepth;
                float2 screenUV = surfaceCS.xy / surfaceCS.w;
                #if UNITY_UV_STARTS_AT_TOP
                    screenUV.y = -screenUV.y;
                #endif
                screenUV = screenUV * 0.5 + 0.5;
                if (any(screenUV < 0) || any(screenUV > 1))
                    return abyssDepth;

                float rawDepth = SampleSceneDepth(screenUV);
                #if UNITY_REVERSED_Z
                    if (rawDepth <= 0.000001) return abyssDepth;
                #else
                    if (rawDepth >= 0.999999) return abyssDepth;
                    rawDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, rawDepth);
                #endif
                float3 sceneWS = ComputeWorldSpacePosition(screenUV, rawDepth, UNITY_MATRIX_I_VP);
                // Vertical metres, not eye-ray distance: works with perspective and orthographic cameras.
                // Opaque scene depth is only a visible-seabed approximation, not a terrain heightfield.
                float depth = surfaceWS.y - sceneWS.y;
                if (depth < -0.001) return abyssDepth;
                hasSeabed = 1;
                return max(depth, 0.0);
            }

            half4 frag(V i) : SV_Target
            {
                float t = _Time.y;
                float tileMeters = max(_TileMeters, 0.001);
                float pixels = clamp(round(_PixelsPerTile), 1.0, 2048.0);
                float2 worldUV = (floor(i.positionWS.xz / tileMeters * pixels) + 0.5) / pixels;
                float2 drift = floor(t * float2(_ScrollSpeed, _CrossDrift) / tileMeters * pixels) / pixels;
                float2 uv = worldUV + drift;
                int frame = (int)fmod(floor(t * max(_FrameRate, 0.0)), 4.0);
                half4 wave = SampleWave(frame, uv);

                float4 thresholds = DepthThresholds();
                float abyssDepth = max(_AbyssDepth, thresholds.w + 0.001);
                half hasSeabed;
                float depth = WaterDepth(float3(worldUV.x * tileMeters, i.positionWS.y, worldUV.y * tileMeters),
                    abyssDepth, hasSeabed);
                float shore = saturate(depth / max(_FoamWidth, 0.001));
                // Foam: a ragged band at the waterline. The foam tile supplies the streak texture and the
                // wave frame's brightness dithers the outer edge so it breaks up per pixel and animates.
                float edge = 1.0 - shore;
                float2 foamUV = float2(uv.x + floor(t * 0.05 * pixels) / pixels, 0.47 + shore * 0.06);
                half lum = dot(SAMPLE_TEXTURE2D(_FoamMap, sampler_FoamMap, foamUV).rgb, half3(0.3, 0.59, 0.11));
                half noise = dot(wave.rgb, half3(0.3, 0.59, 0.11));
                half foam = saturate((edge - 0.35 - noise * 2.5) * 6.0) * saturate((lum - 0.3) * 4.0);
                foam = floor(foam * 3.0) / 3.0 * hasSeabed;

                half3 color = lerp(wave.rgb * _Tint.rgb, DepthColor(depth, thresholds, abyssDepth),
                    saturate(_DepthTintStrength));
                color = lerp(color, _FoamColor.rgb, foam);
                Light sun = GetMainLight();
                half3 illumination = saturate(SampleSH(half3(0, 1, 0))
                    + sun.color * saturate(sun.direction.y) * sun.distanceAttenuation);
                color *= illumination;
                half alpha = lerp(saturate(_Alpha), 1.0, foam);
                color = MixFog(color, i.fog);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
