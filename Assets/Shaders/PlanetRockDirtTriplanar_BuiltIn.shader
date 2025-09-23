Shader "WH30K/PlanetRockDirtTriplanar_BuiltIn"
{
    Properties
    {
        _RockColor ("Rock Color", Color) = (0.45, 0.43, 0.4, 1)
        _DirtColor ("Dirt Color", Color) = (0.36, 0.25, 0.15, 1)
        _TextureScale ("Noise Scale", Float) = 500.0
        _NoiseIntensity ("Noise Intensity", Range(0, 1)) = 0.35
        _BlendSharpness ("Blend Sharpness", Range(1, 8)) = 4
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 300

        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _RockColor;
            fixed4 _DirtColor;
            float _TextureScale;
            float _NoiseIntensity;
            float _BlendSharpness;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            float Hash21(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float SampleTriplanarNoise(float3 worldPos, float3 blendWeights, float scale)
            {
                float safeScale = max(0.0001, scale);
                float3 scaledPos = worldPos / safeScale;
                float xSample = Hash21(scaledPos.yz);
                float ySample = Hash21(scaledPos.xz);
                float zSample = Hash21(scaledPos.xy);
                return dot(float3(xSample, ySample, zSample), blendWeights);
            }

            float NoiseVariation(float noiseValue)
            {
                float centered = (noiseValue - 0.5) * 2.0;
                return 1.0 + centered * _NoiseIntensity;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 normal = normalize(i.worldNormal);
                float3 blend = pow(abs(normal), _BlendSharpness);
                blend = blend / (blend.x + blend.y + blend.z + 1e-5);

                float rockNoise = SampleTriplanarNoise(i.worldPos + 173.0, blend, _TextureScale * 0.75);
                float dirtNoise = SampleTriplanarNoise(i.worldPos, blend, _TextureScale);

                float3 rock = _RockColor.rgb * NoiseVariation(rockNoise);
                float3 dirt = _DirtColor.rgb * NoiseVariation(dirtNoise);

                float slope = saturate(1.0 - abs(normal.y));
                float heightFactor = saturate((i.worldPos.y + 1000.0) / 2000.0);
                float rockBlend = saturate(slope * 1.5 + heightFactor * 0.5);
                float3 albedo = lerp(dirt, rock, rockBlend);

                return fixed4(saturate(albedo), 1.0);
            }
            ENDCG
        }
    }
}
