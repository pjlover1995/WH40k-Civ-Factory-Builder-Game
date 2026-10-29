using System;
using System.Collections.Generic;
using UnityEngine;
using WH30K.Sim;

namespace WH30K.Gameplay.Construction
{
    /// <summary>Procedural buildings that extend a facility into an integrated campus.</summary>
    public sealed class AdaptiveBuildingVisual : MonoBehaviour
    {
        private static readonly Dictionary<string, Mesh> MeshCache = new Dictionary<string, Mesh>();
        private ManagedBuilding building;
        private GameObject architecture;
        private readonly List<Transform> growingAnnexes = new List<Transform>();
        private readonly List<Vector3> growingAnnexTargets = new List<Vector3>();
        private readonly List<Transform> growingCrowns = new List<Transform>();
        private LineRenderer scaffold, scaffoldPosts, completion;
        private Bounds constructionBounds;
        private static Material amber, teal;

        public void Configure(ManagedBuilding owner) { building = owner; Rebuild(); }

        private static bool Radial(int plan) => plan == 0 || plan >= 2;
        private static bool Vertical(int plan) => plan == 0 || plan == 1;
        private static int AnnexCount(int tier) => tier == 0 ? 8 : 12;
        private static float AnnexRadius(int tier) => tier == 0 ? .82f : 1.38f;
        private static float OuterRadius(int tier) => tier == 0 ? 1.08f : 1.67f;

        public static Bounds BoundsFor(BuildingOperationState state, bool reserve)
        {
            var result = new Bounds(new Vector3(0, .5f, 0), Vector3.one);
            var count = state.level + (reserve && state.improving ? 1 : 0);
            for (var tier = 0; tier < count; tier++)
            {
                var plan = tier < state.level ? (tier == 0 ? state.firstExpansion : state.secondExpansion) : state.growthPlan;
                if (Radial(plan))
                {
                    var radius = OuterRadius(tier) + .075f;
                    result.Encapsulate(new Bounds(Vector3.zero, new Vector3(radius * 2, .56f, radius * 2)));
                }
                if (Vertical(plan))
                {
                    var top = 1.65f + tier * .36f;
                    result.Encapsulate(new Bounds(new Vector3(0, top * .5f + .04f, 0), new Vector3(1.16f, top + .08f, 1.16f)));
                }
            }
            return result;
        }

        public void Rebuild()
        {
            // Unity domain reload can preserve child objects while clearing private runtime references.
            // Remove the previous generated hierarchy before reconstructing it from the saved tier state.
            for (var i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i);
                if (child.name == "Adaptive architecture" || child.name == "Concentric adaptive architecture"
                    || child.name == "Adaptive campus architecture")
                { child.gameObject.SetActive(false); Destroy(child.gameObject); }
            }
            architecture = new GameObject("Adaptive campus architecture");
            architecture.transform.SetParent(transform, false);
            growingAnnexes.Clear(); growingAnnexTargets.Clear(); growingCrowns.Clear();
            var state = building.State;
            var filter = GetComponent<MeshFilter>();
            var source = filter.sharedMesh;
            var materials = GetComponent<MeshRenderer>().sharedMaterials;
            constructionBounds = BoundsFor(state, true);
            var count = state.level + (state.improving ? 1 : 0);
            for (var tier = 0; tier < count; tier++)
            {
                var pending = tier == state.level;
                var plan = pending ? state.growthPlan : tier == 0 ? state.firstExpansion : state.secondExpansion;
                if (Radial(plan))
                {
                    var annexCount = AnnexCount(tier);
                    for (var index = 0; index < annexCount; index++)
                    {
                        var angle = (index + (tier == 0 ? 0f : .5f)) * Mathf.PI * 2f / annexCount;
                        var direction = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                        var annex = AddArchitecture($"District building T{tier + 1} / {index + 1}",
                            AdaptiveMesh(building.Id, tier, true, index % 4), materials);
                        var target = direction * AnnexRadius(tier) + Vector3.up * .012f;
                        annex.transform.localPosition = target;
                        annex.transform.localRotation = Quaternion.Euler(0, -angle * Mathf.Rad2Deg, 0);
                        if (pending)
                        {
                            growingAnnexes.Add(annex.transform);
                            growingAnnexTargets.Add(target);
                        }
                    }
                }
                if (Vertical(plan))
                {
                    var crown = AddArchitecture("Integrated upper crown", AdaptiveMesh(building.Id, tier, false), materials);
                    crown.transform.localPosition = Vector3.up * (source.bounds.max.y - .06f + tier * .36f);
                    if (pending) growingCrowns.Add(crown.transform);
                }
            }
            if (amber == null) amber = new Material(Shader.Find("Unlit/Color")) { color = new Color(1f, .65f, .15f) };
            if (teal == null) teal = new Material(Shader.Find("Unlit/Color")) { color = new Color(.2f, 1f, .8f) };
            scaffold = Line("Moving circular construction scaffold", amber, 33, true);
            scaffoldPosts = Line("Radial construction supports", amber, 32, false);
            completion = Line("District completion wave", teal, 49, true);
            var colliderBounds = source.bounds;
            colliderBounds.Encapsulate(BoundsFor(state, false));
            var collider = GetComponent<BoxCollider>(); collider.center = colliderBounds.center; collider.size = colliderBounds.size;
            Animate();
        }

