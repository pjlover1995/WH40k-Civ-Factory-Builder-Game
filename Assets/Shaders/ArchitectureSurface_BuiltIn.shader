Shader "WH30K/Architecture Surface"
{
    Properties
    {
        _MainTex ("Photographic surface atlas", 2D) = "white" {}
        _Color ("Surface tint", Color) = (1,1,1,1)
        _AtlasOffset ("Atlas quadrant (bottom left origin)", Vector) = (0,0.5,0,0)
        _SurfaceScale ("Texture repeats per metre", Float) = 0.16
        _Metallic ("Metallic", Range(0,1)) = 0
        _Glossiness ("Smoothness", Range(0,1)) = 0.25
        _Relief ("Surface relief in metres", Range(0,0.03)) = 0.008
        _TintStrength ("Palette tint strength", Range(0,1)) = 0
        [HideInInspector] _DepthFactor ("Surface overlay depth factor", Float) = 0
        [HideInInspector] _DepthUnits ("Surface overlay depth units", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 300
        Offset [_DepthFactor], [_DepthUnits]
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert addshadow
        #pragma target 3.0
        #pragma multi_compile_instancing
        #include "UnityCG.cginc"

        sampler2D _MainTex;
        float4 _MainTex_TexelSize, _Color, _AtlasOffset;
        float _SurfaceScale, _Metallic, _Glossiness, _Relief, _TintStrength;
        struct Input
        {
            float3 metricPosition;
            float3 axisNormal;
            float3 worldPos;
            float3 worldNormal;
            INTERNAL_DATA
        };

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            // Local axes follow the building on the sphere; column lengths include parent scale.
            // This works for core meshes, UV-less adaptive meshes and residential pieces alike.
            float3 scale = float3(length(unity_ObjectToWorld._m00_m10_m20),
                length(unity_ObjectToWorld._m01_m11_m21), length(unity_ObjectToWorld._m02_m12_m22));
            o.metricPosition = v.vertex.xyz * scale;
            o.axisNormal = normalize(v.normal / max(scale, 0.00001));
            // Procedural meshes do not all supply tangents. Supply a stable orthogonal frame.
            float3 reference = abs(v.normal.y) > 0.99 ? float3(0,0,1) : float3(0,1,0);
            v.tangent = float4(normalize(cross(reference, v.normal)), 1);
        }

        float3 SampleSurface(float2 uv)
        {
            // Mirrored repeats keep the authored quadrant's edges continuous. Explicit gradients
            // and a footprint-sized inset prevent other atlas materials leaking into distant mips.
            float2 dx = ddx(uv), dy = ddy(uv);
            float2 mirror = 1 - abs(frac(uv * 0.5) * 2 - 1);
            float2 direction = 1 - 2 * step(1, frac(uv * 0.5) * 2);
            float footprint = max(length(dx), length(dy));
            float inset = clamp(footprint, _MainTex_TexelSize.x * 4, 0.125);
            float2 atlasUV = _AtlasOffset.xy + 0.5 * (inset + mirror * (1 - 2 * inset));
            float gradientLimit = min(1, 0.03125 / max(footprint, 0.00001));
            return tex2Dgrad(_MainTex, atlasUV, dx * direction * 0.5 * gradientLimit,
                dy * direction * 0.5 * gradientLimit).rgb;
        }

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float3 weights = pow(abs(normalize(IN.axisNormal)), 8);
            weights /= max(dot(weights, float3(1,1,1)), 0.00001);
            float3 p = IN.metricPosition * _SurfaceScale;
            float3 scan = SampleSurface(p.zy) * weights.x + SampleSurface(p.xz) * weights.y
                + SampleSurface(p.xy) * weights.z;
            float luminance = dot(scan, float3(0.2126,0.7152,0.0722));
            // Preserve the surface scan while allowing the sixty residential palettes to show.
            float3 tint = lerp(float3(1,1,1), _Color.rgb * 1.65, _TintStrength);
            float dust = saturate(IN.axisNormal.y) * 0.065;
            o.Albedo = saturate(scan * tint * (1 - dust) + float3(0.36,0.32,0.25) * dust);
            // Dark tarnish and rust are rougher and less metallic than exposed metal.
            float exposed = smoothstep(0.025, 0.28, luminance);
            o.Metallic = _Metallic * lerp(0.35, 1, exposed);
            o.Smoothness = clamp(_Glossiness * lerp(0.55, 1.15, exposed), 0.04, 0.7);
            o.Occlusion = lerp(0.72, 1, smoothstep(0.015, 0.2, luminance));
            o.Alpha = 1;

            // Surface-gradient bump mapping: photographic luminance gives shallow relief only.
            // No claim of a scanned normal/height map; fade relief as texels become subpixel.
            float3 n = normalize(WorldNormalVector(IN, float3(0,0,1)));
            float3 px = ddx(IN.worldPos), py = ddy(IN.worldPos);
            float3 r1 = cross(py, n), r2 = cross(n, px);
            float determinant = dot(px, r1);
            float reliefFade = 1 - smoothstep(0.02, 0.18, max(length(ddx(p)), length(ddy(p))));
            float h = luminance * _Relief * reliefFade;
            float3 gradient = (r1 * ddx(h) + r2 * ddy(h))
                * (sign(determinant) / max(abs(determinant), 0.0000001));
            float3 bumped = normalize(n - gradient);
            o.Normal = normalize(float3(dot(bumped, WorldNormalVector(IN, float3(1,0,0))),
                dot(bumped, WorldNormalVector(IN, float3(0,1,0))), dot(bumped, n)));
        }
        ENDCG
    }
    FallBack "Standard"
}
