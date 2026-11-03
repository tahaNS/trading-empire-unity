// A tiny lit shader for the city's boxes: one directional light plus ambient. It lives in Resources so it is always included in a build
// (the standard shader was being stripped from builds, which showed every 3D surface in magenta).
Shader "TradingEmpire/CityLit"
{
    Properties { _Color ("Color", Color) = (1,1,1,1) }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "UnityLightingCommon.cginc"

            fixed4 _Color;

            struct v2f { float4 pos : SV_POSITION; half3 n : TEXCOORD0; };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.n = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                half3 n = normalize(i.n);
                half nl = saturate(dot(n, _WorldSpaceLightPos0.xyz));
                half3 light = nl * _LightColor0.rgb + ShadeSH9(half4(n, 1));
                return fixed4(_Color.rgb * light, 1);
            }
            ENDCG
        }
    }
    Fallback "Unlit/Color"
}
