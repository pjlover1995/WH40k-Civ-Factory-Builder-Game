Shader "WH30K/PlanetAtmosphere"
{
    Properties { _PlanetCenter ("Center", Vector)=(0,0,0,0) }
    SubShader
    {
        Tags { "Queue"="Transparent+10" "RenderType"="Transparent" }
        ZWrite Off Blend One One Cull Back
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float3 _PlanetCenter;
            struct v2f { float4 pos:SV_POSITION; float3 world:TEXCOORD0; };
            v2f vert(float4 vertex:POSITION) { v2f o; o.pos=UnityObjectToClipPos(vertex); o.world=mul(unity_ObjectToWorld,vertex).xyz; return o; }
            fixed4 frag(v2f i):SV_Target
            {
                float3 n=normalize(i.world-_PlanetCenter), v=normalize(_WorldSpaceCameraPos-i.world);
                float rim=pow(1-saturate(dot(n,v)),3.5);
                float day=saturate(dot(n,normalize(_WorldSpaceLightPos0.xyz))+0.4);
                return float4(float3(0.07,0.24,0.43)*rim*day*0.65,0);
            }
            ENDCG
        }
    }
}
