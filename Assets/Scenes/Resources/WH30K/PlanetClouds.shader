Shader "WH30K/PlanetClouds"
{
    Properties { _PlanetCenter ("Center", Vector)=(0,0,0,0) }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        ZWrite Off Blend SrcAlpha OneMinusSrcAlpha Cull Back
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            float3 _PlanetCenter;
            struct v2f { float4 pos:SV_POSITION; float3 world:TEXCOORD0; };
            v2f vert(float4 vertex:POSITION) { v2f o; o.pos=UnityObjectToClipPos(vertex); o.world=mul(unity_ObjectToWorld,vertex).xyz; return o; }
            float hash(float3 p) { p=frac(p*0.1031); p+=dot(p,p.yzx+33.33); return frac((p.x+p.y)*p.z); }
            float noise(float3 p)
            {
                float3 c=floor(p), f=frac(p); f=f*f*(3-2*f);
                return lerp(lerp(lerp(hash(c),hash(c+float3(1,0,0)),f.x),lerp(hash(c+float3(0,1,0)),hash(c+float3(1,1,0)),f.x),f.y),
                    lerp(lerp(hash(c+float3(0,0,1)),hash(c+float3(1,0,1)),f.x),lerp(hash(c+float3(0,1,1)),hash(c+1),f.x),f.y),f.z);
            }
            fixed4 frag(v2f i):SV_Target
            {
                float3 n=normalize(i.world-_PlanetCenter);
                float3 p=n*9+float3(_Time.y*0.0008,0,_Time.y*0.0003);
                p.x+=sin(p.y*1.7)*0.65;
                float cloud=noise(p)*0.54+noise(p*2.2+7)*0.28+noise(p*5.4+13)*0.12+noise(p*12.1)*0.06;
                float alpha=smoothstep(0.53,0.68,cloud)*0.73;
                float nearFade=smoothstep(350,1800,distance(_WorldSpaceCameraPos,i.world));
                float light=saturate(dot(n,normalize(_WorldSpaceLightPos0.xyz)));
                float3 col=float3(0.72,0.78,0.81)*(0.13+light*_LightColor0.rgb);
                return float4(col,alpha*nearFade);
            }
            ENDCG
        }
    }
}
