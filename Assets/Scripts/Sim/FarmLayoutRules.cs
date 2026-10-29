using System;
using System.Collections.Generic;

namespace WH30K.Sim
{
    [Serializable]
    public struct FarmPoint
    {
        public float x, z;
        public FarmPoint(float x, float z) { this.x = x; this.z = z; }
        public static FarmPoint Lerp(FarmPoint a, FarmPoint b, float t)
            => new FarmPoint(a.x + (b.x - a.x) * t, a.z + (b.z - a.z) * t);
    }

    /// <summary>Seeded, edge-sharing parcels. Geometry is independent of growth timing and Unity.</summary>
    public static class FarmLayoutRules
    {
        private static readonly Dictionary<(int, int), FarmPoint[][]> cache = new Dictionary<(int, int), FarmPoint[][]>();
        public static int Seed(int planet, float x, float y, float z)
        {
            var value = unchecked(planet * 73856093 ^ (int)Math.Round(x * 4096) * 19349663
                ^ (int)Math.Round(y * 4096) * 83492791 ^ (int)Math.Round(z * 4096) * 92821);
            return value == 0 ? 1 : value;
        }
        public static FarmPoint[] Parcel(int seed, int field)
        {
            var cluster = field / 10;
            if (!cache.TryGetValue((seed, cluster), out var parcels))
            {
                if (cache.Count >= 128) cache.Clear();
                parcels = Build(seed, cluster); cache[(seed, cluster)] = parcels;
            }
            return parcels[field % 10]; // Immutable by convention; clipping returns a new polygon.
        }
        private static FarmPoint[][] Build(int seed, int cluster)
        {
            FarmRules.ClusterPosition(cluster, out var cx, out var cz);
            var random = new Random(unchecked(seed ^ cluster * 92821));
            var hasYard = FarmRules.HasBuildingYard(cluster);
            var sites = new FarmPoint[hasYard ? 11 : 10]; if (hasYard) sites[10] = new FarmPoint(cx, cz);
            // Field patches are independent of support buildings; most patches have no empty courtyard.
            var previous = FarmRules.PreviousPatch(cluster);
            FarmRules.ClusterPosition(Math.Max(0, previous), out var px, out var pz);
            var start = previous < 0 || px < cx - .5f ? Math.PI : -Math.PI * .5;
            for (var i = 0; i < 10; i++)
            {
                var angle = start + i * Math.PI / 5 + (random.NextDouble() - .5) * .34;
                var radius = .37 + random.NextDouble() * .09;
                sites[i] = new FarmPoint(cx + (float)(Math.Cos(angle) * radius), cz + (float)(Math.Sin(angle) * radius));
            }
            var result = new FarmPoint[10][];
            for (var i = 0; i < 10; i++)
            {
                // The neighbouring cluster uses this exact same edge; there is no scattered-plot spacing.
                var polygon = new[] { new FarmPoint(cx - .49f, cz - .49f), new FarmPoint(cx - .49f, cz + .49f),
                    new FarmPoint(cx + .49f, cz + .49f), new FarmPoint(cx + .49f, cz - .49f) };
                for (var other = 0; other < sites.Length; other++)
                {
                    if (other == i) continue;
                    var a = sites[i]; var b = sites[other];
                    var limit = (b.x * b.x + b.z * b.z - a.x * a.x - a.z * a.z - (other == 10 ? .07f : 0)) * .5f;
                    polygon = Clip(polygon, b.x - a.x, b.z - a.z, limit);
                }
                // Free outer edges follow individual oblique boundaries. Edges against future clusters
                // stay shared, so the next barn can extend the patchwork without moving finished plots.
                var edge = new Random(unchecked(seed ^ cluster * 83492791 ^ i * 73856093));
                var horizontal = cx < .1f ? -1f : 1f;
                var vertical = cz < .1f ? -1f : 1f;
                var tiltX = (float)(edge.NextDouble() - .5) * .30f;
                var tiltZ = (float)(edge.NextDouble() - .5) * .30f;
                var xLimit = .415f + (float)edge.NextDouble() * .06f;
                var zLimit = .415f + (float)edge.NextDouble() * .06f;
                if (cx < .1f || cx > 2.8f) polygon = Clip(polygon, horizontal, tiltX, horizontal * cx + tiltX * cz + xLimit);
                if (cz < .1f || cz > 2.8f) polygon = Clip(polygon, tiltZ, vertical, tiltZ * cx + vertical * cz + zLimit);
                result[i] = polygon;
            }
            return result;
        }
        public static FarmPoint[] Clip(FarmPoint[] polygon, float nx, float nz, float limit)
        {
            var output = new List<FarmPoint>(polygon.Length + 1);
            if (polygon.Length == 0) return output.ToArray();
            var previous = polygon[polygon.Length - 1]; var before = previous.x * nx + previous.z * nz - limit;
            foreach (var point in polygon)
            {
                var after = point.x * nx + point.z * nz - limit;
                if ((before <= 0) != (after <= 0)) output.Add(FarmPoint.Lerp(previous, point, before / (before - after)));
                if (after <= 0) output.Add(point);
                previous = point; before = after;
            }
            return output.ToArray();
        }
        public static FarmPoint Center(FarmPoint[] polygon)
        {
            float x = 0, z = 0; foreach (var point in polygon) { x += point.x; z += point.z; }
            return new FarmPoint(x / polygon.Length, z / polygon.Length);
        }
        public static float Area(FarmPoint[] polygon)
        {
            var sum = 0f;
            for (var i = 0; i < polygon.Length; i++)
            { var a = polygon[i]; var b = polygon[(i + 1) % polygon.Length]; sum += a.x * b.z - b.x * a.z; }
            return Math.Abs(sum) * .5f;
        }
        public static FarmPoint[] Inset(FarmPoint[] polygon, float margin)
        {
            var result = polygon;
            for (var i = 0; i < polygon.Length; i++)
            {
                var a = polygon[i]; var b = polygon[(i + 1) % polygon.Length];
                var nx = a.z - b.z; var nz = b.x - a.x; // Outward normal for clockwise parcel winding.
                var length = (float)Math.Sqrt(nx * nx + nz * nz);
                result = Clip(result, nx, nz, nx * a.x + nz * a.z - margin * length);
            }
            return result;
        }
        public static float RowAngle(int seed, int field)
            => (float)(new Random(unchecked(seed ^ field * 73856093 ^ 0x1731)).NextDouble() * Math.PI);
    }
}
