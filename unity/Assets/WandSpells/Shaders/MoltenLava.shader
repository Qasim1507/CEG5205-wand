Shader "Wand/MoltenLava"
{
    Properties
    {
        _MainTex ("Stone detail", 2D) = "white" {}
        _Color ("Cooled crust", Color) = (0.08,0.035,0.045,1)
        _FlowColor ("Molten flow", Color) = (1,0.15,0.025,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 100
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            fixed4 _FlowColor;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }
            float hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }
            float noise(float2 p)
            {
                float2 cell = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(hash(cell), hash(cell + float2(1,0)), f.x),
                            lerp(hash(cell + float2(0,1)), hash(cell + 1), f.x), f.y);
            }
            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.uv;
                float t = _Time.y;
                float2 flow = uv * 4.5 + float2(t * .14, -t * .07);
                float2 warp = float2(noise(flow * .75), noise(flow * .75 + 7.3)) - .5;
                float2 p = flow + warp * 1.5;
                float n = noise(p * 2.0) * .58 + noise(p * 4.4 + t * .11) * .28 +
                          noise(p * 8.1 - t * .18) * .14;
                float crust = smoothstep(.57, .74, n);
                float hot = pow(saturate(1.0 - abs(n - .43) * 2.5), 3.0);
                float4 stoneDetail = tex2D(_MainTex, uv * .35 + warp * .05);
                fixed3 darkRock = _Color.rgb * (.7 + stoneDetail.r * .45);
                fixed3 molten = lerp(float3(.24, .018, .006), _FlowColor.rgb * 1.8,
                    saturate(hot * 1.35 + .23));
                fixed3 surface = lerp(molten, darkRock, crust * .9);
                float vein = pow(saturate(1.0 - abs(n - .49) * 8.0), 3.0);
                surface += float3(1.0, .67, .10) * vein * 1.35;
                return fixed4(surface, 1);
            }
            ENDCG
        }
    }
    FallBack "Diffuse"
}
