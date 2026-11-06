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
            float _PlanetSimulationTime;
            struct appdata { float4 vertex:POSITION; float3 normal:NORMAL; float4 color:COLOR; };
            struct v2f { float4 pos:SV_POSITION; float3 world:TEXCOORD0; float3 normal:TEXCOORD1; float depth:TEXCOORD2; };
            v2f vert(appdata v)
            {
                v2f o; float3 world=mul(unity_ObjectToWorld,v.vertex).xyz;
                float3 p=world-_PlanetCenter; float3 n=normalize(p);
                float wave=sin(dot(p,float3(.041,.022,.035))-_PlanetSimulationTime*1.3)*.9
                    +sin(dot(p,float3(-.026,.037,.051))+_PlanetSimulationTime*.8)*.55;
                world+=n*wave; o.pos=UnityWorldToClipPos(world);o.world=world;
                o.normal=UnityObjectToWorldNormal(v.normal);o.depth=v.color.r;return o;
            }
            fixed4 frag(v2f i):SV_Target
            {
                float3 p=normalize(i.world-_PlanetCenter), n=normalize(i.normal);
                float3 v=normalize(_WorldSpaceCameraPos-i.world), l=normalize(_WorldSpaceLightPos0.xyz);
                float3 wp=i.world-_PlanetCenter;
                float a=dot(wp,float3(.041,.022,.035))-_PlanetSimulationTime*1.3;
                float b=dot(wp,float3(-.026,.037,.051))+_PlanetSimulationTime*.8;
                float close=1-smoothstep(1500,9000,distance(_WorldSpaceCameraPos,i.world));
                float3 gradient=cos(a)*float3(.041,.022,.035)*.9+cos(b)*float3(-.026,.037,.051)*.55;
                n=normalize(n-(gradient-n*dot(gradient,n))*close*2);
                float fresnel=pow(1-saturate(dot(n,v)),4);
                float3 water=lerp(float3(0.035,0.22,0.24),float3(0.012,0.055,0.095),saturate(i.depth));
                float diffuse=saturate(dot(n,l));
                float spec=pow(saturate(dot(n,normalize(l+v))),160)*diffuse;
                float3 col=water*(0.17+diffuse*_LightColor0.rgb);
                col+=spec*_LightColor0.rgb*0.65+fresnel*float3(0.07,0.16,0.22)*diffuse;
                float foam=(1-smoothstep(.01,.10,i.depth))*smoothstep(.7,.98,sin(a+b)*.5+.5)*close;
                col=lerp(col,float3(.7,.82,.78)*(0.2+diffuse),foam*.65);
                return float4(col,1);
            }
            ENDCG
        }
    }
}
