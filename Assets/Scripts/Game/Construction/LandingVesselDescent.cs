using UnityEngine;
using UnityEngine.Rendering;

namespace WH30K.Gameplay.Construction
{
    /// <summary>Visual descent for the 500 m tender after the player confirms its surveyed site.</summary>
    public sealed class LandingVesselDescent : MonoBehaviour
    {
        private const float StartAltitude = 900f;
        private const float DurationSeconds = 4.2f;
        private Vector3 targetPosition, surfaceUp;
        private Quaternion targetRotation;
        private float startedAt;
        private bool activeDescent;
        private bool touchdownEffectPlayed;
        private Material touchdownDustMaterial;
        private static Texture2D touchdownDustTexture;
        public bool IsDescending => activeDescent;
        public bool TouchdownEffectPlayed => touchdownEffectPlayed;
        public Vector3 LandingTargetPosition => targetPosition;

        public void Begin(Vector3 position, Quaternion rotation, Vector3 up)
        {
            targetPosition = position;
            targetRotation = rotation;
            surfaceUp = up.normalized;
            transform.SetPositionAndRotation(position + surfaceUp * StartAltitude, rotation * Quaternion.Euler(3f, 0, 0));
            startedAt = Time.unscaledTime;
            activeDescent = true;
            Camera.main?.GetComponent<WH30K.CameraSystem.SimpleOrbitCamera>()
                ?.FocusLandingApproach(surfaceUp, rotation, WH30K.Sim.ShipRules.Length * 2.2f);
        }

        private void EmitTouchdownDust()
        {
            touchdownEffectPlayed = true;
            var shader = Shader.Find("Legacy Shaders/Particles/Alpha Blended") ?? Shader.Find("Particles/Standard Unlit");
            if (shader == null) return;

            var dustRoot = new GameObject("Tender touchdown dust");
            dustRoot.transform.SetPositionAndRotation(targetPosition - surfaceUp * (WH30K.Sim.ShipRules.Height * .5f - 5f),
                Quaternion.FromToRotation(Vector3.up, surfaceUp));
            var particles = dustRoot.AddComponent<ParticleSystem>();
            var main = particles.main;
            main.duration = 1.2f;
            main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(5f, 13f);
            main.startSize = new ParticleSystem.MinMaxCurve(12f, 28f);
            main.startColor = new Color(.42f, .38f, .31f, .34f);
            var emission = particles.emission;
            emission.rateOverTime = 0;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)34) });
            var shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 38f;
            shape.radiusThickness = 1f;
            var color = particles.colorOverLifetime;
            color.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(new Color(.48f,.44f,.36f),0),
                    new GradientColorKey(new Color(.38f,.35f,.29f),1) },
                new[] { new GradientAlphaKey(.38f,0), new GradientAlphaKey(0,1) });
            color.color = new ParticleSystem.MinMaxGradient(fade);
            var noise = particles.noise;
            noise.enabled = true;
            noise.strength = .24f;
            noise.frequency = .18f;
            var renderer = dustRoot.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            touchdownDustMaterial = new Material(shader) { name = "Tender touchdown dust material" };
            var softParticle = GetTouchdownDustTexture();
            if (softParticle != null && touchdownDustMaterial.HasProperty("_MainTex"))
                touchdownDustMaterial.SetTexture("_MainTex", softParticle);
            if (touchdownDustMaterial.HasProperty("_Color")) touchdownDustMaterial.SetColor("_Color", Color.white);
            if (touchdownDustMaterial.HasProperty("_TintColor")) touchdownDustMaterial.SetColor("_TintColor", Color.white);
            if (touchdownDustMaterial.HasProperty("_SrcBlend")) touchdownDustMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            if (touchdownDustMaterial.HasProperty("_DstBlend")) touchdownDustMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            if (touchdownDustMaterial.HasProperty("_ZWrite")) touchdownDustMaterial.SetInt("_ZWrite", 0);
            touchdownDustMaterial.EnableKeyword("_ALPHABLEND_ON");
            touchdownDustMaterial.renderQueue = (int)RenderQueue.Transparent;
            renderer.sharedMaterial = touchdownDustMaterial;
            particles.Play();
            Destroy(dustRoot, 3.5f);
        }

        private static Texture2D GetTouchdownDustTexture()
        {
            if (touchdownDustTexture != null) return touchdownDustTexture;
            const int resolution = 128;
            touchdownDustTexture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false, true)
            {
                name = "Tender procedural dust opacity",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };
            var pixels = new Color[resolution * resolution];
            for (var y = 0; y < resolution; y++)
                for (var x = 0; x < resolution; x++)
                {
                    var u = (x + .5f) / resolution * 2f - 1f;
                    var v = (y + .5f) / resolution * 2f - 1f;
                    var radius = Mathf.Sqrt(u * u + v * v);
                    var edge = 1f - Mathf.SmoothStep(.38f, 1f, radius);
                    var noise = Mathf.PerlinNoise(x * .073f, y * .073f) * .62f
                        + Mathf.PerlinNoise(x * .17f + 13f, y * .17f + 7f) * .38f;
                    var alpha = edge * (.18f + noise * .72f);
                    pixels[y * resolution + x] = new Color(1, 1, 1, alpha);
                }
            touchdownDustTexture.SetPixels(pixels);
            touchdownDustTexture.Apply(false, true);
            return touchdownDustTexture;
        }
        private void OnDestroy()
        {
            if (touchdownDustMaterial != null) Destroy(touchdownDustMaterial);
        }
        private void Update()
        {
            if (!activeDescent) return;
            var t = Mathf.Clamp01((Time.unscaledTime - startedAt) / DurationSeconds);
            var eased = t * t * (3f - 2f * t);
            transform.position = Vector3.Lerp(targetPosition + surfaceUp * StartAltitude, targetPosition, eased);
            transform.rotation = Quaternion.Slerp(targetRotation * Quaternion.Euler(3f, 0, 0), targetRotation, eased);
            if (t >= 1f)
            {
                transform.SetPositionAndRotation(targetPosition, targetRotation);
                activeDescent = false;
                EmitTouchdownDust();
                Camera.main?.GetComponent<WH30K.CameraSystem.SimpleOrbitCamera>()
                    ?.TransitionToLandingSite(surfaceUp, WH30K.Sim.ShipRules.Length * 1.55f, .9f);
            }
        }
    }
}
