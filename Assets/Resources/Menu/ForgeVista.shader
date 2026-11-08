Shader "WH30K/Menu/ForgeVista"
{
    Properties
    {
        [PerRendererData] _MainTex ("Forge plate", 2D) = "white" {}
        _Motion ("Animation tier", Float) = 1
        _MenuTime ("Unscaled menu time", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "CanUseSpriteAtlas"="False" }
        Cull Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float _Motion, _MenuTime;
            float4 _ViewRect, _IdentityArea;
            struct input { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct output { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            output vert(input v)
            {
                output o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }
            fixed4 frag(output i) : SV_Target
            {
                float2 uv = i.uv;
                half3 art = tex2D(_MainTex, uv).rgb;
                float2 screenUV = (uv - _ViewRect.xy) / max(_ViewRect.zw, .001);
                float2 title = float2(screenUV.x, 1 - screenUV.y) / max(_IdentityArea.xy, .001);
                art *= 1 - (1 - smoothstep(.6, 1, title.x)) * (1 - smoothstep(.35, 1, title.y)) * .47;
                if (_Motion > .5)
                {
                    // Key only hot orange pixels in the foundry, preserving stone and silhouettes.
                    half hot = smoothstep(.12, .38, art.r - art.b) * smoothstep(.55, .91, art.r);
                    half foundry = smoothstep(.68, .85, uv.x) * (1 - smoothstep(.60, .74, uv.y));
                    float stream = sin(uv.y * 151 + _MenuTime * 5 + uv.x * 70);
                    float flicker = sin(_MenuTime * 1.23 + uv.x * 26) * .032;
                    art += half3(.95, .43, .14) * hot * foundry * (stream * .06 + flicker);
                    if (_Motion > 1.5)
                    {
                        float2 haze = (uv - float2(.76 + sin(_MenuTime * .11) * .09, .36)) * float2(6, 11);
                        art += half3(.52, .34, .23) * exp(-dot(haze, haze)) * (.015 + .008 * sin(_MenuTime * .5));
                    }
                }
                return fixed4(art, 1) * i.color;
            }
            ENDCG
        }
    }
}