        private GameObject AddArchitecture(string title, Mesh mesh, Material[] materials)
        {
            var go = new GameObject(title, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(architecture.transform, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            go.GetComponent<MeshRenderer>().sharedMaterials = materials;
            return go;
        }

        private static Mesh AdaptiveMesh(string id, int tier, bool radial, int variation = 0)
        {
            var key = id + ":" + tier + ":" + (radial ? "annex-" + variation : "crown");
            if (MeshCache.TryGetValue(key, out var cached)) return cached;
            var builder = new AdaptiveMeshBuilder();
            if (radial) BuildAnnex(builder, id, tier, variation); else BuildCrown(builder, id, tier);
            cached = builder.Build(key); MeshCache.Add(key, cached); return cached;
        }

        private static void BuildAnnex(AdaptiveMeshBuilder mesh, string id, int tier, int variation)
        {
            var bodyDepth = tier == 0 ? .30f : .34f;
            var bodyWidth = tier == 0 ? .39f : .46f;
            var bodyHeight = id == "HabBlock" ? .34f : .29f;
            var body = id == "HabBlock" ? 0 : id == "Utility" ? 1 : 6;
            var accent = id == "HabBlock" ? 4 : id == "Utility" ? 5 : 3;
            var roofStyle = (variation + tier) % 3;

            // Each addition is its own complete structure, with a plinth, facade, roof and entrance.
            mesh.Box(0, new Vector3(0, .055f, 0), new Vector3(bodyDepth + .10f, .09f, bodyWidth + .10f), Quaternion.identity);
            mesh.Box(1, new Vector3(0, .103f, 0), new Vector3(bodyDepth + .12f, .018f, bodyWidth + .12f), Quaternion.identity);
            mesh.Box(body, new Vector3(0, .105f + bodyHeight * .5f, 0), new Vector3(bodyDepth, bodyHeight, bodyWidth), Quaternion.identity);
            mesh.Box(3, new Vector3(0, .13f, 0), new Vector3(bodyDepth + .02f, .025f, bodyWidth + .025f), Quaternion.identity);

            // A covered gallery joins this annex to the core, or to the previous campus tier.
            var linkStart = tier == 0 ? .50f : AnnexRadius(tier - 1) + .15f;
            var linkEnd = AnnexRadius(tier) - bodyDepth * .5f;
            var linkLength = Mathf.Max(.04f, linkEnd - linkStart);
            var linkCenter = AdaptiveBuildingRules.GalleryLocalCenter(linkStart,linkEnd,AnnexRadius(tier));
            mesh.Box(1, new Vector3(linkCenter, .115f, 0), new Vector3(linkLength, .065f, .12f), Quaternion.identity);
            mesh.Box(3, new Vector3(linkCenter, .155f, 0), new Vector3(linkLength, .022f, .15f), Quaternion.identity);
            for (var side = -1; side <= 1; side += 2)
                mesh.Box(0, new Vector3(linkCenter, .20f, side * .085f), new Vector3(linkLength, .11f, .035f), Quaternion.identity);

            if (id == "HabBlock")
            {
                var floors = variation % 2 == 0 ? 2 : 3;
                for (var floor = 0; floor < floors; floor++)
                {
                    var y = .15f + (floor + .5f) * (bodyHeight - .07f) / floors;
                    var windows = variation == 2 ? 3 : 2;
                    for (var window = 0; window < windows; window++)
                    {
                        var z = (window - (windows - 1) * .5f) * bodyWidth * .27f;
                        mesh.Box(6, new Vector3(bodyDepth * .5f + .008f, y, z), new Vector3(.018f, .075f, .052f), Quaternion.identity);
                        mesh.Box(4, new Vector3(bodyDepth * .5f + .02f, y, z), new Vector3(.025f, .052f, .030f), Quaternion.identity);
                    }
                    mesh.Box(1, new Vector3(0, .14f + (floor + 1) * (bodyHeight - .05f) / floors, 0),
                        new Vector3(bodyDepth + .035f, .018f, bodyWidth + .035f), Quaternion.identity);
                }
                mesh.Box(6, new Vector3(-bodyDepth * .5f - .008f, .17f, 0), new Vector3(.018f, .16f, .09f), Quaternion.identity);
                mesh.Box(3, new Vector3(-bodyDepth * .5f - .02f, .13f, 0), new Vector3(.025f, .035f, .11f), Quaternion.identity);
            }
            else if (id == "Utility")
            {
                for (var louver = 0; louver < 5; louver++)
                    mesh.Box(5, new Vector3(bodyDepth * .5f + .016f, .15f + louver * .045f, 0),
                        new Vector3(.025f, .018f, bodyWidth * .64f), Quaternion.identity);
                mesh.Box(6, new Vector3(-bodyDepth * .5f - .008f, .18f, 0), new Vector3(.018f, .14f, .11f), Quaternion.identity);
            }
            else
            {
                mesh.Box(6, new Vector3(bodyDepth * .5f + .009f, .21f, 0), new Vector3(.018f, .12f, bodyWidth * .46f), Quaternion.identity);
                mesh.Box(accent, new Vector3(bodyDepth * .5f + .02f, .21f, 0), new Vector3(.025f, .035f, bodyWidth * .30f), Quaternion.identity);
                for (var vent = -1; vent <= 1; vent++)
                    mesh.Box(1, new Vector3(0, .14f + (vent + 1) * .045f, vent * bodyWidth * .31f),
                        new Vector3(bodyDepth + .035f, .014f, .08f), Quaternion.identity);
            }

            // Three roof treatments keep adjacent buildings related without cloning one silhouette.
            if (roofStyle == 0)
            {
                var halfRoof = bodyWidth * .27f;
                for (var side = -1; side <= 1; side += 2)
                    mesh.Box(2, new Vector3(0, .13f + bodyHeight + .045f, side * halfRoof * .5f),
                        new Vector3(bodyDepth + .08f, .035f, halfRoof * 1.12f),
                        Quaternion.Euler(side * 28f, 0, 0));
                mesh.Box(3, new Vector3(0, .13f + bodyHeight + .09f, 0), new Vector3(bodyDepth + .10f, .035f, .045f), Quaternion.identity);
            }
            else if (roofStyle == 1)
            {
                mesh.Box(2, new Vector3(0, .14f + bodyHeight + .025f, 0), new Vector3(bodyDepth + .08f, .055f, bodyWidth + .08f), Quaternion.identity);
                mesh.Box(3, new Vector3(0, .14f + bodyHeight + .065f, 0), new Vector3(bodyDepth + .12f, .025f, .08f), Quaternion.identity);
            }
            else
            {
                mesh.Box(2, new Vector3(0, .13f + bodyHeight + .035f, 0), new Vector3(bodyDepth + .09f, .07f, bodyWidth + .07f), Quaternion.identity);
                mesh.Box(1, new Vector3(bodyDepth * .30f, .13f + bodyHeight + .09f, 0), new Vector3(.10f, .08f, .10f), Quaternion.identity);
                if (id == "Factory" || id == "Manufactorum" || id == "Smelter" || id == "Refinery" || id == "PromethiumGenerator")
                    mesh.Box(1, new Vector3(-bodyDepth * .20f, .13f + bodyHeight + .13f, 0), new Vector3(.09f, .16f, .10f), Quaternion.identity);
            }
        }

        private static void BuildCrown(AdaptiveMeshBuilder mesh, string id, int tier)
        {
            var wall = id == "HabBlock" ? 0 : id == "Utility" ? 1 : 6;
            var accent = id == "HabBlock" ? 4 : id == "Utility" ? 5 : 3;
            var baseY = .05f + tier * .015f;
            var width = .56f;
            var depth = .54f;
            var height = id == "HabBlock" ? .34f : .28f;
            mesh.Box(0, new Vector3(0, baseY, 0), new Vector3(width + .12f, .10f, depth + .12f), Quaternion.identity);
            mesh.Box(1, new Vector3(0, baseY + .05f, 0), new Vector3(width + .14f, .02f, depth + .14f), Quaternion.identity);
            mesh.Box(wall, new Vector3(0, baseY + .06f + height * .5f, 0), new Vector3(width, height, depth), Quaternion.identity);

            // New floors are a compact, independent upper structure rather than a tube around the roof.
            for (var side = 0; side < 4; side++)
            {
                var rotation = Quaternion.Euler(0, side * 90f, 0);
                var face = rotation * Vector3.forward * (depth * .5f + .012f);
                var rows = id == "HabBlock" ? 2 : 1;
                for (var row = 0; row < rows; row++)
                {
                    var y = baseY + .10f + height * (rows == 1 ? .52f : .32f + row * .38f);
                    var widthOfWindow = id == "HabBlock" ? .07f : .12f;
                    mesh.Box(6, face + Vector3.up * y, new Vector3(widthOfWindow * 1.2f, .11f, .018f), rotation);
                    mesh.Box(accent, face + Vector3.up * y, new Vector3(widthOfWindow, .075f, .025f), rotation);
                }
            }

            var roofY = baseY + .06f + height;
            if (id == "Utility")
            {
                mesh.Box(2, new Vector3(0, roofY + .03f, 0), new Vector3(width + .08f, .06f, depth + .08f), Quaternion.identity);
                for (var i = -1; i <= 1; i++)
                {
                    mesh.Box(1, new Vector3(i * .16f, roofY + .15f, 0), new Vector3(.10f, .18f, .18f), Quaternion.identity);
                    mesh.Box(5, new Vector3(i * .16f, roofY + .15f, -.095f), new Vector3(.045f, .11f, .018f), Quaternion.identity);
                }
            }
            else if (id == "Factory" || id == "Manufactorum" || id == "Smelter" || id == "Refinery" || id == "PromethiumGenerator")
            {
                mesh.Box(2, new Vector3(0, roofY + .025f, 0), new Vector3(width + .08f, .05f, depth + .08f), Quaternion.identity);
                mesh.Box(1, new Vector3(-.12f, roofY + .15f, .06f), new Vector3(.13f, .22f, .14f), Quaternion.identity);
                mesh.Box(4, new Vector3(-.12f, roofY + .27f, .06f), new Vector3(.18f, .04f, .19f), Quaternion.identity);
                mesh.Box(1, new Vector3(.13f, roofY + .10f, -.08f), new Vector3(.14f, .12f, .15f), Quaternion.identity);
            }
            else
            {
                for (var side = -1; side <= 1; side += 2)
                {
                    var panelRotation = Quaternion.Euler(side * 27f, 0, 0);
                    mesh.Box(2, new Vector3(0, roofY + .06f, side * .14f), new Vector3(width + .10f, .04f, .32f), panelRotation);
                }
                mesh.Box(3, new Vector3(0, roofY + .11f, 0), new Vector3(width + .12f, .04f, .045f), Quaternion.identity);
            }
        }

        private LineRenderer Line(string title, Material material, int count, bool loop)
        {
            var go = new GameObject(title, typeof(LineRenderer)); go.transform.SetParent(architecture.transform, false);
            var line = go.GetComponent<LineRenderer>(); line.useWorldSpace = false; line.sharedMaterial = material;
            line.widthMultiplier = .0035f; line.positionCount = count; line.loop = loop; return line;
        }

        private void Update()
        {
            if (building == null) building = GetComponent<ManagedBuilding>();
            if (building == null) return;
            if (architecture == null || scaffold == null || scaffoldPosts == null || completion == null) Rebuild();
            Animate();
        }

        private void Animate()
        {
            var state = building.State;
            var progress = state.improvementWork / AdaptiveBuildingRules.Duration(state);
            for (var i = 0; i < growingAnnexes.Count; i++)
            {
                var annex = growingAnnexes[i];
                var target = growingAnnexTargets[i];
                var targetPlanar = new Vector3(target.x, 0, target.z);
                var direction = targetPlanar.normalized;
                var startRadius = building.State.level == 0 ? .52f : AnnexRadius(building.State.level - 1) + .20f;
                annex.localPosition = direction * Mathf.Lerp(startRadius, targetPlanar.magnitude, progress) + Vector3.up * target.y;
                annex.localScale = new Vector3(.35f + .65f * progress, Mathf.Max(.02f, progress), .35f + .65f * progress);
            }
            foreach (var crown in growingCrowns) crown.localScale = new Vector3(1, Mathf.Max(.02f, progress), 1);
            scaffold.enabled = state.improving;
            scaffoldPosts.enabled = state.improving;
            if (state.improving)
            {
                var radius = .72f + (OuterRadius(state.level) - .72f) * progress;
                var height = .45f * progress;
                for (var i = 0; i < scaffold.positionCount; i++)
                {
                    var angle = i * Mathf.PI * 2 / (scaffold.positionCount - 1);
                    scaffold.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, height, Mathf.Sin(angle) * radius));
                }
                for (var i = 0; i < scaffoldPosts.positionCount; i++)
                {
                    var angle = (i / 2) * Mathf.PI * 2 / (scaffoldPosts.positionCount / 2);
                    scaffoldPosts.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, i % 2 == 0 ? 0 : height, Mathf.Sin(angle) * radius));
                }
            }
            completion.enabled = state.completionSeconds > 0;
            if (completion.enabled)
            {
                var radius = .72f + (1 - state.completionSeconds / 6) * .42f;
                for (var i = 0; i < completion.positionCount; i++)
                {
                    var angle = i * Mathf.PI * 2 / (completion.positionCount - 1);
                    completion.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, .08f, Mathf.Sin(angle) * radius));
                }
            }
        }

        internal sealed class AdaptiveMeshBuilder
        {
            private readonly List<Vector3> vertices = new List<Vector3>();
            private readonly List<int>[] triangles = new List<int>[7];
            public AdaptiveMeshBuilder() { for (var i = 0; i < triangles.Length; i++) triangles[i] = new List<int>(); }

            public void Box(int material, Vector3 center, Vector3 size, Quaternion rotation)
            {
                if (material <= 1 && Mathf.Min(size.x, Mathf.Min(size.y, size.z)) > .08f)
                { ArchitectureGeometry.BevelBox(material, center, size, rotation, .0015f, Quad); return; }
                var half = size * .5f;
                var p = new Vector3[8];
                for (var i = 0; i < 8; i++)
                    p[i] = center + rotation * new Vector3((i & 1) == 0 ? -half.x : half.x,
                        (i & 2) == 0 ? -half.y : half.y, (i & 4) == 0 ? -half.z : half.z);
                Quad(material, p[0], p[2], p[3], p[1]); Quad(material, p[4], p[5], p[7], p[6]);
                Quad(material, p[0], p[1], p[5], p[4]); Quad(material, p[2], p[6], p[7], p[3]);
                Quad(material, p[0], p[4], p[6], p[2]); Quad(material, p[1], p[3], p[7], p[5]);
            }

            public void Band(float inner, float outer, float bottom, float top, int segments,
                int wallMaterial, int trimMaterial, int innerMaterial, Vector2 offset = default)
            {
                for (var i = 0; i < segments; i++)
                {
                    var a = i * Mathf.PI * 2 / segments; var b = (i + 1) * Mathf.PI * 2 / segments;
                    var ia0 = Point(inner, bottom, a, offset); var ib0 = Point(inner, bottom, b, offset);
                    var oa0 = Point(outer, bottom, a, offset); var ob0 = Point(outer, bottom, b, offset);
                    var ia1 = Point(inner, top, a, offset); var ib1 = Point(inner, top, b, offset);
                    var oa1 = Point(outer, top, a, offset); var ob1 = Point(outer, top, b, offset);
                    Quad(wallMaterial, oa0, ob0, ob1, oa1);
                    Quad(innerMaterial, ia0, ia1, ib1, ib0);
                    Quad(trimMaterial, ia1, oa1, ob1, ib1);
                    Quad(innerMaterial, ia0, ib0, ob0, oa0);
                }
            }
            private static Vector3 Point(float radius, float y, float angle, Vector2 offset)
                => new Vector3(Mathf.Cos(angle) * radius + offset.x, y, Mathf.Sin(angle) * radius + offset.y);
            private void Quad(int material, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                var start = vertices.Count; vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
                var indices = triangles[Mathf.Clamp(material, 0, triangles.Length - 1)];
                indices.Add(start); indices.Add(start + 1); indices.Add(start + 2);
                indices.Add(start); indices.Add(start + 2); indices.Add(start + 3);
            }
            public Mesh Build(string name)
            {
                var mesh = new Mesh { name = "WH30K " + name };
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.SetVertices(vertices); mesh.subMeshCount = triangles.Length;
                for (var i = 0; i < triangles.Length; i++) mesh.SetTriangles(triangles[i], i);
                mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
            }
        }
    }
}
