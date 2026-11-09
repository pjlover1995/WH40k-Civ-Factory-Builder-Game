Shader "WH30K/PlanetRockDirtTriplanar_BuiltIn"
{
    Properties
    {
        _RockColor ("Rock", Color) = (0.31,0.30,0.26,1)
        _DirtColor ("Soil", Color) = (0.26,0.23,0.17,1)
        _GrassColor ("Temperate land", Color) = (0.18,0.27,0.15,1)
        _DesertColor ("Arid land", Color) = (0.57,0.43,0.25,1)
        _SnowColor ("Ice", Color) = (0.81,0.87,0.90,1)
        _RockTex ("Rock albedo (optional)", 2D) = "white" {}
        _SoilTex ("Soil albedo (optional)", 2D) = "white" {}
        _GrassTex ("Grass albedo (optional)", 2D) = "white" {}
        _SnowTex ("Snow albedo (optional)", 2D) = "white" {}
        _TextureScale ("Texture repeats per metre", Float) = 0.08
        _TextureStrength ("Assigned texture strength", Range(0,1)) = 1
        [Toggle(_TERRAIN_TEXTURES)] _UseTextures ("Enable assigned albedo textures", Float) = 0
        _PlanetRadius ("Radius", Float) = 6000
        _SeaLevel ("Sea level", Float) = 5980
        _PlanetCenter ("Center", Vector) = (0,0,0,0)
        [HideInInspector] _TerrainRecipe ("Recipe", Float) = 3
        [HideInInspector] _DebugOverlay ("Debug", Float) = 0
        [HideInInspector] _EarthworkBrush ("Earthwork direction and radius", Vector) = (0,0,0,0)
        [HideInInspector] _EarthworkColor ("Earthwork tool colour", Color) = (0.12,0.88,0.94,1)
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
            #pragma shader_feature_local _TERRAIN_TEXTURES
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "AutoLight.cginc"
            float4 _RockColor, _DirtColor, _GrassColor, _DesertColor, _SnowColor;
            float _PlanetRadius, _SeaLevel, _TerrainRecipe, _DebugOverlay, _TextureScale, _TextureStrength;
            sampler2D _RockTex, _SoilTex, _GrassTex, _SnowTex;
            float3 _PlanetCenter;
            float4 _EarthworkBrush, _EarthworkColor;
            float4 _PlanetAtmosphericFog;
            float4 _PlanetAtmosphericFogColor;
            struct appdata { float4 vertex:POSITION; float3 normal:NORMAL; float4 color:COLOR; float2 climate:TEXCOORD1; };
            struct v2f { float4 pos:SV_POSITION; float3 worldPos:TEXCOORD0; float3 normal:TEXCOORD1; float4 biome:COLOR; float2 climate:TEXCOORD3; SHADOW_COORDS(2) };
            v2f vert(appdata v)
            {
                v2f o; o.pos=UnityObjectToClipPos(v.vertex);
                o.worldPos=mul(unity_ObjectToWorld,v.vertex).xyz;
                o.normal=UnityObjectToWorldNormal(v.normal); o.biome=v.color; o.climate=v.climate; TRANSFER_SHADOW(o); return o;
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
            float3 triplanar(sampler2D tex, float3 p, float3 weights)
            {
                // Macro distortion breaks the regular texture grid; a rotated second scale
                // hides repeating features. White defaults leave procedural art unchanged.
                p += (fbm(p*0.031)-0.5)*2.7;
                float3 a=tex2D(tex,p.yz).rgb*weights.x+tex2D(tex,p.xz).rgb*weights.y+tex2D(tex,p.xy).rgb*weights.z;
                p=p*0.273+float3(17.3,9.1,31.7);
                float3 b=tex2D(tex,float2(-p.z,p.y)).rgb*weights.x+tex2D(tex,float2(-p.z,p.x)).rgb*weights.y+tex2D(tex,float2(-p.y,p.x)).rgb*weights.z;
                return lerp(a,b,0.32);
            }
            fixed4 frag(v2f i):SV_Target
            {
                float3 p=i.worldPos-_PlanetCenter, radial=normalize(p), n=normalize(i.normal);
                float height=length(p)-_SeaLevel;
                float physicalSlope=1-saturate(dot(n,radial));
                if(_DebugOverlay>0.5 && _DebugOverlay<1.5)
                    return float4(lerp(float3(0.03,0.15,0.45),float3(1,0.85,0.25),saturate((height+400)/1400)),1);
                if(_DebugOverlay>1.5 && _DebugOverlay<2.5)
                    return float4(lerp(float3(0.1,0.55,0.2),float3(1,0.1,0.05),saturate(physicalSlope*3)),1);
                if(_DebugOverlay>2.5) return float4(i.biome.rgb*1.7,1);
                float climate=fbm(radial*5.3+23);
                float latitude=abs(radial.y);
                float arid=smoothstep(0.46,0.68,climate)*(1-smoothstep(0.5,0.8,latitude));
                float3 vegetation=lerp(_GrassColor.rgb*0.68,_GrassColor.rgb*1.25,fbm(radial*34));
                float3 albedo=lerp(i.biome.rgb,vegetation,0.45)*lerp(0.8,1.2,fbm(radial*190));
                float slope=smoothstep(0.12,0.5,physicalSlope);
                albedo=lerp(albedo,lerp(_DirtColor.rgb,_RockColor.rgb,0.7),slope);
                albedo=lerp(albedo,float3(0.49,0.45,0.31),(1-smoothstep(0,9,height))*0.7);
                float snow=smoothstep(0.78,0.94,latitude+(climate-0.5)*0.1);
                snow=max(snow,smoothstep(600,900,height));
                if(_TerrainRecipe>2.5) snow=i.climate.x;
                albedo=lerp(albedo,_SnowColor.rgb,snow);
                float detailFade=1-smoothstep(1200,7500,distance(_WorldSpaceCameraPos,i.worldPos));
                albedo*=lerp(0.92,lerp(0.78,1.20,fbm(p*0.045)),detailFade);
                // Sample optional textures only in local views. CPU climate colors remain
                // the source of biome identity at every LOD and at orbital distances.
                #if defined(_TERRAIN_TEXTURES)
                if(detailFade>0.01)
                {
                    float3 weights=pow(abs(n),4); weights/=max(0.0001,weights.x+weights.y+weights.z);
                    float3 tp=p*_TextureScale;
                    float soil=(1-smoothstep(0,20,height))*0.6+slope*0.25;
                    float3 tex=lerp(triplanar(_GrassTex,tp,weights),triplanar(_SoilTex,tp,weights),saturate(soil));
                    tex=lerp(tex,triplanar(_RockTex,tp,weights),slope);
                    tex=lerp(tex,triplanar(_SnowTex,tp,weights),snow);
                    albedo*=lerp(float3(1,1,1),tex,_TextureStrength*detailFade);
                }
                #endif
                float3 lightDir=normalize(_WorldSpaceLightPos0.xyz);
                float diffuse=saturate(dot(n,lightDir));
                float shadow=SHADOW_ATTENUATION(i);
                float3 lit=albedo*(float3(0.14,0.17,0.21)+_LightColor0.rgb*diffuse*shadow);
                float3 view=normalize(_WorldSpaceCameraPos-i.worldPos);
                float rim=pow(1-saturate(dot(radial,view)),4)*saturate(dot(radial,lightDir)+0.35);
                lit+=float3(0.13,0.26,0.39)*rim*0.55;
                lit=lerp(lit,float3(0.16,0.23,0.28),saturate(distance(_WorldSpaceCameraPos,i.worldPos)/18000)*0.12);
                if (_EarthworkBrush.w > 0)
                {
                    float d=length(radial-_EarthworkBrush.xyz)*_PlanetRadius;
                    float area=1-smoothstep(_EarthworkBrush.w-1,_EarthworkBrush.w,d);
                    float edge=smoothstep(_EarthworkBrush.w-2.5,_EarthworkBrush.w-1,d);
                    lit=lerp(lit,_EarthworkColor.rgb,area*(0.28+edge*0.52));
                }
                float fogAmount=saturate((distance(_WorldSpaceCameraPos,i.worldPos)-_PlanetAtmosphericFog.x)
                    /max(1,_PlanetAtmosphericFog.y-_PlanetAtmosphericFog.x))*_PlanetAtmosphericFog.z;
                lit=lerp(lit,_PlanetAtmosphericFogColor.rgb,fogAmount);
                return float4(lit,1);
            }
            ENDCG
        }
    }
    Fallback "Diffuse"
}
