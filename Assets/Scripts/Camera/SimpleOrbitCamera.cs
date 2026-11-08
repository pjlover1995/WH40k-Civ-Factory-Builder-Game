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
        private float colonyFocusDistance = 220;
        private Camera distantCamera;
        private float pendingZoom;
        private Vector3 overheadUp = Vector3.up;
        public Camera DistantCamera => distantCamera;
        // World interaction uses the lens basis directly, avoiding inverse view/projection
        // unprojection at multi-million-metre coordinates in Unity 2022's legacy dispatcher.
        public static bool TryScreenRay(Camera camera, Vector2 screenPosition, out Ray ray)
        {
            ray = default;
            if (camera == null || !float.IsFinite(screenPosition.x) || !float.IsFinite(screenPosition.y)) return false;
            var rect = camera.pixelRect;
            if (rect.width <= 0 || rect.height <= 0 || !rect.Contains(screenPosition)) return false;
            var x = ((screenPosition.x - rect.x) / rect.width) * 2 - 1;
            var y = ((screenPosition.y - rect.y) / rect.height) * 2 - 1;
            Vector3 origin, direction;
            if (camera.orthographic)
            {
                origin = camera.transform.TransformPoint(new Vector3(x * camera.orthographicSize * camera.aspect,
                    y * camera.orthographicSize, camera.nearClipPlane));
                direction = camera.transform.forward;
            }
            else
            {
                var tangent = Mathf.Tan(camera.fieldOfView * .5f * Mathf.Deg2Rad);
                var local = new Vector3(x * camera.aspect * tangent, y * tangent, 1);
                direction = camera.transform.TransformDirection(local).normalized;
                origin = camera.transform.TransformPoint(local * camera.nearClipPlane);
            }
            ray = new Ray(origin, direction);
            return true;
        }
        private void Awake() { GetComponent<Camera>().eventMask = 0; }
        private bool viewCached, cachedSurface;
        private Vector3 cachedDirection, cachedTargetPosition, cachedTargetScale;
        private Quaternion cachedTargetRotation;
        private float cachedDistance, cachedYaw, cachedPitch;
        private int cachedRevision, colonyRevision=-1;
        private Vector3 cachedColonyDirection, colonyPoint;
        public bool IsSettlementView => surfaceMode;
        public bool IsSurfaceView => surfaceMode;
        private float SurfaceTransitionDistance => Mathf.Min(planet.Radius * .15f, 12000f);
        public Vector3 NavigationDirection => navigationDirection;
        public Vector3 ColonyWorldPosition
        {
            get
            {
                if(planet==null)return Vector3.zero;
                if(colonyRevision!=planet.SurfaceRevision || !cachedColonyDirection.Equals(colonyDirection))
                {colonyPoint=planet.EvaluateSurfacePoint(colonyDirection);colonyRevision=planet.SurfaceRevision;cachedColonyDirection=colonyDirection;}
                return target.TransformPoint(colonyPoint);
            }
        }

        public void SetTarget(Transform newTarget, float radius)
        {
            var camera = GetComponent<UnityEngine.Camera>();
            camera.eventMask = 0;
            camera.ResetProjectionMatrix(); camera.fieldOfView = 50;
            target = newTarget; planet = target != null ? target.GetComponent<LODPlanet>() : null;
            if (planet != null)
            {
                if (distantCamera == null)
                {
                    var sky = new GameObject("Distant sky camera");
                    sky.transform.SetParent(transform, false);
                    distantCamera = sky.AddComponent<Camera>();
                }
                distantCamera.CopyFrom(camera);
                distantCamera.depth = camera.depth - 1;
                distantCamera.cullingMask = 1 << PlanetarySystem.CelestialLayer;
                // This camera draws scenery only. Unity's legacy mouse dispatcher would
                // otherwise unproject the cursor through its astronomical clip range.
                distantCamera.eventMask = 0;
                distantCamera.nearClipPlane = Mathf.Max(100, radius * .0002f);
                distantCamera.farClipPlane = radius * 20;
                distantCamera.clearFlags = CameraClearFlags.Skybox;
                distantCamera.useOcclusionCulling = false;
                camera.cullingMask &= ~(1 << PlanetarySystem.CelestialLayer);
                camera.clearFlags = CameraClearFlags.Depth;
            }
            viewCached=false;colonyRevision=-1;
            defaultDistance = radius * 3.7f; distance = defaultDistance;
            yaw = 135; pitch = 25; surfaceMode = false;
            if (target != null) UpdateCameraTransform();
        }

        public void SetColonyLocation(Vector3 normal, float focusDistance = 220)
        {
            colonyFocusDistance = focusDistance;
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
                if (Mathf.Abs(wheel) > 0.0001f) pendingZoom += wheel;
            }
            if (Mathf.Abs(pendingZoom) > .00001f)
            {
                var amount = pendingZoom * (1 - Mathf.Exp(-14 * Time.unscaledDeltaTime));
                pendingZoom -= amount; Zoom(amount);
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
            if (target == null || planet == null || float.IsNaN(amount) || float.IsInfinity(amount)) return;
            var factor = Mathf.Exp(Mathf.Clamp(-amount * zoomSpeed, -10, 10));
            if (surfaceMode)
            {
                distance = Mathf.Clamp(distance * factor, 85f, planet.Radius * 6);
                UpdateCameraTransform();
                if (distance > SurfaceTransitionDistance * 2) ExitSurface();
            }
            else
            {
                // Scale altitude, not distance to the planet's centre. Otherwise one wheel
                // step near the surface travels hundreds of kilometres on a large world.
                distance = planet.Radius + Mathf.Clamp((distance - planet.Radius) * factor, 50, planet.Radius * 5);
                UpdateCameraTransform();
                if (distance < planet.Radius + SurfaceTransitionDistance) EnterSurface();
            }
            UpdateCameraTransform();
        }

        private void EnterSurface()
        {
            navigationDirection = target.InverseTransformDirection((transform.position - target.position).normalized);
            var ground = target.TransformPoint(planet.EvaluateSurfacePoint(navigationDirection));
            distance = Vector3.Distance(transform.position, ground);
            overheadUp = transform.up;
            surfaceMode = true; yaw = 0; pitch = 90;
        }

        private void ExitSurface()
        {
            var local = transform.position - target.position;
            distance = local.magnitude;
            var n = local.normalized;
            yaw = Mathf.Atan2(-n.x, -n.z) * Mathf.Rad2Deg;
            pitch = Mathf.Asin(n.y) * Mathf.Rad2Deg;
            surfaceMode = false;
        }

        private void UpdateCameraTransform()
        {
            if(viewCached && cachedDirection.Equals(navigationDirection) && cachedSurface==surfaceMode
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
                var blend = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(SurfaceTransitionDistance, SurfaceTransitionDistance * 2, distance));
                var viewPitch = Mathf.Lerp(pitch, 90f, blend);
                var position = ground + frame * (Quaternion.Euler(viewPitch, yaw, 0) * Vector3.back * distance);
                var local = target.InverseTransformPoint(position);
                var safeRadius = Mathf.Max(planet.SeaLevel, planet.EvaluateSurfacePoint(local.normalized).magnitude) + 45f;
                if (local.magnitude < safeRadius) position = target.TransformPoint(local.normalized * safeRadius);
                transform.position = position; transform.LookAt(ground + up * 6f, viewPitch > 89.5f ? overheadUp : up);
            }
            else
            {
                transform.position = target.position + Quaternion.Euler(pitch, yaw, 0) * (Vector3.back * distance);
                // High relief can rise above the nominal orbital/surface switch altitude.
                var local = target.InverseTransformPoint(transform.position);
                var safeRadius = Mathf.Max(planet.SeaLevel, planet.EvaluateSurfacePoint(local.normalized).magnitude) + 45f;
                if (local.magnitude < safeRadius) transform.position = target.TransformPoint(local.normalized * safeRadius);
                transform.LookAt(target.position, Vector3.up);
            }
            UpdateClipping();
        }

        private void UpdateClipping()
        {
            var camera = GetComponent<Camera>();
            var altitude = Mathf.Max(1, target.InverseTransformPoint(transform.position).magnitude - planet.Radius);
            // Only nearby terrain belongs in the foreground depth buffer. The sky camera
            // retains moons/stars without an unstable one-to-sixty-million clip ratio.
            var horizon = Mathf.Sqrt(altitude * (2 * planet.Radius + altitude));
            camera.farClipPlane = Mathf.Max(10000, horizon * 1.3f + 20000);
            camera.nearClipPlane = Mathf.Max(.5f, camera.farClipPlane / 500000f, altitude * .0001f);
            if (distantCamera != null)
            {
                distantCamera.fieldOfView = camera.fieldOfView;
                distantCamera.aspect = camera.aspect;
            }
        }

        private void OnEnable() { GetComponent<Camera>().eventMask = 0; if (distantCamera != null) distantCamera.enabled = true; }
        private void OnDisable() { if (distantCamera != null) distantCamera.enabled = false; }

        public void FrameTarget()
        {
            if (target == null) return;
            pendingZoom = 0;
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
            pendingZoom = 0;
            colonyDirection = direction.normalized;
            FocusLocation(direction);
            distance = colonyFocusDistance;
            UpdateCameraTransform();
        }

        public void FocusOrbitalArrival(Vector3 direction)
        {
            if (target == null || direction.sqrMagnitude < 0.01f) return;
            pendingZoom = 0;
            colonyDirection = navigationDirection = direction.normalized;
            surfaceMode = true;
            yaw = 24f;
            pitch = 48f;
            distance = 18000f;
            UpdateCameraTransform();
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
