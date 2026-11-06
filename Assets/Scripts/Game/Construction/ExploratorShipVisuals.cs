using UnityEngine;

namespace WH30K.Gameplay.Construction
{
    public static partial class BuildingVisuals
    {
        private static Mesh distantShipMesh;
        private static void AddDistantShip(GameObject ship, MeshRenderer detailed)
        {
            if (distantShipMesh == null)
            {
                var b = new Architecture();
                // Keep the broadside, blunt bow, red spine and tall aft cathedral.
                // The same metric conversion and crash pose are applied as on the full hull.
                b.Box(Stone, new Vector3(0, 29, 2), new Vector3(48, 34, 96));
                b.Box(Roof, new Vector3(0, 49, 0), new Vector3(37, 8, 76));
                b.Box(Iron, new Vector3(0, 59, -24), new Vector3(31, 16, 33));
                b.Box(Stone, new Vector3(0, 91, -33), new Vector3(19, 62, 18));
                b.Box(Brass, new Vector3(0, 137, -33), new Vector3(7, 30, 7));
                b.Box(Iron, new Vector3(0, 27, -41), new Vector3(76, 25, 16));
                distantShipMesh = b.Build("ColonyShip");
                distantShipMesh.name = "Explorator distant silhouette";
            }
            var proxy = new GameObject("Distant Explorator silhouette", typeof(MeshFilter), typeof(MeshRenderer));
            proxy.transform.SetParent(ship.transform, false);
            proxy.GetComponent<MeshFilter>().sharedMesh = distantShipMesh;
            var distant = proxy.GetComponent<MeshRenderer>();
            distant.sharedMaterials = detailed.sharedMaterials;
            distant.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var lod = ship.AddComponent<LODGroup>();
            // Approximately 2% screen height: use the inexpensive silhouette through orbit.
            lod.SetLODs(new[] { new LOD(.02f, new Renderer[] { detailed }),
                new LOD(.000001f, new Renderer[] { distant }) });
            lod.RecalculateBounds();
        }

