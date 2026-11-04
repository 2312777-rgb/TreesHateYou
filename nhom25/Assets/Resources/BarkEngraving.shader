Shader "TreesHateYou/Bark Engraving"
{
    Properties
    {
        _MainTex ("Glyph atlas", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _MainTex_TexelSize;

            struct Varyings
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(appdata_base input)
            {
                Varyings output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.uv = input.texcoord.xy;
                return output;
            }

            fixed4 frag(Varyings input) : SV_Target
            {
                float2 offset = _MainTex_TexelSize.xy * float2(0.8, 1.4);
                float glyph = tex2D(_MainTex, input.uv).a;
                float upperWall = saturate(glyph - tex2D(_MainTex, input.uv - offset).a);
                float lowerWall = saturate(glyph - tex2D(_MainTex, input.uv + offset).a);
                float3 groove = lerp(float3(0.26, 0.145, 0.065), float3(0.055, 0.025, 0.012), upperWall);
                groove = lerp(groove, float3(0.89, 0.67, 0.39), lowerWall * (1.0 - upperWall));
                float opacity = glyph * lerp(0.78, 1.0, max(upperWall, lowerWall));
                return fixed4(groove, opacity);
            }
            ENDCG
        }
    }
}
