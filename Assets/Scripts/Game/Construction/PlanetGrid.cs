using System;
using System.Collections.Generic;
using UnityEngine;
using WH30K.Planet;

namespace WH30K.Gameplay.Construction
{
    /// <summary>
    /// Represents a cube-sphere aligned grid that wraps the entire planet surface.
    /// Provides conversion helpers between world positions and discrete grid cells.
    /// </summary>
    public sealed class PlanetGrid
    {
        private readonly LODPlanet planet;
        private readonly Transform planetTransform;
        private readonly int cellsPerFace;
        private readonly float inverseResolution;

        public int CellsPerFace => cellsPerFace;

        public PlanetGrid(LODPlanet planet, int cellsPerFace)
        {
            this.planet = planet;
            planetTransform = planet.transform;
            this.cellsPerFace = Mathf.Max(4, cellsPerFace);
            inverseResolution = 1f / this.cellsPerFace;
        }

        public PlanetGridCell GetCellFromWorld(Vector3 worldPosition)
        {
            var local = planetTransform.InverseTransformPoint(worldPosition);
            return GetCellFromDirection(local.normalized);
        }

        public PlanetGridCell GetCellFromDirection(Vector3 direction)
        {
            direction.Normalize();

            var absX = Mathf.Abs(direction.x);
            var absY = Mathf.Abs(direction.y);
            var absZ = Mathf.Abs(direction.z);

            int face;
            float u;
            float v;

            if (absX >= absY && absX >= absZ)
            {
                if (direction.x >= 0f)
                {
                    face = (int)CubeFace.PositiveX;
                    u = -direction.z / absX;
                    v = direction.y / absX;
                }
                else
                {
                    face = (int)CubeFace.NegativeX;
                    u = direction.z / absX;
                    v = direction.y / absX;
                }
            }
            else if (absY >= absX && absY >= absZ)
            {
                if (direction.y >= 0f)
                {
                    face = (int)CubeFace.PositiveY;
                    u = direction.x / absY;
                    v = -direction.z / absY;
                }
                else
                {
                    face = (int)CubeFace.NegativeY;
                    u = direction.x / absY;
                    v = direction.z / absY;
                }
            }
            else
            {
                if (direction.z >= 0f)
                {
                    face = (int)CubeFace.PositiveZ;
                    u = direction.x / absZ;
                    v = direction.y / absZ;
                }
                else
                {
                    face = (int)CubeFace.NegativeZ;
                    u = -direction.x / absZ;
                    v = direction.y / absZ;
                }
            }

            var uNorm = (u + 1f) * 0.5f;
            var vNorm = (v + 1f) * 0.5f;

            var x = Mathf.Clamp(Mathf.FloorToInt(uNorm * cellsPerFace), 0, cellsPerFace - 1);
            var y = Mathf.Clamp(Mathf.FloorToInt(vNorm * cellsPerFace), 0, cellsPerFace - 1);
            return new PlanetGridCell(face, x, y);
        }

        public Vector3 GetCellCenterWorld(PlanetGridCell cell)
        {
            var local = GetCellCenterLocal(cell);
            return planetTransform.TransformPoint(local);
        }

        public Vector3 GetCellCenterLocal(PlanetGridCell cell)
        {
            var direction = GetDirection(cell);
            return planet.EvaluateSurfacePoint(direction);
        }

        public Vector3 GetDirection(PlanetGridCell cell)
        {
            var uv = GetCellUV(cell);
            return CubeToDirection(cell.Face, uv);
        }

        public Vector2 GetCellUV(PlanetGridCell cell)
        {
            return new Vector2((cell.X + 0.5f) * inverseResolution, (cell.Y + 0.5f) * inverseResolution);
        }

        public PlanetGridCell OffsetCell(PlanetGridCell cell, int offsetX, int offsetY)
        {
            var uv = GetCellUV(cell);
            uv += new Vector2(offsetX * inverseResolution, offsetY * inverseResolution);
            var direction = CubeToDirection(cell.Face, uv);
            return GetCellFromDirection(direction);
        }

