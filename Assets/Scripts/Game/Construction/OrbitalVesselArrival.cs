using System;
using UnityEngine;

namespace WH30K.Gameplay.Construction
{
    /// <summary>Shows the damaged 3 km main vessel exiting warp and settling into a safe orbit.</summary>
    public sealed class OrbitalVesselArrival : MonoBehaviour
    {
        private Transform planet;
        private Vector3 orbitDirection, orbitAxis, orbitTangent;
        private float planetRadius, orbitalAltitude, arrivalStarted;
        private Action arrived;
        private bool animating;
        private bool callbackSent;

        public void Begin(Transform planetTransform, Vector3 direction, float radius, float altitude, Action onArrival)
        {
            SetOrbitData(planetTransform, direction, radius, altitude, onArrival);
            arrivalStarted = Time.unscaledTime;
            animating = true;
            UpdateTransform(0);
        }

        public void SetOrbit(Transform planetTransform, Vector3 direction, float radius, float altitude)
        {
            SetOrbitData(planetTransform, direction, radius, altitude, null);
            animating = false;
            arrivalStarted = Time.unscaledTime - 4.2f;
            UpdateTransform(1);
        }

        private void SetOrbitData(Transform planetTransform, Vector3 direction, float radius, float altitude, Action onArrival)
        {
            planet = planetTransform;
            planetRadius = radius;
            orbitalAltitude = altitude;
            orbitDirection = direction.normalized;
            var right = Vector3.Cross(orbitDirection, Mathf.Abs(orbitDirection.y) > .95f ? Vector3.right : Vector3.up).normalized;
            orbitTangent = Vector3.Cross(right, orbitDirection).normalized;
            orbitAxis = Vector3.Cross(orbitDirection, orbitTangent).normalized;
            arrived = onArrival;
        }

        private void Update()
        {
            if (planet == null) return;
            var elapsed = Mathf.Max(0, Time.unscaledTime - arrivalStarted);
            if (animating && elapsed < 4.2f)
            {
                UpdateTransform(Mathf.Clamp01(elapsed / 4.2f));
                return;
            }
            if (animating)
            {
                animating = false;
                UpdateTransform(1);
                if (!callbackSent) { callbackSent = true; arrived?.Invoke(); }
            }
            var orbitAngle = Mathf.Max(0, elapsed - 4.2f) * .0012f;
            var n = Quaternion.AngleAxis(orbitAngle * Mathf.Rad2Deg, orbitAxis) * orbitDirection;
            var tangent = Vector3.Cross(orbitAxis, n).normalized;
            SetWorldPose(n, tangent, planetRadius + orbitalAltitude);
        }

        private void UpdateTransform(float arrival)
        {
            var start = orbitDirection * (planetRadius + 35000f) + orbitTangent * (18000f * (1 - arrival));
            var end = orbitDirection * (planetRadius + orbitalAltitude);
            var t = arrival * arrival * (3f - 2f * arrival);
            var p = Vector3.Lerp(start, end, t);
            SetWorldPose(p.normalized, orbitTangent, p.magnitude);
        }

        private void SetWorldPose(Vector3 direction, Vector3 tangent, float radialDistance)
        {
            var worldNormal = planet.TransformDirection(direction).normalized;
            var worldForward = planet.TransformDirection(tangent).normalized;
            var position = planet.TransformPoint(direction.normalized * radialDistance);
            // Keep orbit radius centred on the planetary centre; surface relief sets only the local camera focus.
            transform.SetPositionAndRotation(position, Quaternion.LookRotation(worldForward, worldNormal));
        }
    }
}
