using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WH30K.Planet;

namespace WH30K.CameraSystem
{
    [RequireComponent(typeof(UnityEngine.Camera))]
    public class SimpleOrbitCamera : MonoBehaviour
    {
        [SerializeField] private float orbitSpeed = 180f;
        [SerializeField] private float zoomSpeed = 2f;
        private Transform target;
        private LODPlanet planet;
        private float distance, defaultDistance, yaw, pitch;
        private bool surfaceMode;
        private Vector3 colonyDirection, navigationDirection;
        private bool viewCached, cachedSurface;
        private Vector3 cachedDirection, cachedTargetPosition, cachedTargetScale;
        private Quaternion cachedTargetRotation;
        private float cachedDistance, cachedYaw, cachedPitch;
        private int cachedRevision, colonyRevision=-1;
        private Vector3 cachedColonyDirection, colonyPoint;
        public bool IsSettlementView => surfaceMode;
        public bool IsSurfaceView => surfaceMode;
        public Vector3 NavigationDirection => navigationDirection;
        public Vector3 ColonyWorldPosition
        {
            get
            {
                if(planet==null)return Vector3.zero;
                if(colonyRevision!=planet.SurfaceRevision || cachedColonyDirection!=colonyDirection)
                {colonyPoint=planet.EvaluateSurfacePoint(colonyDirection);colonyRevision=planet.SurfaceRevision;cachedColonyDirection=colonyDirection;}
                return target.TransformPoint(colonyPoint);
            }
        }

        public void SetTarget(Transform newTarget, float radius)
        {
            var camera = GetComponent<UnityEngine.Camera>();
            camera.ResetProjectionMatrix(); camera.fieldOfView = 50;
            camera.nearClipPlane = 1f; camera.farClipPlane = radius * 20f;
            target = newTarget; planet = target != null ? target.GetComponent<LODPlanet>() : null;
            viewCached=false;colonyRevision=-1;
            defaultDistance = radius * 3.7f; distance = defaultDistance;
            yaw = 135; pitch = 25; surfaceMode = false;
            if (target != null) UpdateCameraTransform();
        }

        public void SetColonyLocation(Vector3 normal)
        {
            colonyDirection = normal.normalized;
            navigationDirection = colonyDirection;
            FrameTarget();
        }

        private bool PointerOverUi => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        private void Update()
        {
            if (target == null || planet == null) return;
            var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (selected != null && selected.activeInHierarchy && selected.GetComponent<InputField>() != null) return;
            var keys = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            Navigate(keys, Time.unscaledDeltaTime * (Input.GetKey(KeyCode.LeftShift) ? 3f : 1f));
            if (!PointerOverUi)
            {
                if (Input.GetMouseButton(1))
                {
                    yaw += Input.GetAxis("Mouse X") * orbitSpeed * 0.016f;
                    pitch -= Input.GetAxis("Mouse Y") * orbitSpeed * 0.012f;
                    pitch = Mathf.Clamp(pitch, surfaceMode ? 20f : -85f, 85f);
                }
                if (Input.GetMouseButton(2))
                {
                    if (!surfaceMode) EnterSurface();
                    PanSurface(new Vector2(-Input.GetAxis("Mouse X"), -Input.GetAxis("Mouse Y")), distance * 0.012f);
                }
                var wheel = Input.GetAxis("Mouse ScrollWheel");
                if (Mathf.Abs(wheel) > 0.0001f) Zoom(wheel);
            }
            if (Input.GetKeyDown(KeyCode.Home)) FrameTarget();
            if (Input.GetKeyDown(KeyCode.F)) FocusSettlement(colonyDirection);
            UpdateCameraTransform();
        }

        public void Navigate(Vector2 input, float seconds)
        {
            if (target == null || input.sqrMagnitude < 0.001f) return;
            if (surfaceMode) PanSurface(input.normalized, Mathf.Max(35f, distance * 0.7f) * seconds);
            else { yaw += input.x * 20f * seconds; pitch = Mathf.Clamp(pitch + input.y * 20f * seconds, -85, 85); }
        }

        private void PanSurface(Vector2 input, float step)
        {
            var up = target.TransformDirection(navigationDirection);
            var right = Vector3.ProjectOnPlane(transform.right, up).normalized;
            var forward = Vector3.ProjectOnPlane(transform.forward, up).normalized;
            var delta = target.InverseTransformDirection(right * input.x + forward * input.y);
            navigationDirection = (navigationDirection + delta * (step / planet.Radius)).normalized;
        }

        public void Zoom(float amount)
        {
            if (target == null) return;
            distance *= 1f - amount * zoomSpeed;
            if (surfaceMode)
            {
                distance = Mathf.Clamp(distance, 85f, planet.Radius * 1.4f);
                if (distance > planet.Radius * 0.75f) FrameTarget();
            }
            else
            {
                distance = Mathf.Clamp(distance, planet.Radius + 300f, planet.Radius * 6);
                if (distance < planet.Radius + 1800f) EnterSurface();
            }
            UpdateCameraTransform();
        }

        private void EnterSurface()
        {
            navigationDirection = target.InverseTransformDirection((transform.position - target.position).normalized);
            surfaceMode = true; distance = 1800; yaw = 35; pitch = 55;
        }

        private void UpdateCameraTransform()
        {
            if(viewCached && cachedDirection==navigationDirection && cachedSurface==surfaceMode
                && cachedDistance==distance && cachedYaw==yaw && cachedPitch==pitch && cachedRevision==planet.SurfaceRevision
                && cachedTargetPosition==target.position && cachedTargetRotation==target.rotation && cachedTargetScale==target.lossyScale)return;
            viewCached=true;cachedDirection=navigationDirection;cachedSurface=surfaceMode;
            cachedDistance=distance;cachedYaw=yaw;cachedPitch=pitch;cachedRevision=planet.SurfaceRevision;
            cachedTargetPosition=target.position;cachedTargetRotation=target.rotation;cachedTargetScale=target.lossyScale;
            if (surfaceMode)
            {
                var ground = target.TransformPoint(planet.EvaluateSurfacePoint(navigationDirection));
                var up = target.TransformDirection(navigationDirection);
                var frame = Quaternion.FromToRotation(Vector3.up, up);
                var position = ground + frame * (Quaternion.Euler(pitch, yaw, 0) * Vector3.back * distance);
                var local = target.InverseTransformPoint(position);
                var safeRadius = Mathf.Max(planet.SeaLevel, planet.EvaluateSurfacePoint(local.normalized).magnitude) + 45f;
                if (local.magnitude < safeRadius) position = target.TransformPoint(local.normalized * safeRadius);
                transform.position = position; transform.LookAt(ground + up * 6f, up);
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
            surfaceMode = false; distance = defaultDistance;
            if (navigationDirection.sqrMagnitude > 0.01f)
            {
                yaw = Mathf.Atan2(-navigationDirection.x, -navigationDirection.z) * Mathf.Rad2Deg;
                pitch = Mathf.Asin(navigationDirection.y) * Mathf.Rad2Deg;
            }
            UpdateCameraTransform();
        }

        public void FocusSettlement(Vector3 direction)
        {
            if (target == null || direction.sqrMagnitude < 0.01f) return;
            colonyDirection = direction.normalized;
            FocusLocation(direction);
        }

        public void FocusLocation(Vector3 direction)
        {
            if (target == null || direction.sqrMagnitude < 0.01f) return;
            navigationDirection = direction.normalized;
            surfaceMode = true; yaw = 35; pitch = 48; distance = 220;
            UpdateCameraTransform();
        }
    }
}
