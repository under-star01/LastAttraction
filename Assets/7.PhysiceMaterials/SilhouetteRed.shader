Shader "Custom/SilhouetteRed"
{
    Properties
    {
        _Color("Silhouette Color", Color) = (1, 0, 0, 0.4)
    }
        SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent"
               "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Silhouette"
            Cull Back
            ZWrite Off
            ZTest Always            // ¡ç º® ¶Õ°í º¸ÀÌ´Â ÇÙ½É
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionHCS : SV_POSITION; };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                return _Color;
            }
            ENDHLSL
        }
    }
}