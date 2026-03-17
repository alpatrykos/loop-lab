Shader "LoopLab/Geometric"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.04, 0.05, 0.08, 1)
        _AccentColor ("Accent Color", Color) = (0.97, 0.76, 0.27, 1)
        _GridScale ("Grid Scale", Float) = 8
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

            float2 WrapPeriodic(float2 position, float2 period)
            {
                return position - period * floor(position / period);
            }

            float2 Rotate2D(float2 position, float angleRadians)
            {
                float sine = sin(angleRadians);
                float cosine = cos(angleRadians);
                return float2(
                    cosine * position.x - sine * position.y,
                    sine * position.x + cosine * position.y);
            }

            float Hash21(float2 samplePosition)
            {
                samplePosition = frac(samplePosition * float2(123.34, 345.45));
                samplePosition += dot(samplePosition, samplePosition + 34.345);
                return frac(samplePosition.x * samplePosition.y);
            }

            float2 GetHexCell(float2 samplePosition, out float2 cellCenter)
            {
                const float2 cellSize = float2(1.0, 1.73205080757);
                float2 halfCell = cellSize * 0.5;
                float2 first = WrapPeriodic(samplePosition, cellSize) - halfCell;
                float2 second = WrapPeriodic(samplePosition - halfCell, cellSize) - halfCell;

                if (dot(first, first) < dot(second, second))
                {
                    cellCenter = samplePosition - first;
                    return first;
                }

                cellCenter = samplePosition - second;
                return second;
            }

            float SdHexagon(float2 position, float radius)
            {
                const float3 k = float3(-0.866025404, 0.5, 0.577350269);
                position = abs(position);
                position -= 2.0 * min(dot(k.xy, position), 0.0) * k.xy;
                position -= float2(clamp(position.x, -k.z * radius, k.z * radius), radius);
                return length(position) * sign(position.y);
            }

            float FillMask(float signedDistance)
            {
                float antiAlias = max(fwidth(signedDistance) * 1.5, 0.001);
                return 1.0 - smoothstep(-antiAlias, antiAlias, signedDistance);
            }

            float StrokeMask(float signedDistance, float width)
            {
                float antiAlias = max(fwidth(signedDistance) * 1.5, 0.001);
                return 1.0 - smoothstep(width - antiAlias, width + antiAlias, abs(signedDistance));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                const float fullTurn = 6.28318530718;
                const float sqrt3 = 1.73205080757;

                float theta = _Phase * fullTurn;
                float columns = max(4.0, floor(_GridScale + 0.5));
                float rows = max(3.0, floor(columns * 0.625 + 0.5));
                float2 tileExtent = float2(columns, rows * sqrt3);

                float2 domain = float2(input.uv.x * columns, input.uv.y * rows * sqrt3);
                domain += float2(
                    _LoopVector.x * 0.34 + _LoopVector.y * 0.12,
                    _LoopVector.y * 0.26);

                float2 seedOffset = float2(_Seed * 0.0131, _Seed * 0.0177);
                float2 cellCenter;
                float2 local = GetHexCell(domain, cellCenter);
                float2 periodicCellCenter = WrapPeriodic(cellCenter, tileExtent);
                float cellHash = Hash21(periodicCellCenter + seedOffset);
                float spinDirection = cellHash >= 0.5 ? 1.0 : -1.0;
                float seedPhase = cellHash * fullTurn;

                float scalePulse = 1.0 + 0.18 * sin(theta + seedPhase);
                float2 orbit = 0.18 * float2(
                    cos(theta + seedPhase),
                    sin(theta + seedPhase));

                float2 outerDomain = Rotate2D(local + orbit, theta * spinDirection + seedPhase);
                float2 innerDomain = Rotate2D(local - orbit * 0.72, -theta * spinDirection + seedPhase);

                float outerHex = SdHexagon(outerDomain / scalePulse, 0.36);
                float innerScale = 0.63 + 0.08 * cos(theta + seedPhase);
                float innerHex = SdHexagon(innerDomain / innerScale, 0.17);

                float outerFill = FillMask(outerHex);
                float outerStroke = StrokeMask(outerHex, 0.03);
                float innerFill = FillMask(innerHex);
                float innerStroke = StrokeMask(innerHex, 0.022);

                float localAngle = atan2(outerDomain.y, outerDomain.x);
                float localRadius = length(outerDomain);
                float spokePattern = 0.5 + 0.5 * cos(localAngle * 6.0 + theta * 2.0 + seedPhase);
                float radialPattern = 0.5 + 0.5 * cos(localRadius * 18.0 - theta * 2.0 + seedPhase * 2.0);
                float detailBlend = saturate(spokePattern * 0.65 + radialPattern * 0.35);

                float bandPattern = 0.5 + 0.5 * cos((input.uv.x * 2.0 + input.uv.y) * fullTurn + theta);

                float3 shadowColor = _BaseColor.rgb * 0.72;
                float3 midColor = lerp(_BaseColor.rgb, _AccentColor.rgb, 0.32);
                float3 color = lerp(shadowColor, _BaseColor.rgb, bandPattern * 0.22 + 0.08);

                color = lerp(color, midColor, outerFill * (0.34 + detailBlend * 0.22));
                color = lerp(color, _AccentColor.rgb, outerStroke * 0.95);
                color = lerp(color, _AccentColor.rgb, innerFill * (0.55 + detailBlend * 0.25));
                color = lerp(color, shadowColor, innerStroke * 0.30);

                return half4(saturate(color), 1.0);
            }
            ENDHLSL
        }
    }
}
