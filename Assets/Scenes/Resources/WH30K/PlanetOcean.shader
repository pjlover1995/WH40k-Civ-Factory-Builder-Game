Shader "WH30K/PlanetOcean"
{
    Properties
    {
        _PlanetCenter ("Center", Vector)=(0,0,0,0)
        _SeaLevel ("Sea level (metres)", Float) = 6000
        _WaterSkirtFloor ("Water seam floor (metres)", Float) = 5000
        _ShoreClearance ("Shoreline clearance (metres)", Float) = 2.5
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry+1" "DisableBatching"="True" }
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
            float _PlanetSimulationTime;
            float _SeaLevel, _WaterSkirtFloor, _ShoreClearance;
            struct appdata { float4 vertex:POSITION; float2 water:TEXCOORD2; };
            struct v2f { float4 pos:SV_POSITION; float3 world:TEXCOORD0; float3 normal:TEXCOORD1; float depth:TEXCOORD2; };
            v2f vert(appdata v)
            {
                v2f o;
                float radius=length(v.vertex.xyz);
                float3 n=v.vertex.xyz/radius;
                float3 p=mul((float3x3)unity_ObjectToWorld,n*_SeaLevel);
                float wave=sin(dot(p,float3(.041,.022,.035))-_PlanetSimulationTime*1.3)*.9
                    +sin(dot(p,float3(-.026,.037,.051))+_PlanetSimulationTime*.8)*.55;
                // Fade geometry waves near land so only the shading animates at
                // the contact line, where the depth separation is smallest.
                wave*=smoothstep(_ShoreClearance,_ShoreClearance+10.0,v.water.x);
                float4 waterVertex=float4(n*lerp(_SeaLevel+wave,_WaterSkirtFloor,v.water.y),1);
                o.pos=UnityObjectToClipPos(waterVertex);
                o.world=mul(unity_ObjectToWorld,waterVertex).xyz;
                o.normal=UnityObjectToWorldNormal(n);
                o.depth=v.water.x;
                return o;
            }
            fixed4 frag(v2f i):SV_Target
            {
                // Match the terrain triangle and exclude shallow depths where the
                // wave's maximum 1.45 m of displacement could intersect the seabed.
                clip(i.depth - max(_ShoreClearance, 1.5));
                float depth=saturate(i.depth/110.0);
                float3 n=normalize(i.normal);
                float3 v=normalize(_WorldSpaceCameraPos-i.world), l=normalize(_WorldSpaceLightPos0.xyz);
                float3 wp=i.world-_PlanetCenter;
                float a=dot(wp,float3(.041,.022,.035))-_PlanetSimulationTime*1.3;
                float b=dot(wp,float3(-.026,.037,.051))+_PlanetSimulationTime*.8;
                float close=1-smoothstep(1500,9000,distance(_WorldSpaceCameraPos,i.world));
                float3 gradient=cos(a)*float3(.041,.022,.035)*.9+cos(b)*float3(-.026,.037,.051)*.55;
                n=normalize(n-(gradient-n*dot(gradient,n))*close*2);
                float fresnel=pow(1-saturate(dot(n,v)),4);
                float3 water=lerp(float3(0.035,0.22,0.24),float3(0.012,0.055,0.095),depth);
                float diffuse=saturate(dot(n,l));
                float spec=pow(saturate(dot(n,normalize(l+v))),160)*diffuse;
                float3 col=water*(0.17+diffuse*_LightColor0.rgb);
                col+=spec*_LightColor0.rgb*0.65+fresnel*float3(0.07,0.16,0.22)*diffuse;
                float foam=(1-smoothstep(.01,.10,depth))*smoothstep(.7,.98,sin(a+b)*.5+.5)*close;
                col=lerp(col,float3(.7,.82,.78)*(0.2+diffuse),foam*.65);
                return float4(col,1);
            }
            ENDCG
        }
    }
}
