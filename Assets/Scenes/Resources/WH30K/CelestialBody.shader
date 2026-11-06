Shader "WH30K/CelestialBody"
{
    Properties { _Color("Surface",Color)=(.5,.48,.43,1) _Gas("Gas bands",Float)=0 }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        CGPROGRAM
        #pragma surface surf Lambert
        #pragma target 3.0
        fixed4 _Color;float _Gas;
        struct Input { float3 worldPos;float3 worldNormal; };
        float hash(float3 p) {p=frac(p*.1031);p+=dot(p,p.yzx+33.33);return frac((p.x+p.y)*p.z);}
        float noise(float3 p)
        {
            float3 c=floor(p),f=frac(p);f=f*f*(3-2*f);
            return lerp(lerp(lerp(hash(c),hash(c+float3(1,0,0)),f.x),lerp(hash(c+float3(0,1,0)),hash(c+float3(1,1,0)),f.x),f.y),
                lerp(lerp(hash(c+float3(0,0,1)),hash(c+float3(1,0,1)),f.x),lerp(hash(c+float3(0,1,1)),hash(c+1),f.x),f.y),f.z);
        }
        void surf(Input IN,inout SurfaceOutput o)
        {
            float3 n=normalize(IN.worldNormal);float3 p=n*15,cell=floor(p);
            float nearest=2;
            [unroll] for(int x=-1;x<=1;x++) [unroll] for(int y=-1;y<=1;y++) [unroll] for(int z=-1;z<=1;z++)
            {
                float3 c=cell+float3(x,y,z);
                float3 center=c+float3(hash(c+13),hash(c+37),hash(c+71));
                nearest=min(nearest,length(p-center));
            }
            float basin=1-smoothstep(.20,.43,nearest);
            float rim=smoothstep(.30,.39,nearest)*(1-smoothstep(.39,.49,nearest));
            float mottling=noise(n*5)*.55+noise(n*17)*.3+noise(n*63)*.15;
            float rock=(.62+mottling*.5)*(1-basin*.35)+rim*.18;
            float bands=.7+.2*sin(n.y*85+noise(n*9)*3)+.1*sin(n.y*210);
            o.Albedo=_Color.rgb*lerp(rock,bands,_Gas);
        }
        ENDCG
    }
    Fallback "Diffuse"
}
