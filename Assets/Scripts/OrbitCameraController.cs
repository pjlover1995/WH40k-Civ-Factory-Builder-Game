using UnityEngine;

namespace WH40kCivFactoryBuilderGame
{
    [RequireComponent(typeof(Camera))]
    public class OrbitCameraController : MonoBehaviour
    {
        [SerializeField]
        private Transform target;

        [SerializeField]
        private float initialDistance = 16000f;

        [SerializeField]
        private float minDistance = 500f;

        [SerializeField]
        private float maxDistance = 35000f;

        [SerializeField]
        private float mouseOrbitSpeed = 120f;

        [SerializeField]
        private float keyboardOrbitSpeed = 60f;

        [SerializeField]
        private float zoomSpeed = 8000f;

        [SerializeField]
        private float smoothing = 12f;

        private float yaw;
        private float pitch;
        private float distance;
        private Vector3 currentVelocity;

        private void Start()
        {
            if (target == null)
            {
                return;
            }

            Vector3 offset = transform.position - target.position;
            distance = offset.magnitude;
            if (distance <= Mathf.Epsilon)
            {
                distance = initialDistance;
            }

            Vector3 direction = offset.normalized;
            pitch = Mathf.Asin(Mathf.Clamp(direction.y, -1f, 1f)) * Mathf.Rad2Deg;
            yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            distance = Mathf.Clamp(distance, minDistance, maxDistance);

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void LateUpdate()
        {
            if (target == null)
            {
                return;
            }

            HandleInput();
            UpdateCameraPosition();
        }

        public void SetTarget(Transform newTarget)
        {
            target = newTarget;
            Start();
        }

        private void HandleInput()
        {
            if (Input.GetMouseButton(0))
            {
                yaw += Input.GetAxis("Mouse X") * mouseOrbitSpeed * Time.deltaTime;
                pitch -= Input.GetAxis("Mouse Y") * mouseOrbitSpeed * Time.deltaTime;
            }

            float horizontal = Input.GetAxis("Horizontal");
            float vertical = Input.GetAxis("Vertical");
            if (Mathf.Abs(horizontal) > 0.0001f)
            {
                yaw += horizontal * keyboardOrbitSpeed * Time.deltaTime;
            }

            if (Mathf.Abs(vertical) > 0.0001f)
            {
                pitch -= vertical * keyboardOrbitSpeed * Time.deltaTime;
            }

            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.0001f)
            {
                distance -= scroll * zoomSpeed;
            }

            if (Input.GetKey(KeyCode.PageUp) || Input.GetKey(KeyCode.E))
            {
                distance -= zoomSpeed * 0.5f * Time.deltaTime;
            }

            if (Input.GetKey(KeyCode.PageDown) || Input.GetKey(KeyCode.Q))
            {
                distance += zoomSpeed * 0.5f * Time.deltaTime;
            }

            pitch = Mathf.Clamp(pitch, -89f, 89f);
            distance = Mathf.Clamp(distance, minDistance, maxDistance);
        }

        private void UpdateCameraPosition()
        {
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 desiredPosition = target.position - (rotation * Vector3.forward * distance);
            transform.position = Vector3.SmoothDamp(transform.position, desiredPosition, ref currentVelocity, 1f / Mathf.Max(0.01f, smoothing));
            transform.LookAt(target.position);
        }
    }
}
