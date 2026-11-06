Shader "WH30K/OrbitalSky"
{
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 _WorldPlanetCenterRadius;
            struct v2f { float4 pos:SV_POSITION; float3 dir:TEXCOORD0; };
            v2f vert(float4 vertex:POSITION) { v2f o; o.pos=UnityObjectToClipPos(vertex); o.dir=vertex.xyz; return o; }
            float hash(float3 p) { p=frac(p*0.1031); p+=dot(p,p.yzx+33.33); return frac((p.x+p.y)*p.z); }
            float stars(float3 d,float scale,float threshold)
            {
                float3 p=d*scale, c=floor(p); float h=hash(c);
                float size=length(frac(p)-0.5);
                float aa=max(fwidth(size),0.025);
                return step(threshold,h)*(1-smoothstep(0.08,0.08+aa,size));
            }
            fixed4 frag(v2f i):SV_Target
            {
                float3 d=normalize(i.dir);
                float3 up=normalize(_WorldSpaceCameraPos-_WorldPlanetCenterRadius.xyz);
                float altitude=length(_WorldSpaceCameraPos-_WorldPlanetCenterRadius.xyz)-_WorldPlanetCenterRadius.w;
                float atmosphere=1-smoothstep(1300,6000,altitude);
                float horizon=pow(1-abs(dot(d,up)),7)*atmosphere;
                float s=stars(d,430,0.995)*0.65+stars(d+13,170,0.997)*0.9;
                float3 base=float3(0.003,0.005,0.012);
                return float4(base+s*float3(0.68,0.77,0.92)*(1-atmosphere*0.8)+horizon*float3(0.075,0.13,0.19),1);
            }
            ENDCG
        }
    }
    Fallback Off
}
