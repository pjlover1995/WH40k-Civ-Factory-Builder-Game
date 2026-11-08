using UnityEngine;

namespace WH30K.Gameplay.Construction
{
    /// <summary>Visual descent for the 500 m tender after the player confirms its surveyed site.</summary>
    public sealed class LandingVesselDescent : MonoBehaviour
    {
        private Vector3 targetPosition, surfaceUp;
        private Quaternion targetRotation;
        private float startedAt;
        private bool activeDescent;
        public bool IsDescending => activeDescent;

        public void Begin(Vector3 position, Quaternion rotation, Vector3 up)
        {
            targetPosition = position;
            targetRotation = rotation;
            surfaceUp = up.normalized;
            transform.SetPositionAndRotation(position + surfaceUp * 1800f, rotation * Quaternion.Euler(3f, 0, 0));
            startedAt = Time.unscaledTime;
            activeDescent = true;
        }

        private void Update()
        {
            if (!activeDescent) return;
            var t = Mathf.Clamp01((Time.unscaledTime - startedAt) / 3.4f);
            var eased = t * t * (3f - 2f * t);
            transform.position = Vector3.Lerp(targetPosition + surfaceUp * 1800f, targetPosition, eased);
            transform.rotation = Quaternion.Slerp(targetRotation * Quaternion.Euler(3f, 0, 0), targetRotation, eased);
            if (t >= 1f)
            {
                transform.SetPositionAndRotation(targetPosition, targetRotation);
                activeDescent = false;
            }
        }
    }
}
