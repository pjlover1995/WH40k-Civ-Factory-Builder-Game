using UnityEngine;

namespace WH30K.Planet
{
    /// <summary>
    /// Represents a single quad patch on one face of the cube-sphere. Handles mesh generation
    /// and dynamic subdivision based on camera distance.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class LODPatch : MonoBehaviour
    {
        private LODPlanet planet;
        private Vector3 localUp;
        private Vector3 axisA;
        private Vector3 axisB;
        private Vector2 uvMin;
        private Vector2 uvMax;
        private int depth;

        private Mesh mesh;
        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;
        private LODPatch[] children;
        private Vector3 cachedCenter;
        private bool centerDirty = true;
        private bool surfaceDirty;
        private Vector3[] vertices, normals;
        private Color[] colors;
        private Vector2[] uvs;
        private int[] triangles, edge;

        public void RefreshSurface(Vector3 center, float range)
        {
            if (Vector3.Distance(center, GetCenterWorld()) > range + planet.Radius * 2.5f / Mathf.Pow(2, depth)) return;
            surfaceDirty = true;
            if (children != null) foreach (var child in children) child.RefreshSurface(center, range);
        }
        public void RebuildDirtySurface()
        {
            if(children!=null)foreach(var child in children)child.RebuildDirtySurface();
            else if(surfaceDirty){Regenerate();surfaceDirty=false;}
        }

        public void Initialize(LODPlanet parentPlanet, Vector3 faceNormal, Vector2 uvStart, Vector2 uvEnd, int lodDepth)
        {
            planet = parentPlanet;
            localUp = faceNormal.normalized;
            axisA = new Vector3(localUp.y, localUp.z, -localUp.x).normalized;
            axisB = Vector3.Cross(localUp, axisA).normalized;
            uvMin = uvStart;
            uvMax = uvEnd;
            depth = lodDepth;
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;

            meshFilter = GetComponent<MeshFilter>();
            meshRenderer = GetComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = planet.TerrainMaterial;
            // The directional shader supplies the planetary terminator. Only local
            // architecture casts shadow maps, avoiding acne across a 24,000-unit globe.
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            mesh = new Mesh { name = $"PatchMesh_{depth}" };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            meshFilter.sharedMesh = mesh;

            Regenerate();
        }

        public void RefreshLOD(Vector3 cameraPosition)
        {
            if (planet == null)
            {
                return;
            }

            var center = GetCenterWorld();
            var patchExtent = planet.Radius * 1.1f / Mathf.Pow(2f, depth);
            var distance = Mathf.Max(0f, Vector3.Distance(cameraPosition, center) - patchExtent);
            var splitThreshold = planet.SplitDistance / Mathf.Pow(planet.SplitFalloff, depth + 1);
            var mergeThreshold = splitThreshold * 1.6f;

            if (distance < splitThreshold && depth < planet.MaxDepth)
            {
                Subdivide();
                if (children != null)
                {
                    foreach (var child in children)
                    {
                        child.RefreshLOD(cameraPosition);
                    }

                    SetVisible(false);
                }
            }
            else
            {
                if (children != null)
                {
                    if (distance > mergeThreshold)
                    {
                        DestroyChildren();
                        SetVisible(true);
                    }
                    else
                    {
                        foreach (var child in children)
                        {
                            child.RefreshLOD(cameraPosition);
                        }

                        SetVisible(false);
                    }
                }
                else
                {
                    SetVisible(true);
                }
            }
        }

        private void Subdivide()
        {
            if (children != null)
            {
                return;
            }
            if (!planet.TrySplit()) return;

            children = new LODPatch[4];
            var midpoint = (uvMin + uvMax) * 0.5f;
            var depthChild = depth + 1;

            children[0] = CreateChild(new Vector2(uvMin.x, uvMin.y), new Vector2(midpoint.x, midpoint.y), depthChild, "SW");
            children[1] = CreateChild(new Vector2(midpoint.x, uvMin.y), new Vector2(uvMax.x, midpoint.y), depthChild, "SE");
            children[2] = CreateChild(new Vector2(uvMin.x, midpoint.y), new Vector2(midpoint.x, uvMax.y), depthChild, "NW");
            children[3] = CreateChild(new Vector2(midpoint.x, midpoint.y), new Vector2(uvMax.x, uvMax.y), depthChild, "NE");
        }

        private LODPatch CreateChild(Vector2 childUvMin, Vector2 childUvMax, int childDepth, string suffix)
        {
            var go = new GameObject($"{name}_{suffix}");
            go.transform.SetParent(transform, false);
            var patch = go.AddComponent<LODPatch>();
            patch.Initialize(planet, localUp, childUvMin, childUvMax, childDepth);
            return patch;
        }

        private void DestroyChildren()
        {
            if (children == null)
            {
                return;
            }

            for (var i = 0; i < children.Length; i++)
            {
                if (children[i] != null)
                {
                    Destroy(children[i].gameObject);
                }
            }

            children = null;
        }

        private void SetVisible(bool visible)
        {
            if (visible && surfaceDirty) { Regenerate(); surfaceDirty = false; }
            if (meshRenderer != null)
            {
                meshRenderer.enabled = visible;
            }

            if (meshFilter != null && meshFilter.sharedMesh != mesh)
            {
                meshFilter.sharedMesh = mesh;
            }
        }

        private void Regenerate()
        {
            centerDirty = true;
            var resolution = planet.GetResolutionForDepth(depth);
            var surfaceCount = resolution * resolution;
            var edgeCount = (resolution - 1) * 4;
            var vertexCount = surfaceCount + edgeCount;
            var newTopology = vertices == null || vertices.Length != vertexCount;
            if(newTopology)
            {
                vertices = new Vector3[vertexCount]; normals = new Vector3[vertexCount];
                colors = new Color[vertexCount]; uvs = new Vector2[vertexCount];
                triangles = new int[(resolution - 1) * (resolution - 1) * 6 + edgeCount * 6];
                edge = new int[edgeCount];
            }

            var triIndex = 0;
            for (var y = 0; y < resolution; y++)
            {
                for (var x = 0; x < resolution; x++)
                {
                    var i = x + y * resolution;
                    var percentX = Mathf.Lerp(uvMin.x, uvMax.x, x / (float)(resolution - 1));
                    var percentY = Mathf.Lerp(uvMin.y, uvMax.y, y / (float)(resolution - 1));
                    var pointOnCube = localUp + (percentX - 0.5f) * 2f * axisA + (percentY - 0.5f) * 2f * axisB;
                    var pointOnSphere = pointOnCube.normalized;
                    var vertex = planet.EvaluateSurfacePoint(pointOnSphere);
                    vertices[i] = vertex;
                    // Match boundary normals across different LODs; reuse rendered neighbours inside.
                    if(x==0 || y==0 || x==resolution-1 || y==resolution-1)
                        normals[i] = planet.EvaluateSurfaceNormal(pointOnSphere);
                    colors[i] = planet.BiomeColor(pointOnSphere, vertex.magnitude);
                    uvs[i] = new Vector2(percentX, percentY);

                    if (x == resolution - 1 || y == resolution - 1)
                    {
                        continue;
                    }

                    triangles[triIndex++] = i;
                    triangles[triIndex++] = i + resolution + 1;
                    triangles[triIndex++] = i + resolution;

                    triangles[triIndex++] = i;
                    triangles[triIndex++] = i + 1;
                    triangles[triIndex++] = i + resolution + 1;
                }
            }

            // Sink a narrow skirt under each boundary to cover cracks between unequal LODs.
            for(var y=1;y<resolution-1;y++)for(var x=1;x<resolution-1;x++)
            {
                var i=x+y*resolution;
                var normal=Vector3.Cross(vertices[i+1]-vertices[i-1],vertices[i+resolution]-vertices[i-resolution]).normalized;
                normals[i]=Vector3.Dot(normal,vertices[i])<0?-normal:normal;
            }
            var e = 0;
            for (var x = 0; x < resolution - 1; x++) edge[e++] = x;
            for (var y = 0; y < resolution - 1; y++) edge[e++] = y * resolution + resolution - 1;
            for (var x = resolution - 1; x > 0; x--) edge[e++] = (resolution - 1) * resolution + x;
            for (var y = resolution - 1; y > 0; y--) edge[e++] = y * resolution;
            for (var i = 0; i < edgeCount; i++)
            {
                var a = edge[i]; var b = edge[(i + 1) % edgeCount];
                var lower = surfaceCount + i; var nextLower = surfaceCount + (i + 1) % edgeCount;
                vertices[lower] = vertices[a] - vertices[a].normalized * 12f;
                normals[lower] = normals[a]; uvs[lower] = uvs[a];
                colors[lower] = colors[a];
                triangles[triIndex++] = a; triangles[triIndex++] = lower; triangles[triIndex++] = b;
                triangles[triIndex++] = b; triangles[triIndex++] = lower; triangles[triIndex++] = nextLower;
            }

            if(newTopology)mesh.Clear();
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.colors = colors;
            if(newTopology){mesh.uv = uvs;mesh.triangles = triangles;}
            else mesh.RecalculateBounds();
        }

        private Vector3 GetCenterWorld()
        {
            if (!centerDirty)
            {
                return transform.TransformPoint(cachedCenter);
            }

            var midX = (uvMin.x + uvMax.x) * 0.5f;
            var midY = (uvMin.y + uvMax.y) * 0.5f;
            var pointOnCube = localUp + (midX - 0.5f) * 2f * axisA + (midY - 0.5f) * 2f * axisB;
            var pointOnSphere = pointOnCube.normalized;
            cachedCenter = planet.EvaluateSurfacePoint(pointOnSphere);
            centerDirty = false;
            return transform.TransformPoint(cachedCenter);
        }

        private void OnDestroy()
        {
            if (mesh != null)
            {
                Destroy(mesh);
            }
        }
    }
}
