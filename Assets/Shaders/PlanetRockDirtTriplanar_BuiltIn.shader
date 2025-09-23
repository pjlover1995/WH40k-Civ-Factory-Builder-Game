Shader "WH30K/PlanetRockDirtTriplanar_BuiltIn"
{
    Properties
    {
        _RockColor ("Rock Color", Color) = (0.45, 0.43, 0.4, 1)
        _DirtColor ("Dirt Color", Color) = (0.36, 0.25, 0.15, 1)
        _GrassColor ("Grass Color", Color) = (0.28, 0.46, 0.24, 1)
        _DesertColor ("Desert Color", Color) = (0.86, 0.74, 0.5, 1)
        _SnowColor ("Snow Color", Color) = (0.92, 0.95, 0.98, 1)
        _OceanShallowColor ("Shallow Ocean Color", Color) = (0.12, 0.4, 0.55, 1)
        _OceanDeepColor ("Deep Ocean Color", Color) = (0.02, 0.08, 0.2, 1)
        _TextureScale ("Noise Scale", Float) = 500.0
        _NoiseIntensity ("Noise Intensity", Range(0, 1)) = 0.35
        _BlendSharpness ("Blend Sharpness", Range(1, 8)) = 4
        _BiomeNoiseScale ("Biome Noise Scale", Float) = 850.0
        _BiomeContrast ("Biome Contrast", Range(0.5, 2.5)) = 1.2
        _CoastBlend ("Coastline Blend Distance", Float) = 120.0
        _SnowLine ("Snow Line Height", Float) = 600.0
        _PlanetRadius ("Planet Radius", Float) = 3000.0
        _SeaLevel ("Sea Level", Float) = 2880.0
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
            fixed4 _GrassColor;
            fixed4 _DesertColor;
            fixed4 _SnowColor;
            fixed4 _OceanShallowColor;
            fixed4 _OceanDeepColor;
            float _TextureScale;
            float _NoiseIntensity;
            float _BlendSharpness;
            float _BiomeNoiseScale;
            float _BiomeContrast;
            float _CoastBlend;
            float _SnowLine;
            float _PlanetRadius;
            float _SeaLevel;

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
                float3 worldPos = i.worldPos;
                float3 normal = normalize(i.worldNormal);
                float3 blend = pow(abs(normal), _BlendSharpness);
                blend = blend / (blend.x + blend.y + blend.z + 1e-5);

                float rockNoise = SampleTriplanarNoise(worldPos + 173.0, blend, _TextureScale * 0.75);
                float dirtNoise = SampleTriplanarNoise(worldPos, blend, _TextureScale);
                float biomeNoise = SampleTriplanarNoise(worldPos + 823.0, blend, max(_BiomeNoiseScale, 0.001));

                float3 rock = _RockColor.rgb * NoiseVariation(rockNoise);
                float3 dirt = _DirtColor.rgb * NoiseVariation(dirtNoise);

                float altitude = length(worldPos);
                float surfaceHeight = altitude - _SeaLevel;
                float radiusScale = max(0.1, _PlanetRadius / 3000.0);
                float coastWidth = max(1.0, _CoastBlend * radiusScale);
                float landMask = saturate(smoothstep(-coastWidth, coastWidth, surfaceHeight));

                float3 sphereNormal = normalize(worldPos);
                float latAbs = abs(sphereNormal.y);
                float temperature = 1.0 - latAbs;
                temperature = pow(saturate(temperature), _BiomeContrast);
                temperature *= saturate(1.0 - max(0.0, surfaceHeight) / (_SnowLine + 1200.0));

                float moisture = saturate(biomeNoise * 1.35 - 0.2);
                float desertFactor = saturate((1.0 - moisture) * temperature);

                float snowHeight = _SnowLine;
                float altitudeSnow = max(0.0, surfaceHeight) - max(0.0, (1.0 - temperature) * 180.0);
                float snowFactor = saturate(smoothstep(snowHeight - 120.0, snowHeight + 80.0, altitudeSnow));
                snowFactor = max(snowFactor, saturate(pow(1.0 - temperature, 2.0)));

                float grassFactor = saturate(1.0 - desertFactor);
                grassFactor *= 1.0 - snowFactor;

                float weightSum = desertFactor + grassFactor + snowFactor + 1e-3;
                float3 biomeColor = (desertFactor * _DesertColor.rgb + grassFactor * _GrassColor.rgb + snowFactor * _SnowColor.rgb) / weightSum;

                float slope = saturate(1.0 - abs(normal.y));
                float3 soilColor = lerp(dirt, rock, saturate(pow(slope, 1.3) + snowFactor * 0.25));
                float biomeBlend = saturate(1.0 - slope * 0.7);
                float3 landColor = lerp(soilColor, biomeColor, biomeBlend);
                landColor = lerp(landColor, _SnowColor.rgb, snowFactor * 0.25);

                float waterDepth = saturate(-surfaceHeight / (coastWidth * 2.0));
                float3 oceanColor = lerp(_OceanShallowColor.rgb, _OceanDeepColor.rgb, waterDepth * waterDepth);
                float shoreFoam = exp(-abs(surfaceHeight) / (25.0 * radiusScale)) * (1.0 - landMask);
                oceanColor = lerp(oceanColor, float3(0.9, 0.95, 1.0), shoreFoam * 0.3);

                float3 albedo = lerp(oceanColor, landColor, landMask);

                return fixed4(saturate(albedo), 1.0);
            }
            ENDCG
        }
    }
}
