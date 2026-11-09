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
                float atmosphere=1-smoothstep(3000,18000,altitude);
                float elevation=saturate(dot(d,up));
                float horizon=pow(1-elevation,2.2)*atmosphere;
                float s=stars(d,430,0.995)*0.65+stars(d+13,170,0.997)*0.9;
                float3 space=float3(0.035,0.065,0.105);
                float3 air=lerp(float3(0.18,0.29,0.38),float3(0.08,0.15,0.23),elevation);
                float3 base=lerp(space,air,atmosphere);
                return float4(base+s*float3(0.68,0.77,0.92)*(1-atmosphere*0.8)+horizon*float3(0.11,0.21,0.30),1);
            }
            ENDCG
        }
    }
    Fallback Off
}
