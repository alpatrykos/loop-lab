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

            float2 Rotate(float2 value, float angle)
            {
                float sine;
                float cosine;
                sincos(angle, sine, cosine);
                return float2(
                    cosine * value.x - sine * value.y,
                    sine * value.x + cosine * value.y);
            }

            float FlowPotential(float2 position, float2 loopVector, float seedOffset)
            {
                float2 primaryPosition = position * (_GridScale * 0.82);
                float primary = sin(dot(primaryPosition, float2(1.34, -1.57)) + loopVector.x * 1.9 + loopVector.y * 1.3 + seedOffset);

                float2 secondaryPosition = Rotate(position, 0.82 + seedOffset * 0.09) * (_GridScale * 1.12);
                float secondary = cos(dot(secondaryPosition, float2(-0.91, 1.62)) - loopVector.y * 2.15 + loopVector.x * 0.95 + seedOffset * 1.7);

                float2 tertiaryPosition = Rotate(position, -1.17) * (_GridScale * 0.62);
                float tertiary = sin(tertiaryPosition.x * 1.76 - tertiaryPosition.y * 1.21 + (loopVector.x - loopVector.y) * 2.4 + seedOffset * 0.63);

                return primary * 0.52 + secondary * 0.31 + tertiary * 0.17;
            }

            float2 CurlLikeFlow(float2 position, float2 loopVector, float seedOffset)
            {
                const float derivativeStep = 0.12;
                float potentialUp = FlowPotential(position + float2(0.0, derivativeStep), loopVector, seedOffset);
                float potentialDown = FlowPotential(position - float2(0.0, derivativeStep), loopVector, seedOffset);
                float potentialRight = FlowPotential(position + float2(derivativeStep, 0.0), loopVector, seedOffset);
                float potentialLeft = FlowPotential(position - float2(derivativeStep, 0.0), loopVector, seedOffset);

                float derivativeY = (potentialUp - potentialDown) / (2.0 * derivativeStep);
                float derivativeX = (potentialRight - potentialLeft) / (2.0 * derivativeStep);
                return float2(derivativeY, -derivativeX);
            }

            float FluidDensity(float2 position, float2 loopVector, float seedOffset)
            {
                float bandA = sin(dot(position, float2(1.48, -1.18)) * _GridScale + FlowPotential(position * 0.74, loopVector, seedOffset) * 1.42 + loopVector.x * 1.45);
                float bandB = cos(dot(Rotate(position, 1.08), float2(1.0, 1.58)) * (_GridScale * 0.62) - FlowPotential(position * 1.04, loopVector, seedOffset + 1.9) * 1.08 + loopVector.y * 1.24);
                float bandC = sin((position.x + position.y * 0.7) * (_GridScale * 0.86) + (loopVector.x + loopVector.y) * 1.1 + seedOffset * 1.3);
                return bandA * 0.56 + bandB * 0.28 + bandC * 0.16;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                const float fullTurn = 6.28318530718;
                float seedOffset = _Seed * 0.00091;
                float phaseAngle = _Phase * fullTurn;
                float2 loopVector = _LoopVector.xy;
                float2 centered = input.uv * 2.0 - 1.0;
                centered.x *= 1.06;

                float durationBlend = saturate((_Duration - 2.0) * 0.5);
                float2 domain = Rotate(centered, loopVector.x * 0.18 + seedOffset * 0.4);
                float2 primaryFlow = CurlLikeFlow(domain, loopVector, seedOffset);
                float2 advected = domain + primaryFlow * lerp(0.09, 0.13, durationBlend);

                float2 rotatedLoop = float2(-loopVector.y, loopVector.x);
                float2 secondaryFlow = CurlLikeFlow(advected * 1.31 + float2(0.18, -0.07), rotatedLoop, seedOffset + 2.7);
                advected += secondaryFlow * lerp(0.035, 0.065, durationBlend);

                float2 tertiaryFlow = CurlLikeFlow(advected * 1.72 - primaryFlow * 0.26, float2(loopVector.y, -loopVector.x), seedOffset + 5.1);
                advected += tertiaryFlow * 0.02;

                float flowMagnitude = saturate(length(primaryFlow) * 0.34 + length(secondaryFlow) * 0.26 + length(tertiaryFlow) * 0.2);
                float density = FluidDensity(advected, loopVector, seedOffset);
                float body = smoothstep(-0.32, 0.78, density + flowMagnitude * 0.42);

                float highlightField = FluidDensity(advected * 1.48 - secondaryFlow * 0.3 + float2(0.06, -0.04), rotatedLoop, seedOffset + 7.4);
                float highlight = smoothstep(0.22, 0.88, body * 0.58 + flowMagnitude * 0.72 + highlightField * 0.24);

                float interiorBlend = smoothstep(-0.85, 0.62, density - flowMagnitude * 0.28);
                float radius = length(centered);
                float depth = smoothstep(1.28, 0.08, radius + density * 0.08);
                float phaseLift = 0.5 + 0.5 * sin(phaseAngle + dot(advected, float2(0.85, -1.1)) * 0.9);

                float3 baseBlend = lerp(_BaseColor.rgb * 0.82, _AccentColor.rgb, body);
                float3 innerColor = lerp(baseBlend, _BaseColor.rgb * 0.56 + _AccentColor.rgb * 0.38, interiorBlend * 0.24);
                float3 highlightColor = lerp(_AccentColor.rgb * 1.05, float3(0.88, 0.97, 1.0), 0.55);
                float3 color = lerp(_BaseColor.rgb * 0.74, innerColor, depth);
                color += highlightColor * highlight * (0.18 + phaseLift * 0.08);
                color += flowMagnitude * (_AccentColor.rgb * 0.06);
                color = lerp(color, _BaseColor.rgb * 0.68, smoothstep(0.92, 1.35, radius));

                return half4(saturate(color), 1.0);
            }
            ENDHLSL
        }
    }
}