        // +Z prow. The long low gun deck and stern cathedral follow the supplied fleet miniature.
        private static void ExploratorShip(Architecture b)
        {
            var stations = new[] { -46f, -36f, -25f, -10f, -2f, 10f, 25f, 35f, 43f, 50f };
            var widths = new[] { 30f, 34f, 30f, 28f, 28f, 27f, 26f, 29f, 24f, 19f };
            for (var i = 0; i < stations.Length - 1; i++)
            {
                var a = ShipHullRing(stations[i], widths[i]);
                var c = ShipHullRing(stations[i + 1], widths[i + 1]);
                for (var face = 0; face < a.Length; face++)
                {
                    // Remove shell faces at the starboard breach, exposing the internal frame.
                    if (i == 4 && (face == 1 || face == 2)) continue;
                    var next = (face + 1) % a.Length;
                    b.Quad(face == 0 ? Roof : Stone, a[face], c[face], c[next], a[next]);
                }
            }
            foreach (var end in new[] { 0, stations.Length - 1 })
            {
                var ring = ShipHullRing(stations[end], widths[end]);
                for (var i = 0; i < ring.Length; i++)
                {
                    var next = (i + 1) % ring.Length; var center = new Vector3(0, 26, stations[end]);
                    if (end == 0) b.Triangle(Iron, center, ring[i], ring[next]);
                    else b.Triangle(Iron, center, ring[next], ring[i]);
                }
            }
            b.Box(Stone, new Vector3(0, 44, 4), new Vector3(37, 12, 77));
            b.Box(Roof, new Vector3(0, 51, 4), new Vector3(39, 2.5f, 76));
            for (var tier = 0; tier < 3; tier++)
            {
                b.Box(Iron, new Vector3(0, 55 + tier * 6, -17 - tier * 7), new Vector3(34 - tier * 4, 5, 43 - tier * 10));
                b.Box(Roof, new Vector3(0, 58 + tier * 6, -17 - tier * 7), new Vector3(36 - tier * 4, 1, 44 - tier * 10));
            }
            // Blunt armoured bow with brass ram teeth, rather than a tall pointed prow.
            b.Box(Stone, new Vector3(0, 32, 43), new Vector3(38, 31, 13));
            b.Box(Brass, new Vector3(0, 25, 49.5f), new Vector3(33, 22, 1));
            b.Box(Dark, new Vector3(0, 39, 50), new Vector3(26, 3, .15f));
            for (var tooth = -2; tooth <= 2; tooth++)
                b.Box(Iron, new Vector3(tooth * 6.6f, 25, 50), new Vector3(2, 25, .2f));
            for (var side = -1; side <= 1; side += 2)
            {
                b.Box(Roof, new Vector3(side * 29, 54, 1), new Vector3(3, 3, 69));
                b.Box(Brass, new Vector3(side * 30, 51.5f, 1), new Vector3(1, 1, 69));
                b.Box(Brass, new Vector3(side * 30, 26, 1), new Vector3(1.5f, 2, 69));
                for (var bay = 0; bay < 10; bay++)
                {
                    var z = -24 + bay * 6;
                    if (side == 1 && (bay == 5 || bay == 6))
                    {
                        b.Beam(Iron, new Vector3(26, 28, z - 2), new Vector3(37, 43, z + 1), 1.6f);
                        b.Beam(Brass, new Vector3(28, 49, z - 2), new Vector3(34, 42, z + 1), .9f);
                        b.Beam(Cyan, new Vector3(23, 30, z - 2), new Vector3(24, 43, z + 2), .45f);
                        continue;
                    }
                    ShipSideArch(b, side, z);
                    for (var gun = 0; gun < 2; gun++)
                    {
                        b.Beam(Iron, new Vector3(side * 29, 34 + gun * 7, z), new Vector3(side * 35, 34 + gun * 7, z), 1.3f);
                        b.Box(Brass, new Vector3(side * 31, 33 + gun * 7, z), new Vector3(1, 2.3f, 3));
                    }
                }
                b.Box(Roof, new Vector3(side * 25, 40, 37), new Vector3(2, 12, 8));
                ShipCog(b, side, 30.8f, 40, 29, 4);
                for (var rib = 0; rib < 6; rib++)
                    b.Box(Brass, new Vector3(side * 26, 37, 35 + rib * 1.5f), new Vector3(1, 7, .35f));
                // Broad stern transom, projecting sponsons, and a broken starboard skeg.
                b.Box(Stone, new Vector3(side * 33, 40, -37), new Vector3(19, 31, 17));
                b.Box(Roof, new Vector3(side * 32, 57, -37), new Vector3(22, 3, 19));
                b.Box(Brass, new Vector3(side * 42, 43, -37), new Vector3(1, 20, 12));
                b.Box(Iron, new Vector3(side * 33, 17, -36), new Vector3(21, 5, 22));
                b.Beam(Iron, new Vector3(side * 35, 19, -39), new Vector3(side * 39, side < 0 ? 3 : 9, -34), 3);
                for (var drive = 0; drive < 2; drive++)
                    ShipDrive(b, new Vector3(side * (13 + drive * 21), 39, -44), side < 0 && drive == 1);
            }
            // Aft command cathedral: a narrow vertical keep over horizontal oxblood terraces.
            b.Box(Stone, new Vector3(0, 78, -33), new Vector3(30, 22, 19));
            b.Box(Roof, new Vector3(0, 90, -33), new Vector3(33, 2, 21));
            b.Box(Stone, new Vector3(0, 113, -34), new Vector3(20, 44, 13));
            b.Box(Stone, new Vector3(0, 133, -35), new Vector3(13, 15, 10));
            for (var side = -1; side <= 1; side += 2)
            {
                b.Beam(Brass, new Vector3(side * 13, 72, -25), new Vector3(side * 9, 137, -29), 1.1f);
                for (var window = 0; window < 3; window++)
                    b.Arch(Dark, new Vector3(side * (3 + window * 2.5f), 102, -27.35f), 1.3f, 24, .3f);
                ShipCog(b, side, 15.6f, 78, -32, 4.8f);
            }
            for (var level = 0; level < 3; level++)
            {
                b.Box(Brass, new Vector3(0, 96 + level * 15, -34), new Vector3(21, 1, 14));
                b.Box(Amber, new Vector3(0, 82 + level * 4, -23.3f), new Vector3(21, .7f, .4f));
            }
            for (var spire = -2; spire <= 2; spire++)
            {
                var top = 163 - Mathf.Abs(spire) * 5;
                b.Cylinder(Iron, new Vector3(spire * 3, 138, -35), 1.1f, .65f, top - 141, 8);
                b.Cylinder(Brass, new Vector3(spire * 3, top - 3, -35), 1, 0, 3, 8);
            }
            for (var z = -14; z <= 31; z += 9)
            {
                b.Box(Iron, new Vector3(0, 56, z), new Vector3(13, 6, 5));
                for (var slit = -2; slit <= 2; slit++)
                    b.Box(Dark, new Vector3(slit * 2.2f, 59.1f, z), new Vector3(1, .2f, 3));
            }
            // Localised impact damage leaves the main structure intact and usable.
            b.Box(Dark, new Vector3(20, 33, 4), new Vector3(3, 24, 13));
            for (var z = -1; z <= 11; z += 3)
                b.Beam(Iron, new Vector3(21, 12, z), new Vector3(30, 29, z + 1), 1.2f);
            b.Quad(Iron, new Vector3(27, 16, 0), new Vector3(41, 8, 2), new Vector3(39, 10, 11), new Vector3(26, 20, 10));
            b.Quad(Roof, new Vector3(29, 49, 5), new Vector3(38, 43, 7), new Vector3(36, 39, 12), new Vector3(28, 46, 11));
            for (var plate = 0; plate < 4; plate++)
                b.Box(plate % 2 == 0 ? Dark : Iron, new Vector3(35 + plate * 2, 12 + plate, 15 + plate * 2), new Vector3(6, 2, 3));
            b.Beam(Iron, new Vector3(-10, 10, 20), new Vector3(-7, 4, 40), 3);
            b.Beam(Iron, new Vector3(10, 10, 20), new Vector3(15, 6, 37), 3);
            for (var patch = 0; patch < 4; patch++)
                b.Box(Dark, new Vector3(12, 52.5f, -10 + patch * 4), new Vector3(7, .6f, 2.5f));
        }

