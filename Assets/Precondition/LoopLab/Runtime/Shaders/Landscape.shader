Shader "LoopLab/Landscape"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.18, 0.31, 0.44, 1)
        _AccentColor ("Accent Color", Color) = (0.92, 0.73, 0.43, 1)
        _GridScale ("Grid Scale", Float) = 4.5
        _Phase ("Phase", Float) = 0
        _Seed ("Seed", Float) = 0
        _Duration ("Duration", Float) = 3
        _LoopVector ("Loop Vector", Vector) = (1, 0, 0, 0)
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Opaque" }

        Pass
        {
            Name "Forward"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _AccentColor;
                float4 _LoopVector;
                float _GridScale;
                float _Phase;
                float _Seed;
                float _Duration;
            CBUFFER_END

            static const float kFullTurn = 6.28318530718;

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            float PhaseBlend(float phase, float rate, float offset)
            {
                return 0.5 + 0.5 * cos((phase * rate + offset) * kFullTurn);
            }

            float TileBand(float2 centeredUv, float2 primaryFrequency, float2 secondaryFrequency)
            {
                float primary =
                    cos(centeredUv.x * kFullTurn * primaryFrequency.x) *
                    cos(centeredUv.y * kFullTurn * primaryFrequency.y);
                float secondary =
                    cos(centeredUv.x * kFullTurn * secondaryFrequency.x) *
                    cos(centeredUv.y * kFullTurn * secondaryFrequency.y);
                return primary * 0.62 + secondary * 0.38;
            }

            float TileFbm(float2 uv, float phase, float seed)
            {
                float2 centeredUv = uv - 0.5;
                float seedBias = frac(seed * 0.013);
                float octaveA = lerp(
                    TileBand(centeredUv, float2(1.0, 1.0), float2(2.0, 1.0)),
                    TileBand(centeredUv, float2(1.0, 2.0), float2(2.0, 2.0)),
                    PhaseBlend(phase, 1.0, 0.11 + seedBias * 0.37));
                float octaveB = lerp(
                    TileBand(centeredUv, float2(2.0, 2.0), float2(3.0, 2.0)),
                    TileBand(centeredUv, float2(2.0, 3.0), float2(4.0, 2.0)),
                    PhaseBlend(phase, 2.0, 0.23 + seedBias * 0.29));
                float octaveC = lerp(
                    TileBand(centeredUv, float2(4.0, 3.0), float2(5.0, 4.0)),
                    TileBand(centeredUv, float2(3.0, 5.0), float2(6.0, 4.0)),
                    PhaseBlend(phase, 3.0, 0.37 + seedBias * 0.21));
                float octaveD = lerp(
                    TileBand(centeredUv, float2(6.0, 5.0), float2(8.0, 6.0)),
                    TileBand(centeredUv, float2(5.0, 7.0), float2(8.0, 8.0)),
                    PhaseBlend(phase, 4.0, 0.49 + seedBias * 0.17));
                float value = octaveA * 0.42 + octaveB * 0.28 + octaveC * 0.18 + octaveD * 0.12;
                return value * 0.5 + 0.5;
            }

            float TileRidgedFbm(float2 uv, float phase, float seed)
            {
                float primary = TileFbm(uv, phase, seed);
                float secondary = TileFbm(uv, frac(phase * 2.0 + 0.23), seed + 17.0);
                float ridgePrimary = 1.0 - abs(primary * 2.0 - 1.0);
                float ridgeSecondary = 1.0 - abs(secondary * 2.0 - 1.0);
                return saturate(ridgePrimary * ridgePrimary * 0.68 + ridgeSecondary * ridgeSecondary * 0.32);
            }

            float TerrainLayer(float xCoord, float band, float phase, float seed, float amplitude, float contrast)
            {
                float2 sampleUv = float2(xCoord, band);
                float ridge = TileRidgedFbm(sampleUv, phase, seed);
                float contour = TileFbm(sampleUv, frac(phase * 2.0 + 0.17), seed + 9.0);
                ridge = pow(saturate(ridge), contrast);
                return ridge * amplitude + contour * amplitude * 0.18;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                float phase = frac(_Phase);
                float seed = _Seed * 0.071;
                float detailBias = saturate((_GridScale - 4.2) * 0.22);
                float mirroredY = abs(uv.y - 0.5) * 2.0;
                float atmosphereBand = saturate(mirroredY);

                float warpField = TileFbm(uv, frac(phase * 2.0 + 0.11), seed + 3.0) - 0.5;
                float ridgeWarp = TileRidgedFbm(uv, frac(phase * 3.0 + 0.29), seed + 19.0) - 0.5;
                float bandWarp = (warpField * 0.16 + ridgeWarp * 0.11) * (0.72 + detailBias * 0.24);

                float farHeight = 0.46 + TerrainLayer(uv.x, 0.18 + bandWarp * 0.24, frac(phase + 0.09), seed + 31.0, 0.05, 1.08);
                float midHeight = 0.31 + TerrainLayer(uv.x, 0.42 + bandWarp * 0.32, frac(phase * 2.0 + 0.17), seed + 53.0, 0.07, 1.18);
                float nearHeight = 0.18 + TerrainLayer(uv.x, 0.66 + bandWarp * 0.40, frac(phase * 3.0 + 0.29), seed + 79.0, 0.09, 1.30);

                float farMask = 1.0 - smoothstep(farHeight, farHeight + 0.03, mirroredY);
                float midMask = 1.0 - smoothstep(midHeight, midHeight + 0.026, mirroredY);
                float nearMask = 1.0 - smoothstep(nearHeight, nearHeight + 0.022, mirroredY);

                float cloudNoise = TileFbm(uv, frac(phase * 2.0 + 0.43), seed + 101.0);
                float cloudRidge = TileRidgedFbm(uv, frac(phase * 4.0 + 0.59), seed + 127.0);
                float cloudMask = smoothstep(0.58, 0.84, cloudNoise + cloudRidge * 0.18) * atmosphereBand;

                float horizonGlow = saturate(1.0 - abs(mirroredY - farHeight) * 4.4) * (0.68 + cloudNoise * 0.32);
                float valleyFogNoise = TileFbm(uv, frac(phase * 5.0 + 0.71), seed + 149.0);
                float valleyFog = smoothstep(0.18, 0.74, mirroredY) * (1.0 - nearMask * 0.78) * saturate(0.26 + valleyFogNoise * 0.74);

                float farTexture = TileFbm(uv, frac(phase + 0.17), seed + 173.0);
                float midTexture = TileRidgedFbm(uv, frac(phase * 2.0 + 0.33), seed + 197.0);
                float nearTexture = TileRidgedFbm(uv, frac(phase * 3.0 + 0.49), seed + 223.0);

                float farCrest = 1.0 - smoothstep(0.0, 0.035, abs(mirroredY - farHeight));
                float midCrest = 1.0 - smoothstep(0.0, 0.032, abs(mirroredY - midHeight));
                float nearCrest = 1.0 - smoothstep(0.0, 0.028, abs(mirroredY - nearHeight));
                float farBandMask = saturate(farMask - midMask);
                float midBandMask = saturate(midMask - nearMask * 0.92);
                float nearBandMask = nearMask;

                float3 baseColor = saturate(_BaseColor.rgb);
                float3 accentColor = saturate(_AccentColor.rgb);
                float3 dawnGlow = saturate(lerp(accentColor * 1.06, float3(1.0, 0.79, 0.63), 0.46));
                float3 skyZenith = saturate(baseColor * 1.42 + float3(0.07, 0.10, 0.18));
                float3 skyHorizon = saturate(lerp(baseColor * 0.48, dawnGlow, 0.78));

                float skyBlend = saturate(atmosphereBand * 0.88 + cloudNoise * 0.12);
                float3 color = lerp(skyHorizon, skyZenith, skyBlend);
                color = lerp(color, dawnGlow, horizonGlow * 0.24);
                color = lerp(color, float3(1.0, 0.97, 0.93), cloudMask * 0.09);

                float3 farColor = lerp(skyHorizon * 0.88, baseColor * 0.68 + dawnGlow * 0.18, saturate(farTexture * 0.68 + 0.18));
                float3 midColor = lerp(baseColor * 0.42, baseColor * 0.8 + dawnGlow * 0.22, saturate(midTexture * 0.66 + 0.18));
                float3 nearColor = lerp(baseColor * 0.28, baseColor * 0.52 + accentColor * 0.18, saturate(nearTexture * 0.58 + 0.28));

                farColor = lerp(farColor, dawnGlow, farCrest * 0.34);
                midColor = lerp(midColor, dawnGlow, midCrest * 0.28);
                nearColor = lerp(nearColor, accentColor, nearCrest * 0.18);

                color = lerp(color, farColor, farBandMask);
                color = lerp(color, midColor, midBandMask);
                color = lerp(color, nearColor, nearBandMask * 0.82);
                color = lerp(color, accentColor * 0.62 + dawnGlow * 0.38, nearCrest * nearBandMask * 0.18);
                color = lerp(color, skyHorizon, valleyFog * (0.26 + (1.0 - nearBandMask) * 0.12));

                float grain = (TileFbm(uv, frac(phase * 6.0 + 0.83), seed + 251.0) - 0.5) * 0.03;
                float vignette = saturate(1.0 - dot(uv - 0.5, uv - 0.5) * 0.7);
                color += grain;
                color *= lerp(0.94, 1.02, vignette);

                return half4(saturate(color), 1.0);
            }
            ENDHLSL
        }
    }
}
