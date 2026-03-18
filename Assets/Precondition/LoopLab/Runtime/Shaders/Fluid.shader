Shader "LoopLab/Fluid"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.04, 0.25, 0.52, 1)
        _AccentColor ("Accent Color", Color) = (0.18, 0.84, 0.88, 1)
        _GridScale ("Grid Scale", Float) = 6
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
            #include "Assets/Precondition/LoopLab/Runtime/Shaders/LoopLabLooping.hlsl"

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

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            float TileBand(float2 centeredUv, float2 primaryFrequency, float2 secondaryFrequency)
            {
                float primary =
                    cos(centeredUv.x * LoopLabFullTurn * primaryFrequency.x) *
                    cos(centeredUv.y * LoopLabFullTurn * primaryFrequency.y);
                float secondary =
                    cos(centeredUv.x * LoopLabFullTurn * secondaryFrequency.x) *
                    cos(centeredUv.y * LoopLabFullTurn * secondaryFrequency.y);
                return primary * 0.58 + secondary * 0.42;
            }

            float TileField(float2 centeredUv, float phase, float seed)
            {
                float seedBias = frac(seed * 0.017);
                float octaveA = lerp(
                    TileBand(centeredUv, float2(1.0, 2.0), float2(2.0, 1.0)),
                    TileBand(centeredUv, float2(1.0, 3.0), float2(2.0, 2.0)),
                    LoopLabBlendWeight(phase, 1.0, 0.11 + seedBias * 0.31));
                float octaveB = lerp(
                    TileBand(centeredUv, float2(2.0, 3.0), float2(3.0, 2.0)),
                    TileBand(centeredUv, float2(3.0, 3.0), float2(4.0, 2.0)),
                    LoopLabBlendWeight(phase, 2.0, 0.23 + seedBias * 0.23));
                float octaveC = lerp(
                    TileBand(centeredUv, float2(4.0, 3.0), float2(3.0, 5.0)),
                    TileBand(centeredUv, float2(5.0, 4.0), float2(4.0, 6.0)),
                    LoopLabBlendWeight(phase, 3.0, 0.37 + seedBias * 0.17));
                float octaveD = lerp(
                    TileBand(centeredUv, float2(6.0, 5.0), float2(5.0, 7.0)),
                    TileBand(centeredUv, float2(7.0, 6.0), float2(8.0, 8.0)),
                    LoopLabBlendWeight(phase, 4.0, 0.49 + seedBias * 0.13));
                return octaveA * 0.36 + octaveB * 0.28 + octaveC * 0.21 + octaveD * 0.15;
            }

            float FlowPotential(float2 centeredUv, float phase, float seed)
            {
                float primary = TileField(centeredUv, phase, seed);
                float secondary = TileField(centeredUv, frac(phase * 2.0 + 0.19), seed + 17.0);
                float tertiary = TileField(centeredUv, frac(phase * 3.0 + 0.37), seed + 41.0);
                return primary * 0.56 + secondary * 0.29 + tertiary * 0.15;
            }

            float2 CurlLikeFlow(float2 centeredUv, float phase, float seed)
            {
                const float derivativeStep = 0.01;
                float potentialUp = FlowPotential(centeredUv + float2(0.0, derivativeStep), phase, seed);
                float potentialDown = FlowPotential(centeredUv - float2(0.0, derivativeStep), phase, seed);
                float potentialRight = FlowPotential(centeredUv + float2(derivativeStep, 0.0), phase, seed);
                float potentialLeft = FlowPotential(centeredUv - float2(derivativeStep, 0.0), phase, seed);

                float derivativeY = (potentialUp - potentialDown) / (2.0 * derivativeStep);
                float derivativeX = (potentialRight - potentialLeft) / (2.0 * derivativeStep);
                return float2(derivativeY, -derivativeX);
            }

            float FluidDensity(float2 centeredUv, float phase, float seed)
            {
                float body = TileField(centeredUv, phase, seed);
                float contour = TileField(centeredUv, frac(phase * 2.0 + 0.17), seed + 67.0);
                float ribbon = TileBand(centeredUv, float2(6.0, 2.0), float2(5.0, 3.0));
                float eddy = TileBand(centeredUv, float2(2.0, 6.0), float2(3.0, 5.0));
                return body * 0.44 + contour * 0.28 + ribbon * 0.18 + eddy * 0.10;
            }

            float FluidRidge(float2 centeredUv, float phase, float seed)
            {
                float primary = 1.0 - abs(TileField(centeredUv, phase, seed) * 2.0 - 1.0);
                float secondary = 1.0 - abs(TileField(centeredUv, frac(phase * 2.0 + 0.29), seed + 97.0) * 2.0 - 1.0);
                return saturate(primary * primary * 0.62 + secondary * secondary * 0.38);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float phase = LoopLabPhase01(_Phase);
                float2 loopVector = _LoopVector.xy;
                float seed = _Seed * 0.071;
                float durationBlend = saturate((_Duration - 1.8) * 0.45);
                float2 centered = input.uv - 0.5;

                float2 primaryDomain = LoopLabApplyOrbit(centered, loopVector, float2(0.048, -0.027), float2(-0.019, 0.035));
                float2 detailDomain = LoopLabApplyOrbit(centered, loopVector, float2(-0.024, 0.018), float2(0.029, 0.021));
                float2 shadowDomain = LoopLabApplyOrbit(centered, loopVector, float2(0.012, -0.008), float2(0.007, 0.014));

                float2 primaryFlow = CurlLikeFlow(primaryDomain, frac(phase * 1.0 + 0.07), seed + 13.0);
                float2 secondaryFlow = CurlLikeFlow(detailDomain + primaryFlow * 0.18, frac(phase * 2.0 + 0.23), seed + 31.0);
                float2 tertiaryFlow = CurlLikeFlow(primaryDomain - detailDomain * 0.22, frac(phase * 3.0 + 0.41), seed + 59.0);

                float flowMagnitude = saturate(length(primaryFlow) * 0.34 + length(secondaryFlow) * 0.24 + length(tertiaryFlow) * 0.16);
                float swirlMask = saturate(abs(primaryFlow.x) * 0.28 + abs(secondaryFlow.y) * 0.24 + abs(tertiaryFlow.x * tertiaryFlow.y) * 0.18);
                float density = FluidDensity(primaryDomain + secondaryFlow * lerp(0.03, 0.05, durationBlend), phase, seed);
                float interiorField = FluidDensity(detailDomain - primaryFlow * 0.04, frac(phase * 2.0 + 0.27), seed + 83.0);
                float highlightField = FluidRidge(primaryDomain + tertiaryFlow * 0.05, frac(phase * 4.0 + 0.49), seed + 109.0);
                float shadowField = TileField(shadowDomain, frac(phase * 0.5 + 0.61), seed + 139.0);

                float body = smoothstep(-0.25, 0.72, density + interiorField * 0.24 + flowMagnitude * 0.22);
                float highlight = smoothstep(0.16, 0.94, highlightField * 0.58 + body * 0.22 + flowMagnitude * 0.24);
                float interiorBlend = smoothstep(-0.78, 0.68, density * 0.58 + interiorField * 0.42);
                float depth = smoothstep(-0.74, 0.52, density * 0.46 - shadowField * 0.24 + swirlMask * 0.22);
                float phaseLift = 0.5 + 0.5 * LoopLabSinWave(phase, 2.0, density * 0.35 + interiorField * 0.27);
                float filament = smoothstep(0.18, 0.82, highlightField * 0.54 + abs(density - interiorField) * 0.26 + swirlMask * 0.12);

                float3 baseColor = saturate(_BaseColor.rgb);
                float3 accentColor = saturate(_AccentColor.rgb);
                float3 deepColor = baseColor * 0.72 + accentColor * 0.08;
                float3 poolColor = lerp(baseColor * 0.60, accentColor * 0.40, depth * 0.44 + interiorBlend * 0.10);
                float3 innerColor = lerp(poolColor, accentColor, body * 0.60 + interiorBlend * 0.16);
                float3 highlightColor = lerp(accentColor * 1.08, float3(0.88, 0.97, 1.0), 0.52);

                float3 color = lerp(deepColor, innerColor, body);
                color = lerp(color, baseColor * 0.46 + accentColor * 0.34, depth * 0.24);
                color += highlightColor * highlight * (0.12 + phaseLift * 0.08);
                color += accentColor * filament * 0.07;
                color += accentColor * flowMagnitude * 0.06;
                color = lerp(color, deepColor, smoothstep(0.34, 0.90, shadowField * 0.5 + 0.5) * 0.18);

                return half4(saturate(color), 1.0);
            }
            ENDHLSL
        }
    }
}
