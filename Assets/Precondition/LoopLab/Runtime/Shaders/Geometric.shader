Shader "LoopLab/Geometric"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.08, 0.09, 0.13, 1)
        _AccentColor ("Accent Color", Color) = (0.95, 0.44, 0.18, 1)
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

            half4 Frag(Varyings input) : SV_Target
            {
                const float fullTurn = 6.28318530718;
                float2 grid = frac((input.uv + _LoopVector.xy * 0.05) * _GridScale);
                float edgeDistance = min(min(grid.x, 1.0 - grid.x), min(grid.y, 1.0 - grid.y));
                float stroke = smoothstep(0.04, 0.08, edgeDistance);

                float checker = frac(floor(input.uv.x * _GridScale + _Seed * 0.001) + floor(input.uv.y * _GridScale));
                float pulse = 0.5 + 0.5 * sin((input.uv.x - input.uv.y + _Phase) * fullTurn * 2.0);

                float blend = saturate((1.0 - stroke) * 0.7 + checker * 0.2 + pulse * 0.3);
                float3 color = lerp(_BaseColor.rgb, _AccentColor.rgb, blend);

                return half4(saturate(color), 1.0);
            }
            ENDHLSL
        }
    }
}
