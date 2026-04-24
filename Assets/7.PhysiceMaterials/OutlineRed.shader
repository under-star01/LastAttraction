Shader "Custom/OutlineRed"
{
    Properties
    {
        _OutlineWidth("Outline Width", Float) = 0.03
    }
        SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Outline"
            Cull Front          // 앞면 제거 → 뒤집힌 노멀만 렌더링
            ZWrite On
            ZTest LEqual        // 벽에 가리면 안 보임

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _OutlineWidth;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                // 오브젝트 공간에서 노멀 방향으로 버텍스를 밀어냄
                float3 offset = IN.normalOS * _OutlineWidth;
                float4 pos = float4(IN.positionOS.xyz + offset, 1.0);
                OUT.positionHCS = TransformObjectToHClip(pos);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                return half4(1, 0, 0, 1); // 빨간색
            }
            ENDHLSL
        }
    }
}