        private static Vector3[] ShipHullRing(float z, float w) => new[]
        {
            new Vector3(-w * .8f, 44, z), new Vector3(w * .8f, 44, z),
            new Vector3(w, 34, z), new Vector3(w * .7f, 12, z),
            new Vector3(-w * .7f, 12, z), new Vector3(-w, 34, z)
        };

        private static void ShipSideArch(Architecture b, int side, float z)
        {
            // Pointed recesses face the broadside, rather than the bow/stern.
            var x = side * 29f;
            Vector3 P(float y, float dz) => new Vector3(x, y, z + dz);
            void Face(Vector3 a, Vector3 c, Vector3 d)
            { if (side > 0) b.Triangle(Dark, a, d, c); else b.Triangle(Dark, a, c, d); }
            Face(P(29, -2.4f), P(29, 2.4f), P(44, 2.4f));
            Face(P(29, -2.4f), P(44, 2.4f), P(44, -2.4f));
            Face(P(44, -2.4f), P(44, 2.4f), P(49, 0));
            foreach (var edge in new[] { -1, 1 })
            {
                b.Beam(Brass, new Vector3(side * 30, 28, z + edge * 2.6f), new Vector3(side * 30, 44, z + edge * 2.6f), .65f);
                b.Beam(Brass, new Vector3(side * 30, 44, z + edge * 2.6f), new Vector3(side * 30, 50, z), .65f);
            }
        }

        private static void ShipCog(Architecture b, int side, float x, float y, float z, float radius)
        {
            for (var i = 0; i < 16; i++)
            {
                var a = i * Mathf.PI / 8; var c = (i + 1) * Mathf.PI / 8;
                b.Beam(Brass, new Vector3(side * x, y + Mathf.Sin(a) * radius, z + Mathf.Cos(a) * radius),
                    new Vector3(side * x, y + Mathf.Sin(c) * radius, z + Mathf.Cos(c) * radius), .6f);
                if (i % 2 == 0) b.Box(Brass, new Vector3(side * x, y + Mathf.Sin(a) * (radius + .5f),
                    z + Mathf.Cos(a) * (radius + .5f)), new Vector3(.9f, 1.3f, 1.3f));
            }
            b.Box(Brass, new Vector3(side * x, y, z), new Vector3(1, radius, radius * .65f));
            b.Box(Dark, new Vector3(side * (x + .6f), y + .6f, z), new Vector3(.2f, .7f, radius * .45f));
        }

        private static void ShipDrive(Architecture b, Vector3 center, bool dead)
        {
            // Ribbed engine bells run along Z, with the opening at the stern.
            for (var ring = 0; ring < 5; ring++)
                for (var segment = 0; segment < 20; segment++)
                {
                    var radius = 7 + ring * .55f;
                    var a = segment * Mathf.PI / 10; var c = (segment + 1) * Mathf.PI / 10;
                    Vector3 P(float angle, float r, float z) => center + new Vector3(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r * 1.8f, z);
                    b.Quad(ring % 2 == 0 ? Brass : Iron, P(a, radius, -ring * 1.1f), P(a, radius + .45f, -ring * 1.1f - .65f),
                        P(c, radius + .45f, -ring * 1.1f - .65f), P(c, radius, -ring * 1.1f));
                    if (ring == 0)
                        b.Quad(Iron, P(a, 7, 0), P(a, 9, -5), P(c, 9, -5), P(c, 7, 0));
                    if (ring == 4) b.Triangle(Dark, center + Vector3.back * 5, P(c, 9, -5), P(a, 9, -5));
                }
            b.Box(dead ? Roof : Cyan, center + Vector3.back * 5.2f, new Vector3(5, 7, .25f));
            if (dead) b.Beam(Iron, center + new Vector3(-7, -10, -5.4f), center + new Vector3(5, 11, -5.4f), 1.4f);
        }
    }
}