        public CellFrame CalculateFrame(PlanetGridCell cell)
        {
            var uv = GetCellUV(cell);
            var centerLocal = planet.EvaluateSurfacePoint(CubeToDirection(cell.Face, uv));
            var centerWorld = planetTransform.TransformPoint(centerLocal);
            var planetCenter = planetTransform.position;
            var normalWorld = (centerWorld - planetCenter).sqrMagnitude > 0.001f
                ? (centerWorld - planetCenter).normalized
                : planetTransform.up;

            var delta = inverseResolution;
            var rightLocal = planet.EvaluateSurfacePoint(CubeToDirection(cell.Face, uv + new Vector2(delta, 0f)));
            var leftLocal = planet.EvaluateSurfacePoint(CubeToDirection(cell.Face, uv - new Vector2(delta, 0f)));
            var forwardLocal = planet.EvaluateSurfacePoint(CubeToDirection(cell.Face, uv + new Vector2(0f, delta)));
            var backLocal = planet.EvaluateSurfacePoint(CubeToDirection(cell.Face, uv - new Vector2(0f, delta)));

            var rightWorld = planetTransform.TransformPoint(rightLocal);
            var leftWorld = planetTransform.TransformPoint(leftLocal);
            var forwardWorld = planetTransform.TransformPoint(forwardLocal);
            var backWorld = planetTransform.TransformPoint(backLocal);

            var rightVector = (rightWorld - leftWorld) * 0.5f;
            var forwardVector = (forwardWorld - backWorld) * 0.5f;

            var rightDir = rightVector.sqrMagnitude > 0.001f ? rightVector.normalized : Vector3.right;
            var forwardDir = forwardVector.sqrMagnitude > 0.001f ? forwardVector.normalized : Vector3.forward;

            return new CellFrame(cell, centerWorld, normalWorld, rightDir, forwardDir,
                rightVector.magnitude, forwardVector.magnitude);
        }

        private static Vector3 CubeToDirection(int face, Vector2 uv)
        {
            var u = Mathf.Clamp((uv.x * 2f) - 1f, -2f, 2f);
            var v = Mathf.Clamp((uv.y * 2f) - 1f, -2f, 2f);

            switch ((CubeFace)face)
            {
                case CubeFace.PositiveX:
                    return new Vector3(1f, v, -u).normalized;
                case CubeFace.NegativeX:
                    return new Vector3(-1f, v, u).normalized;
                case CubeFace.PositiveY:
                    return new Vector3(u, 1f, -v).normalized;
                case CubeFace.NegativeY:
                    return new Vector3(u, -1f, v).normalized;
                case CubeFace.PositiveZ:
                    return new Vector3(u, v, 1f).normalized;
                case CubeFace.NegativeZ:
                    return new Vector3(-u, v, -1f).normalized;
                default:
                    return Vector3.up;
            }
        }

        private enum CubeFace
        {
            PositiveX = 0,
            NegativeX = 1,
            PositiveY = 2,
            NegativeY = 3,
            PositiveZ = 4,
            NegativeZ = 5
        }
    }

    public readonly struct PlanetGridCell : IEquatable<PlanetGridCell>
    {
        public int Face { get; }
        public int X { get; }
        public int Y { get; }

        public PlanetGridCell(int face, int x, int y)
        {
            Face = Mathf.Clamp(face, 0, 5);
            X = x;
            Y = y;
        }

        public bool Equals(PlanetGridCell other)
        {
            return Face == other.Face && X == other.X && Y == other.Y;
        }

        public override bool Equals(object obj)
        {
            return obj is PlanetGridCell other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = Face;
                hash = (hash * 397) ^ X;
                hash = (hash * 397) ^ Y;
                return hash;
            }
        }

        public override string ToString()
        {
            return $"Face {Face} ({X}, {Y})";
        }
    }

    public readonly struct CellFrame
    {
        public PlanetGridCell Cell { get; }
        public Vector3 Center { get; }
        public Vector3 Normal { get; }
        public Vector3 Right { get; }
        public Vector3 Forward { get; }
        public float RightExtent { get; }
        public float ForwardExtent { get; }

        public CellFrame(PlanetGridCell cell, Vector3 center, Vector3 normal, Vector3 right, Vector3 forward,
            float rightExtent, float forwardExtent)
        {
            Cell = cell;
            Center = center;
            Normal = normal;
            Right = right;
            Forward = forward;
            RightExtent = rightExtent;
            ForwardExtent = forwardExtent;
        }
    }
}
