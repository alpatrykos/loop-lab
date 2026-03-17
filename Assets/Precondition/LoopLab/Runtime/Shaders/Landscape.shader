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
                float ridge = sin((input.uv.x + _LoopVector.x * 0.12 + _Phase * 0.5) * _GridScale * fullTurn);
                float horizon = smoothstep(0.08, 0.9, input.uv.y + _LoopVector.y * 0.04);
                float fog = smoothstep(0.0, 0.75, input.uv.y);
                float grain = frac((input.uv.x + input.uv.y + _Seed * 0.001) * 31.0);

                float3 color = lerp(_AccentColor.rgb, _BaseColor.rgb, horizon);
                color += ridge * 0.08;
                color = lerp(color, _AccentColor.rgb * 1.05, fog * 0.18);
                color += grain * 0.02;

                return half4(saturate(color), 1.0);
            }
            ENDHLSL
        }
    }
}
