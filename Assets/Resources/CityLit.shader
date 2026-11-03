// The city's shader. Everything in the city is built from coloured boxes, cylinders and cones merged into a few meshes, so colour comes from
// the vertex colour. Facade windows are drawn here from the UVs (no textures): uv0 holds the window tile coordinates (one tile = 2x2 windows),
// uv1.x the facade kind (0 none, 1 apartment, 2 glass, 3 shop) and uv1.y how self-lit the surface is (signs, lamps).
// One sun with soft shadows plus sky/ground ambient light. It lives in Resources so builds always include it.
Shader "TradingEmpire/CityLit"
{
    Properties
    {
        _Tint ("Tint", Color) = (1,1,1,1)
        _TintAmt ("Tint amount", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }

        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "AutoLight.cginc"

            fixed4 _Tint;
            half _TintAmt;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                fixed4 color : COLOR;
                float2 uv0 : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                half3 n : TEXCOORD0;
                fixed4 col : COLOR;
                float2 uv0 : TEXCOORD1;
                float2 kind : TEXCOORD2;
                float3 wpos : TEXCOORD3;
                UNITY_SHADOW_COORDS(4)
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.n = UnityObjectToWorldNormal(v.normal);
                o.col = v.color;
                o.uv0 = v.uv0;
                o.kind = v.uv1;
                o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                UNITY_TRANSFER_SHADOW(o, v.uv1);
                return o;
            }

            // window colours (the same ones the web version paints)
            static const fixed3 APT_FRAME = fixed3(0.208, 0.278, 0.353), APT_LIT = fixed3(1.0, 0.851, 0.541), APT_DARK = fixed3(0.561, 0.710, 0.839);
            static const fixed3 GLASS_LIT = fixed3(1.0, 0.890, 0.627), GLASS_DARK = fixed3(0.247, 0.384, 0.522);
            static const fixed3 SHOP_FRAME = fixed3(0.169, 0.227, 0.290), SHOP_LIT = fixed3(1.0, 0.914, 0.659), SHOP_DARK = fixed3(0.494, 0.651, 0.769);

            bool inRect(float2 p, float x0, float y0, float x1, float y1) { return p.x >= x0 && p.x <= x1 && p.y >= y0 && p.y <= y1; }

            fixed3 facade(float2 uv, int kind, fixed3 wall)
            {
                // one tile = a 2x2 block of 32px windows; the first one is lit
                float2 t = float2(frac(uv.x), 1.0 - frac(uv.y));
                float2 cell = floor(t * 2.0);
                float2 p = frac(t * 2.0) * 32.0;
                bool lit = cell.x < 0.5 && cell.y < 0.5;
                fixed3 c = wall;
                if (kind == 1)
                {
                    if (inRect(p, 7, 6, 25, 26)) c = APT_FRAME;
                    if (inRect(p, 9, 8, 23, 24)) c = lit ? APT_LIT : APT_DARK;
                }
                else if (kind == 2)
                {
                    if (inRect(p, 2, 4, 30, 28))
                    {
                        c = lit ? GLASS_LIT : GLASS_DARK;
                        if (p.y <= 10) c = lerp(c, fixed3(1, 1, 1), 0.18);
                    }
                }
                else if (kind == 3)
                {
                    if (inRect(p, 3, 8, 29, 28)) c = SHOP_FRAME;
                    if (inRect(p, 5, 10, 27, 26)) c = lit ? SHOP_LIT : SHOP_DARK;
                }
                return c;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                half3 n = normalize(i.n);
                fixed3 albedo = i.col.rgb;
                int kind = (int)(i.kind.x + 0.5);
                if (kind > 0) albedo = facade(i.uv0, kind, albedo);
                albedo = lerp(albedo, _Tint.rgb, _TintAmt);
                UNITY_LIGHT_ATTENUATION(atten, i, i.wpos);
                half nl = saturate(dot(n, _WorldSpaceLightPos0.xyz));
                half3 light = nl * atten * _LightColor0.rgb + ShadeSH9(half4(n, 1));
                half3 lit = albedo * light;
                lit = lerp(lit, albedo * 1.25, i.kind.y); // signs and lamps glow
                return fixed4(lit, 1);
            }
            ENDCG
        }

        Pass
        {
            Tags { "LightMode"="ShadowCaster" }
            CGPROGRAM
            #pragma vertex vertS
            #pragma fragment fragS
            #pragma multi_compile_shadowcaster
            #include "UnityCG.cginc"

            struct v2fS { V2F_SHADOW_CASTER; };

            v2fS vertS(appdata_base v)
            {
                v2fS o;
                TRANSFER_SHADOW_CASTER_NORMALOFFSET(o)
                return o;
            }

            float4 fragS(v2fS i) : SV_Target { SHADOW_CASTER_FRAGMENT(i) }
            ENDCG
        }
    }
}
