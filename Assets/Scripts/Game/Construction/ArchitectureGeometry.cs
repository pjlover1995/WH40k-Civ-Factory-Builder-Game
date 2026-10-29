using System;
using UnityEngine;

namespace WH30K.Gameplay.Construction
{
    /// <summary>Small physical chamfers catch light without changing occupied bounds.</summary>
    public static class ArchitectureGeometry
    {
        public static void BevelBox(int material, Vector3 center, Vector3 size, Quaternion rotation,
            float bevel, Action<int, Vector3, Vector3, Vector3, Vector3> quad)
        {
            var h = size * .5f;
            bevel = Mathf.Min(bevel, Mathf.Min(h.x, Mathf.Min(h.y, h.z)) * .25f);
            var inner = h - Vector3.one * bevel;
            void Face(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), a + b + c) < 0)
                { var swap = b; b = d; d = swap; }
                quad(material, center + rotation * a, center + rotation * b,
                    center + rotation * c, center + rotation * d);
            }
            Vector3 Point(int axis, float a, float b, float c)
            {
                var p = Vector3.zero;
                p[axis] = a; p[(axis + 1) % 3] = b; p[(axis + 2) % 3] = c;
                return p;
            }
            for (var axis = 0; axis < 3; axis++)
            {
                var next = (axis + 1) % 3; var last = (axis + 2) % 3;
                for (var sign = -1; sign <= 1; sign += 2)
                    Face(Point(axis, h[axis] * sign, -inner[next], -inner[last]),
                        Point(axis, h[axis] * sign, inner[next], -inner[last]),
                        Point(axis, h[axis] * sign, inner[next], inner[last]),
                        Point(axis, h[axis] * sign, -inner[next], inner[last]));
                for (var a = -1; a <= 1; a += 2)
                    for (var b = -1; b <= 1; b += 2)
                        Face(Point(axis, -inner[axis], h[next] * a, inner[last] * b),
                            Point(axis, inner[axis], h[next] * a, inner[last] * b),
                            Point(axis, inner[axis], inner[next] * a, h[last] * b),
                            Point(axis, -inner[axis], inner[next] * a, h[last] * b));
            }
            for (var x = -1; x <= 1; x += 2)
                for (var y = -1; y <= 1; y += 2)
                    for (var z = -1; z <= 1; z += 2)
                    {
                        var a = new Vector3(h.x * x, inner.y * y, inner.z * z);
                        var b = new Vector3(inner.x * x, h.y * y, inner.z * z);
                        var c = new Vector3(inner.x * x, inner.y * y, h.z * z);
                        // Split the triangular corner along an edge: both quad triangles have area.
                        Face(a, b, (b + c) * .5f, c);
                    }
        }
    }
}
