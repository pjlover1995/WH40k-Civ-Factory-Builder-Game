using UnityEngine;
using WH30K.Planet;

namespace WH30K.CameraSystem
{
    /// <summary>Planet overview and an oblique, terrain-safe orbit around the colony.</summary>
    [RequireComponent(typeof(UnityEngine.Camera))]
    public class SimpleOrbitCamera : MonoBehaviour
    {
        [SerializeField] private float orbitSpeed = 180f;
        [SerializeField] private float zoomSpeed = 2f;
        [SerializeField] private float minDistance = 800f;
        [SerializeField] private float maxDistance = 9000f;
        [SerializeField] private Vector2 pitchLimits = new Vector2(-80f, 80f);
        private Transform target;
        private LODPlanet planet;
        private float distance, defaultDistance, yaw, pitch;
        private bool hasTarget, surfaceMode;
        private Vector3 settlementDirection;
        public bool IsSettlementView => surfaceMode;

        public void SetTarget(Transform newTarget, float approximateRadius)
        {
            var camera = GetComponent<UnityEngine.Camera>();
            camera.ResetProjectionMatrix();
            camera.fieldOfView = 50f;
            camera.nearClipPlane = 3f;
            camera.farClipPlane = approximateRadius * 12f;
            target = newTarget;
            planet = target != null ? target.GetComponent<LODPlanet>() : null;
            minDistance = approximateRadius + 250f;
            maxDistance = approximateRadius * 6f;
            defaultDistance = approximateRadius * 2.85f;
            distance = defaultDistance;
            yaw = 135f; pitch = 25f;
            surfaceMode = false;
            hasTarget = target != null;
            if (hasTarget) UpdateCameraTransform();
        }

        private void Update()
        {
            if (!hasTarget || target == null) return;
            if (Input.GetMouseButton(1))
            {
                yaw += Input.GetAxis("Mouse X") * orbitSpeed * Time.deltaTime;
                pitch -= Input.GetAxis("Mouse Y") * orbitSpeed * 0.5f * Time.deltaTime;
                pitch = surfaceMode ? Mathf.Clamp(pitch, 25f, 82f) : Mathf.Clamp(pitch, pitchLimits.x, pitchLimits.y);
            }
            var scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.0001f)
            {
                distance *= 1f - scroll * zoomSpeed;
                distance = surfaceMode ? Mathf.Clamp(distance, 320f, 6000f) : Mathf.Clamp(distance, minDistance, maxDistance);
            }
            if (Input.GetKeyDown(KeyCode.Home)) FrameTarget();
            if (Input.GetKeyDown(KeyCode.F)) FocusSettlement(settlementDirection);
            UpdateCameraTransform();
        }

        private void UpdateCameraTransform()
        {
            if (target == null) return;
            if (surfaceMode && planet != null)
            {
                var localGround = planet.EvaluateSurfacePoint(settlementDirection);
                var ground = target.TransformPoint(localGround);
                var up = target.TransformDirection(settlementDirection);
                var frame = Quaternion.FromToRotation(Vector3.up, up);
                var orbit = Quaternion.Euler(pitch, yaw, 0);
                var position = ground + frame * (orbit * Vector3.back * distance);
                var local = target.InverseTransformPoint(position);
                var safeRadius = Mathf.Max(planet.SeaLevel, planet.EvaluateHeight(local.normalized)) + 80f;
                if (local.magnitude < safeRadius) position = target.TransformPoint(local.normalized * safeRadius);
                transform.position = position;
                transform.LookAt(ground + up * 60f, up);
            }
            else
            {
                transform.position = target.position + Quaternion.Euler(pitch, yaw, 0) * (Vector3.back * distance);
                transform.LookAt(target.position, Vector3.up);
            }
        }

        public void FrameTarget()
        {
            if (target == null) return;
            surfaceMode = false;
            distance = defaultDistance;
            if (settlementDirection.sqrMagnitude > 0.01f)
            {
                yaw = Mathf.Atan2(-settlementDirection.x, -settlementDirection.z) * Mathf.Rad2Deg;
                pitch = Mathf.Asin(settlementDirection.y) * Mathf.Rad2Deg;
            }
            UpdateCameraTransform();
        }

        public void FocusSettlement(Vector3 direction)
        {
            if (target == null || direction.sqrMagnitude < 0.01f) return;
            settlementDirection = direction.normalized;
            surfaceMode = true;
            yaw = 35f; pitch = 48f;
            distance = 1000f;
            UpdateCameraTransform();
        }
    }
}
