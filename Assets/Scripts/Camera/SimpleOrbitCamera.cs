using UnityEngine;

namespace WH30K.CameraSystem
{
    /// <summary>
    /// Minimal orbital camera that supports mouse orbiting/zooming and quick framing via Home/F keys.
    /// </summary>
    [RequireComponent(typeof(UnityEngine.Camera))]
    public class SimpleOrbitCamera : MonoBehaviour
    {
        [SerializeField] private float orbitSpeed = 180f;
        [SerializeField] private float zoomSpeed = 2f;
        [SerializeField] private float minDistance = 800f;
        [SerializeField] private float maxDistance = 9000f;
        [SerializeField] private Vector2 pitchLimits = new Vector2(-80f, 80f);

        private Transform target;
        private float distance;
        private float defaultDistance;
        private float yaw;
        private float pitch;
        private bool hasTarget;

        public void SetTarget(Transform newTarget, float approximateRadius)
        {
            target = newTarget;
            defaultDistance = Mathf.Clamp(approximateRadius * 2.2f, minDistance, maxDistance);
            distance = defaultDistance;
            yaw = 135f;
            pitch = 25f;
            hasTarget = target != null;
            if (hasTarget)
            {
                UpdateCameraTransform();
            }
        }

        private void Update()
        {
            if (!hasTarget || target == null)
            {
                return;
            }

            if (Input.GetMouseButton(1))
            {
                yaw += Input.GetAxis("Mouse X") * orbitSpeed * Time.deltaTime;
                pitch -= Input.GetAxis("Mouse Y") * orbitSpeed * 0.5f * Time.deltaTime;
                pitch = Mathf.Clamp(pitch, pitchLimits.x, pitchLimits.y);
            }

            var scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.0001f)
            {
                distance *= 1f - scroll * zoomSpeed;
                distance = Mathf.Clamp(distance, minDistance, maxDistance);
            }

            if (Input.GetKeyDown(KeyCode.Home) || Input.GetKeyDown(KeyCode.F))
            {
                FrameTarget();
            }

            UpdateCameraTransform();
        }

        private void UpdateCameraTransform()
        {
            if (target == null)
            {
                return;
            }

            var rotation = Quaternion.Euler(pitch, yaw, 0f);
            var offset = rotation * (Vector3.back * distance);
            transform.position = target.position + offset;
            transform.LookAt(target.position, Vector3.up);
        }

        public void FrameTarget()
        {
            if (target == null)
            {
                return;
            }

            distance = defaultDistance;
            yaw = 135f;
            pitch = 25f;
            UpdateCameraTransform();
        }
    }
}
