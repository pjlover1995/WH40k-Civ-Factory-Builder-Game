using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WH30K.Gameplay.Construction
{
    /// <summary>Shared, batched gothic-industrial architecture for the core, construction and its preview.</summary>
    public static class BuildingVisuals
    {
        public const float WorldScale = 0.2f;
        private static readonly Dictionary<string, Mesh> Meshes = new Dictionary<string, Mesh>();
        private static Material[] materials;
        private const int Stone = 0, Iron = 1, Roof = 2, Brass = 3, Amber = 4, Cyan = 5, Dark = 6;
        public static Material[] ServiceRoadMaterials => new[] { GetMaterials()[Iron], GetMaterials()[Brass] };

        public static GameObject Create(string id, string name, Transform parent, Vector3 dimensions,
            Material preview = null)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.transform.localScale = dimensions;
            if (!Meshes.TryGetValue(id, out var mesh) || mesh == null)
            {
                var builder = new Architecture();
                Foundation(builder);
                switch (id)
                {
                    case "HabBlock": Habitation(builder); break;
                    case "Factory": Manufactorum(builder); break;
                    case "Utility": Scrubber(builder); break;
                    default: Outpost(builder); break;
                }
                mesh = builder.Build(id);
                Meshes[id] = mesh;
            }
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.GetComponent<MeshRenderer>();
            if (preview != null)
            {
                var overrides = new Material[7];
                for (var i = 0; i < overrides.Length; i++) overrides[i] = preview;
                renderer.sharedMaterials = overrides;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
            }
            else renderer.sharedMaterials = GetMaterials();
            return go;
        }

        private static void Foundation(Architecture b)
        {
            b.Box(Stone, new Vector3(0, 1, 0), new Vector3(100, 14, 100));
            b.Box(Iron, new Vector3(0, 9, 0), new Vector3(96, 3, 96));
            for (var x = -1; x <= 1; x += 2)
                for (var z = -1; z <= 1; z += 2)
                {
                    b.Box(Brass, new Vector3(x * 44, 12, z * 44), new Vector3(6, 4, 6));
                    b.Box(Amber, new Vector3(x * 44, 15, z * 44), new Vector3(2, 3, 2));
                }
            for (var i = 0; i < 5; i++)
                b.Box(Stone, new Vector3(0, 2 + i * 1.6f, -48 + i * 2), new Vector3(24, 3, 4));
        }

        private static void Habitation(Architecture b)
        {
            b.Box(Stone, new Vector3(0, 58, 0), new Vector3(65, 96, 68));
            b.Box(Iron, new Vector3(0, 106, 0), new Vector3(70, 5, 74));
            b.Gable(Roof, new Vector3(0, 109, 0), 71, 24, 76);
            for (var floor = 0; floor < 4; floor++)
            {
                var y = 29 + floor * 20;
                b.Box(Iron, new Vector3(0, y - 7, 0), new Vector3(68, 3, 71));
                for (var x = -1; x <= 1; x++)
                {
                    Window(b, x * 19, y, -34.8f, 8, 12);
                    Window(b, x * 19, y, 34.8f, 8, 12);
                }
                for (var side = -1; side <= 1; side += 2)
                    for (var z = -1; z <= 1; z++)
                    {
                        b.Box(Dark, new Vector3(side * 33, y + 5, z * 21), new Vector3(2, 14, 9));
                        b.Box(Amber, new Vector3(side * 34.1f, y + 6, z * 21), new Vector3(1, 10, 3));
                    }
            }
            for (var side = -1; side <= 1; side += 2)
                for (var z = -1; z <= 1; z++)
                {
                    b.Box(Stone, new Vector3(side * 39, 43, z * 27), new Vector3(10, 67, 8));
                    b.Beam(Brass, new Vector3(side * 43, 17, z * 27), new Vector3(side * 32, 100, z * 27), 3);
                    b.Cylinder(Roof, new Vector3(side * 39, 79, z * 27), 6, 0, 15, 4);
                }
            Tower(b, new Vector3(0, 108, -24), 16, 31);
            b.Arch(Dark, new Vector3(0, 10, -36), 20, 26, 4);
            b.Arch(Brass, new Vector3(0, 11, -38.3f), 13, 22, 1);
            b.Box(Dark, new Vector3(0, 20, -39), new Vector3(9, 18, 1));
            Crest(b, new Vector3(0, 93, -37));
        }

        private static void Manufactorum(Architecture b)
        {
            b.Box(Stone, new Vector3(0, 36, 4), new Vector3(84, 51, 70));
            b.Box(Iron, new Vector3(0, 65, 4), new Vector3(88, 7, 74));
            for (var i = 0; i < 4; i++)
            {
                var z = -25 + i * 18;
                b.Gable(Roof, new Vector3(0, 69, z), 86, 17, 17);
                b.Beam(Brass, new Vector3(-43, 70, z - 7), new Vector3(0, 87, z - 7), 2);
                b.Beam(Brass, new Vector3(0, 87, z - 7), new Vector3(43, 70, z - 7), 2);
                for (var side = -1; side <= 1; side += 2)
                {
                    b.Box(Iron, new Vector3(side * 44, 37, z), new Vector3(5, 52, 5));
                    b.Box(Dark, new Vector3(side * 43, 41, z), new Vector3(2, 19, 10));
                    b.Box(Amber, new Vector3(side * 44.3f, 40, z), new Vector3(1, 12, 7));
                }
            }
            for (var i = 0; i < 3; i++)
            {
                var x = -26 + i * 26;
                var h = i == 1 ? 81 : 65;
                b.Cylinder(Stone, new Vector3(x, 64, 28), 11, 9, 26);
                b.Cylinder(Iron, new Vector3(x, 80, 28), 6, 5, h);
                for (var ring = 0; ring < 3; ring++)
                    b.Cylinder(Brass, new Vector3(x, 88 + ring * 22, 28), 7, 7, 3);
                b.Cylinder(Iron, new Vector3(x, 80 + h, 28), 8, 8, 5);
                b.Cylinder(Amber, new Vector3(x, 85 + h, 28), 5.2f, 5.2f, 0.7f);
            }
            b.Arch(Brass, new Vector3(0, 11, -32), 32, 37, 5);
            b.Arch(Dark, new Vector3(0, 11, -35), 25, 31, 2);
            for (var i = 0; i < 5; i++)
                b.Box(Iron, new Vector3(0, 15 + i * 5, -36.5f), new Vector3(24, 1.3f, 1));
            for (var side = -1; side <= 1; side += 2)
            {
                b.Cylinder(Iron, new Vector3(side * 33, 13, -38), 7, 7, 27);
                b.Cylinder(Brass, new Vector3(side * 33, 39, -38), 8, 8, 3);
                b.Beam(Iron, new Vector3(side * 33, 40, -38), new Vector3(side * 33, 40, -15), 5);
                Window(b, side * 27, 49, -32.5f, 9, 10);
            }
            Crest(b, new Vector3(0, 59, -36));
        }

        private static void Scrubber(Architecture b)
        {
            b.Box(Stone, new Vector3(0, 30, -20), new Vector3(76, 40, 40));
            b.Gable(Roof, new Vector3(0, 50, -20), 79, 15, 44);
            for (var side = -1; side <= 1; side += 2)
            {
                var x = side * 25;
                b.Cylinder(Iron, new Vector3(x, 12, 20), 19, 17, 91, 20);
                b.Cylinder(Stone, new Vector3(x, 10, 20), 21, 21, 13, 20);
                for (var ring = 0; ring < 5; ring++)
                {
                    b.Cylinder(Brass, new Vector3(x, 29 + ring * 17, 20), 19, 19, 3, 20);
                    for (var vane = 0; vane < 8; vane++)
                    {
                        var a = vane * Mathf.PI / 4;
                        b.Beam(Iron, new Vector3(x + Mathf.Cos(a) * 18, 31 + ring * 17, 20 + Mathf.Sin(a) * 18),
                            new Vector3(x + Mathf.Cos(a) * 18, 43 + ring * 17, 20 + Mathf.Sin(a) * 18), 2);
                    }
                }
                b.Cylinder(Dark, new Vector3(x, 104, 20), 16, 16, 1, 20);
                b.Cylinder(Cyan, new Vector3(x, 105, 20), 12, 12, 1, 20);
                for (var blade = 0; blade < 6; blade++)
                {
                    var a = blade * Mathf.PI / 3;
                    b.Beam(Iron, new Vector3(x, 107, 20),
                        new Vector3(x + Mathf.Cos(a) * 15, 107, 20 + Mathf.Sin(a) * 15), 3);
                }
                b.Cylinder(Iron, new Vector3(x, 107, 20), 4, 4, 4);
                b.Beam(Iron, new Vector3(x, 24, -4), new Vector3(x, 24, 20), 10);
                Window(b, x, 29, -40.8f, 9, 15, Cyan);
            }
            b.Box(Iron, new Vector3(0, 85, -12), new Vector3(18, 56, 20));
            b.Cylinder(Brass, new Vector3(0, 112, -12), 11, 0, 31, 4);
            b.Beam(Iron, new Vector3(0, 130, -12), new Vector3(0, 157, -12), 1.4f);
            b.Arch(Dark, new Vector3(0, 11, -42), 18, 24, 3);
            Crest(b, new Vector3(0, 44, -43));
        }

        private static void Outpost(Architecture b)
        {
            b.Box(Stone, new Vector3(0, 32, 0), new Vector3(81, 43, 73));
            b.Box(Iron, new Vector3(0, 54, 0), new Vector3(86, 5, 78));
            b.Box(Stone, new Vector3(0, 77, 7), new Vector3(44, 44, 41));
            b.Gable(Roof, new Vector3(0, 99, 7), 49, 25, 47);
            for (var side = -1; side <= 1; side += 2)
                for (var z = -1; z <= 1; z += 2)
                {
                    var center = new Vector3(side * 34, 11, z * 31);
                    b.Box(Stone, center + Vector3.up * 31, new Vector3(18, 62, 18));
                    b.Box(Brass, center + Vector3.up * 63, new Vector3(21, 4, 21));
                    b.Cylinder(Roof, center + Vector3.up * 65, 14, 0, 24, 4);
                    b.Box(Amber, center + new Vector3(0, 41, z * 10), new Vector3(4, 14, 1));
                }
            for (var i = 0; i < 7; i++)
            {
                b.Box(Stone, new Vector3(-36 + i * 12, 62, -37), new Vector3(7, 12, 6));
                b.Box(Stone, new Vector3(-36 + i * 12, 62, 37), new Vector3(7, 12, 6));
            }
            Tower(b, new Vector3(0, 123, 7), 12, 19);
            b.Beam(Iron, new Vector3(0, 148, 7), new Vector3(0, 160, 7), 1.2f);
            b.Arch(Brass, new Vector3(0, 11, -38), 28, 34, 3);
            b.Arch(Dark, new Vector3(0, 11, -40), 21, 28, 2);
            Window(b, -13, 76, -14, 8, 17);
            Window(b, 13, 76, -14, 8, 17);
            Crest(b, new Vector3(0, 47, -40));
        }

        private static void Window(Architecture b, float x, float y, float z, float w, float h, int light = Amber)
        {
            var sign = z < 0 ? -1f : 1f;
            b.Arch(Brass, new Vector3(x, y, z), w + 3, h + 3, 1.5f);
            b.Arch(Dark, new Vector3(x, y + 1, z + sign), w, h, 1.2f);
            b.Arch(light, new Vector3(x, y + 3, z + sign * 1.8f), w * 0.55f, h - 4, 0.7f);
            b.Box(Iron, new Vector3(x, y + h * 0.47f, z + sign * 2.3f), new Vector3(1.1f, h - 2, 0.8f));
        }

        private static void Tower(Architecture b, Vector3 p, float width, float height)
        {
            b.Box(Stone, p + Vector3.up * height * 0.5f, new Vector3(width, height, width));
            b.Box(Brass, p + Vector3.up * height, new Vector3(width + 3, 2, width + 3));
            b.Cylinder(Roof, p + Vector3.up * (height + 1), width * 0.8f, 0, 20, 4);
        }

        private static void Crest(Architecture b, Vector3 p)
        {
            // A winged cog insignia, formed from relief geometry rather than a flat decal.
            b.Box(Brass, p, new Vector3(6, 9, 2));
            for (var side = -1; side <= 1; side += 2)
                for (var i = 0; i < 3; i++)
                    b.Beam(Brass, p + new Vector3(side * 3, 2 - i * 2, 0),
                        p + new Vector3(side * (15 - i * 3), 5 - i * 3, 0), 1.7f);
            b.Box(Dark, p + new Vector3(0, 1, -1.3f), new Vector3(2, 3, 0.5f));
        }

        private static Material[] GetMaterials()
        {
            if (materials != null && materials[0] != null) return materials;
            materials = new[]
            {
                Surface("Weathered limestone", new Color(0.43f, 0.44f, 0.40f), 0.08f, 0.24f, 17),
                Surface("Forged iron", new Color(0.16f, 0.19f, 0.20f), 0.75f, 0.40f, 29),
                Surface("Oxide roof", new Color(0.27f, 0.075f, 0.055f), 0.5f, 0.32f, 43),
                Surface("Aged brass", new Color(0.47f, 0.32f, 0.13f), 0.72f, 0.43f, 59),
                Emissive("Amber lumen", new Color(1f, 0.52f, 0.14f)),
                Emissive("Filter status lumen", new Color(0.15f, 0.78f, 0.74f)),
                Surface("Recesses", new Color(0.035f, 0.043f, 0.045f), 0.25f, 0.15f, 71)
            };
            return materials;
        }

        private static Material Surface(string name, Color color, float metal, float gloss, int seed)
        {
            var material = new Material(Shader.Find("Standard")) { name = name, color = color };
            material.SetFloat("_Metallic", metal);
            material.SetFloat("_Glossiness", gloss);
            var texture = new Texture2D(128, 128, TextureFormat.RGB24, true) { name = name + " grain", wrapMode = TextureWrapMode.Repeat };
            var pixels = new Color[128 * 128];
            var random = new System.Random(seed);
            for (var y = 0; y < 128; y++)
                for (var x = 0; x < 128; x++)
                {
                    var grain = 0.78f + (float)random.NextDouble() * 0.2f;
                    var joint = y % 32 == 0 || (x + (y / 32 % 2) * 32) % 64 == 0;
                    pixels[x + y * 128] = Color.white * (joint ? grain * 0.63f : grain);
                }
            texture.SetPixels(pixels);
            texture.Apply(true, true);
            material.mainTexture = texture;
            return material;
        }

        private static Material Emissive(string name, Color color)
        {
            var material = new Material(Shader.Find("Standard")) { name = name, color = color * 0.24f };
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * 1.1f);
            material.SetFloat("_Glossiness", 0.65f);
            return material;
        }

        private sealed class Architecture
        {
            private readonly List<Vector3> vertices = new List<Vector3>();
            private readonly List<Vector3> normals = new List<Vector3>();
            private readonly List<Vector2> uvs = new List<Vector2>();
            private readonly List<int>[] triangles = new List<int>[7];
            public Architecture() { for (var i = 0; i < 7; i++) triangles[i] = new List<int>(); }
            private static Vector3 Unit(Vector3 p) => Vector3.Scale(p, new Vector3(0.01f, 1f / 166f, 0.01f));

            private void Quad(int material, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                var start = vertices.Count;
                a = Unit(a); b = Unit(b); c = Unit(c); d = Unit(d);
                var cross = Vector3.Cross(b - a, c - a);
                if (cross.sqrMagnitude < 1e-12f) cross = Vector3.Cross(b - a, d - a);
                var normal = cross.sqrMagnitude > 1e-12f ? cross.normalized : Vector3.up;
                vertices.AddRange(new[] { a, b, c, d });
                for (var i = 0; i < 4; i++) normals.Add(normal);
                uvs.AddRange(new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right });
                triangles[material].AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
            }

            public void Box(int material, Vector3 center, Vector3 size) => OrientedBox(material, center, size, Quaternion.identity);
            public void Beam(int material, Vector3 a, Vector3 b, float width)
                => OrientedBox(material, (a + b) * 0.5f, new Vector3(width, (b - a).magnitude, width), Quaternion.FromToRotation(Vector3.up, b - a));
            private void OrientedBox(int material, Vector3 center, Vector3 size, Quaternion rotation)
            {
                var p = new Vector3[8];
                for (var i = 0; i < 8; i++)
                    p[i] = center + rotation * Vector3.Scale(size * 0.5f,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Quad(material, p[0], p[2], p[3], p[1]); Quad(material, p[5], p[7], p[6], p[4]);
                Quad(material, p[4], p[6], p[2], p[0]); Quad(material, p[1], p[3], p[7], p[5]);
                Quad(material, p[2], p[6], p[7], p[3]); Quad(material, p[4], p[0], p[1], p[5]);
            }

            public void Gable(int material, Vector3 p, float width, float height, float depth)
                => Prism(material, p, new[] { new Vector2(-width / 2, 0), new Vector2(width / 2, 0), new Vector2(0, height) }, depth);
            public void Arch(int material, Vector3 p, float width, float height, float depth)
                => Prism(material, p, new[] { new Vector2(-width / 2, 0), new Vector2(width / 2, 0),
                    new Vector2(width / 2, height * 0.68f), new Vector2(0, height), new Vector2(-width / 2, height * 0.68f) }, depth);
            private void Prism(int material, Vector3 p, Vector2[] polygon, float depth)
            {
                var front = new Vector3[polygon.Length]; var back = new Vector3[polygon.Length];
                for (var i = 0; i < polygon.Length; i++)
                {
                    front[i] = p + new Vector3(polygon[i].x, polygon[i].y, -depth / 2);
                    back[i] = front[i] + Vector3.forward * depth;
                }
                for (var i = 1; i < polygon.Length - 1; i++)
                {
                    Quad(material, front[0], front[i + 1], front[i], front[i]);
                    Quad(material, back[0], back[i], back[i + 1], back[i + 1]);
                }
                for (var i = 0; i < polygon.Length; i++)
                {
                    var next = (i + 1) % polygon.Length;
                    Quad(material, front[i], front[next], back[next], back[i]);
                }
            }

            public void Cylinder(int material, Vector3 p, float bottom, float top, float height, int sides = 16)
            {
                for (var i = 0; i < sides; i++)
                {
                    var a = i * Mathf.PI * 2 / sides; var c = (i + 1) * Mathf.PI * 2 / sides;
                    var v = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)); var w = new Vector3(Mathf.Cos(c), 0, Mathf.Sin(c));
                    Quad(material, p + v * bottom, p + v * top + Vector3.up * height,
                        p + w * top + Vector3.up * height, p + w * bottom);
                    Quad(material, p, p + v * bottom, p + w * bottom, p + w * bottom);
                    var peak = p + Vector3.up * height;
                    Quad(material, peak, peak + w * top, peak + v * top, peak + v * top);
                }
            }

            public Mesh Build(string id)
            {
                var mesh = new Mesh { name = id + " gothic architecture", indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0, uvs); mesh.subMeshCount = 7;
                for (var i = 0; i < 7; i++) mesh.SetTriangles(triangles[i], i);
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
