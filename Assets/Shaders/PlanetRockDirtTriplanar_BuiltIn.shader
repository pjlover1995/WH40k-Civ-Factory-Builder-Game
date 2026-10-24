Shader "WH30K/PlanetRockDirtTriplanar_BuiltIn"
{
    Properties
    {
        _RockColor ("Rock", Color) = (0.31,0.30,0.26,1)
        _DirtColor ("Soil", Color) = (0.26,0.23,0.17,1)
        _GrassColor ("Temperate land", Color) = (0.18,0.27,0.15,1)
        _DesertColor ("Arid land", Color) = (0.57,0.43,0.25,1)
        _SnowColor ("Ice", Color) = (0.81,0.87,0.90,1)
        _PlanetRadius ("Radius", Float) = 12000
        _SeaLevel ("Sea level", Float) = 11980
        _PlanetCenter ("Center", Vector) = (0,0,0,0)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "AutoLight.cginc"
            float4 _RockColor, _DirtColor, _GrassColor, _DesertColor, _SnowColor;
            float _PlanetRadius, _SeaLevel;
            float3 _PlanetCenter;
            struct appdata { float4 vertex:POSITION; float3 normal:NORMAL; float4 color:COLOR; };
            struct v2f { float4 pos:SV_POSITION; float3 worldPos:TEXCOORD0; float3 normal:TEXCOORD1; float4 biome:COLOR; SHADOW_COORDS(2) };
            v2f vert(appdata v)
            {
                v2f o; o.pos=UnityObjectToClipPos(v.vertex);
                o.worldPos=mul(unity_ObjectToWorld,v.vertex).xyz;
                o.normal=UnityObjectToWorldNormal(v.normal); o.biome=v.color; TRANSFER_SHADOW(o); return o;
            }
            float hash(float3 p) { p=frac(p*0.1031); p+=dot(p,p.yzx+33.33); return frac((p.x+p.y)*p.z); }
            float noise(float3 p)
            {
                float3 c=floor(p), f=frac(p); f=f*f*(3-2*f);
                return lerp(lerp(lerp(hash(c),hash(c+float3(1,0,0)),f.x),
                    lerp(hash(c+float3(0,1,0)),hash(c+float3(1,1,0)),f.x),f.y),
                    lerp(lerp(hash(c+float3(0,0,1)),hash(c+float3(1,0,1)),f.x),
                    lerp(hash(c+float3(0,1,1)),hash(c+1),f.x),f.y),f.z);
            }
            float fbm(float3 p) { return noise(p)*0.57+noise(p*2.07+17)*0.28+noise(p*4.13+41)*0.15; }
            fixed4 frag(v2f i):SV_Target
            {
                float3 p=i.worldPos-_PlanetCenter, radial=normalize(p), n=normalize(i.normal);
                float height=length(p)-_SeaLevel;
                float climate=fbm(radial*5.3+23);
                float latitude=abs(radial.y);
                float arid=smoothstep(0.46,0.68,climate)*(1-smoothstep(0.5,0.8,latitude));
                float3 vegetation=lerp(_GrassColor.rgb*0.68,_GrassColor.rgb*1.25,fbm(radial*34));
                float3 albedo=i.biome.rgb*lerp(0.8,1.2,fbm(radial*190));
                float slope=saturate((1-dot(n,radial))*12);
                albedo=lerp(albedo,lerp(_DirtColor.rgb,_RockColor.rgb,0.7),saturate(slope+smoothstep(220,620,height)*0.7));
                albedo=lerp(albedo,float3(0.49,0.45,0.31),(1-smoothstep(0,9,height))*0.7);
                float snow=smoothstep(0.78,0.94,latitude+(climate-0.5)*0.1);
                snow=max(snow,smoothstep(600,900,height));
                albedo=lerp(albedo,_SnowColor.rgb,snow);
                float detailFade=1-smoothstep(1200,7500,distance(_WorldSpaceCameraPos,i.worldPos));
                albedo*=lerp(0.94,lerp(0.80,1.10,fbm(p*0.028)),detailFade);
                float3 lightDir=normalize(_WorldSpaceLightPos0.xyz);
                float diffuse=saturate(dot(n,lightDir));
                float shadow=SHADOW_ATTENUATION(i);
                float3 lit=albedo*(float3(0.14,0.17,0.21)+_LightColor0.rgb*diffuse*shadow);
                float3 view=normalize(_WorldSpaceCameraPos-i.worldPos);
                float rim=pow(1-saturate(dot(radial,view)),4)*saturate(dot(radial,lightDir)+0.35);
                lit+=float3(0.13,0.26,0.39)*rim*0.55;
                lit=lerp(lit,float3(0.16,0.23,0.28),saturate(distance(_WorldSpaceCameraPos,i.worldPos)/18000)*0.12);
                return float4(lit,1);
            }
            ENDCG
        }
    }
    Fallback "Diffuse"
}
