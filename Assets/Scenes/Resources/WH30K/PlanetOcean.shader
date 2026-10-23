Shader "WH30K/PlanetOcean"
{
    Properties { _PlanetCenter ("Center", Vector)=(0,0,0,0) }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry+1" }
        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            float3 _PlanetCenter;
            struct appdata { float4 vertex:POSITION; float3 normal:NORMAL; float4 color:COLOR; };
            struct v2f { float4 pos:SV_POSITION; float3 world:TEXCOORD0; float3 normal:TEXCOORD1; float depth:TEXCOORD2; };
            v2f vert(appdata v) { v2f o; o.pos=UnityObjectToClipPos(v.vertex); o.world=mul(unity_ObjectToWorld,v.vertex).xyz; o.normal=UnityObjectToWorldNormal(v.normal); o.depth=v.color.r; return o; }
            fixed4 frag(v2f i):SV_Target
            {
                float3 p=normalize(i.world-_PlanetCenter), n=normalize(i.normal);
                float3 v=normalize(_WorldSpaceCameraPos-i.world), l=normalize(_WorldSpaceLightPos0.xyz);
                float wave=sin(p.x*2300+_Time.y*0.3)*sin(p.z*1900-_Time.y*0.22);
                float close=1-smoothstep(1500,9000,distance(_WorldSpaceCameraPos,i.world));
                n=normalize(n+float3(wave,0,-wave)*0.012*close);
                float fresnel=pow(1-saturate(dot(n,v)),4);
                float3 water=lerp(float3(0.035,0.22,0.24),float3(0.012,0.055,0.095),saturate(i.depth));
                float diffuse=saturate(dot(n,l));
                float spec=pow(saturate(dot(n,normalize(l+v))),160)*diffuse;
                float3 col=water*(0.17+diffuse*_LightColor0.rgb);
                col+=spec*_LightColor0.rgb*0.65+fresnel*float3(0.07,0.16,0.22)*diffuse;
                return float4(col,1);
            }
            ENDCG
        }
    }
}
