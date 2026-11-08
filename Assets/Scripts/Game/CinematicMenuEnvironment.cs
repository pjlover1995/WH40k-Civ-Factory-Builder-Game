using UnityEngine;
using UnityEngine.UI;

namespace WH30K.UI
{
    /// <summary>Full-screen reference plate with restrained, localised forge effects.</summary>
    internal sealed class CinematicMenuEnvironment : MonoBehaviour
    {
        private RawImage plate;
        private Material effects;
        private int quality = 1;
        private float elapsed;

        private void Awake()
        {
            var root = (RectTransform)transform;
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = root.offsetMax = Vector2.zero;
            root.SetAsFirstSibling();

            // An opaque backing also covers the frame if the art fails to import.
            var backing = gameObject.AddComponent<Image>();
            backing.color = new Color(.025f, .018f, .016f);
            backing.raycastTarget = false;

            var art = new GameObject("ForgeWorldPlate", typeof(RectTransform), typeof(RawImage));
            art.transform.SetParent(root, false);
            plate = art.GetComponent<RawImage>();
            plate.raycastTarget = false;
            plate.texture = Resources.Load<Texture2D>("Menu/ForgeWorld");
            art.SetActive(plate.texture != null);
            var rect = plate.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;

            var shader = Resources.Load<Shader>("Menu/ForgeVista");
            if (shader != null && shader.isSupported)
            {
                effects = new Material(shader);
                plate.material = effects;
            }
            LayoutPlate();
        }

        public void SetAnimationQuality(int value)
        {
            quality = Mathf.Clamp(value, 0, 2);
            if (effects != null) effects.SetFloat("_Motion", quality);
            LayoutPlate();
        }

        private void OnRectTransformDimensionsChange() => LayoutPlate();

        private void LateUpdate()
        {
            if (quality > 0) elapsed += Time.unscaledDeltaTime;
            if (effects != null) effects.SetFloat("_MenuTime", elapsed);
            if (quality > 0) LayoutPlate();
        }

        private void LayoutPlate()
        {
            if (plate == null || plate.texture == null) return;
            var viewport = ((RectTransform)transform).rect;
            if (viewport.width <= 0 || viewport.height <= 0) return;
            var screenAspect = viewport.width / viewport.height;
            var artAspect = (float)plate.texture.width / plate.texture.height;
            var overscan = quality == 0 ? 1f : .988f;
            var width = Mathf.Min(1f, screenAspect / artAspect) * overscan;
            var height = Mathf.Min(1f, artAspect / screenAspect) * overscan;
            var x = quality == 0 ? 0f : Mathf.Sin(elapsed * .10f) * .004f;
            var y = quality == 0 ? 0f : Mathf.Sin(elapsed * .073f) * .003f;
            plate.uvRect = new Rect((1f - width) * .5f + x, (1f - height) * .5f + y, width, height);
            if (effects != null)
            {
                var crop = plate.uvRect;
                effects.SetVector("_ViewRect", new Vector4(crop.x, crop.y, crop.width, crop.height));
                effects.SetVector("_IdentityArea", new Vector4(700f / viewport.width, 148f / viewport.height, 0, 0));
            }
        }

        private void OnDestroy()
        {
            if (effects != null) Destroy(effects);
        }
    }
}
