using System.Collections.Generic;
using UnityEngine;

namespace WH30K.Gameplay.Construction
{
    /// <summary>
    /// Helper that manages the visual overlay for the placement grid.
    /// It builds a small cluster of quads around the active placement cell.
    /// </summary>
    public sealed class PlanetGridVisualizer
    {
        private readonly Transform root;
        private readonly Material allowedMaterial;
        private readonly Material blockedMaterial;
        private readonly float elevationOffset;
        private readonly List<GridTile> tiles = new List<GridTile>();

        private struct GridTile
        {
            public readonly GameObject GameObject;
            public readonly MeshRenderer Renderer;

            public GridTile(GameObject go, MeshRenderer renderer)
            {
                GameObject = go;
                Renderer = renderer;
            }
        }

        public PlanetGridVisualizer(Transform parent, Material allowedMaterial, Material blockedMaterial, float elevationOffset)
        {
            this.allowedMaterial = allowedMaterial;
            this.blockedMaterial = blockedMaterial;
            this.elevationOffset = elevationOffset;

            var rootGO = new GameObject("PlacementGridOverlay");
            root = rootGO.transform;
            root.SetParent(parent, false);
            root.gameObject.SetActive(false);
        }

        public void Hide()
        {
            if (root != null)
            {
                root.gameObject.SetActive(false);
            }
        }

        public void Dispose()
        {
            if (root != null)
            {
                Object.Destroy(root.gameObject);
            }

            tiles.Clear();
        }

        public void ShowArea(PlanetGrid grid, PlanetGridCell centerCell, bool placementValid)
        {
            if (root == null)
            {
                return;
            }

            var frames = GatherFrames(grid, centerCell);
            EnsureTileCount(frames.Count);

            for (var i = 0; i < frames.Count; i++)
            {
                var frame = frames[i];
                var tile = tiles[i];
                UpdateTile(tile, frame, placementValid);
            }

            for (var i = frames.Count; i < tiles.Count; i++)
            {
                tiles[i].GameObject.SetActive(false);
            }

            root.gameObject.SetActive(true);
        }

        private void UpdateTile(GridTile tile, CellFrame frame, bool placementValid)
        {
            var go = tile.GameObject;
            go.SetActive(true);

            var position = frame.Center + frame.Normal * elevationOffset;
            var forward = frame.Forward.sqrMagnitude > 0.001f
                ? frame.Forward
                : Vector3.forward;
            var rotation = Quaternion.LookRotation(forward, frame.Normal);
            go.transform.SetPositionAndRotation(position, rotation);
            go.transform.localScale = new Vector3(Mathf.Max(1f, frame.RightExtent), Mathf.Max(1f, frame.ForwardExtent), 1f);

            var renderer = tile.Renderer;
            renderer.sharedMaterial = placementValid ? allowedMaterial : blockedMaterial;
        }

        private List<CellFrame> GatherFrames(PlanetGrid grid, PlanetGridCell centerCell)
        {
            var frames = new List<CellFrame>();
            var visited = new HashSet<PlanetGridCell>();
            for (var y = -1; y <= 1; y++)
            {
                for (var x = -1; x <= 1; x++)
                {
                    var target = grid.OffsetCell(centerCell, x, y);
                    if (!visited.Add(target))
                    {
                        continue;
                    }

                    frames.Add(grid.CalculateFrame(target));
                }
            }

            return frames;
        }

        private void EnsureTileCount(int count)
        {
            var shader = Shader.Find("Unlit/Color");
            while (tiles.Count < count)
            {
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = $"GridTile_{tiles.Count:00}";
                quad.transform.SetParent(root, false);
                var collider = quad.GetComponent<Collider>();
                if (collider != null)
                {
                    Object.Destroy(collider);
                }

                var renderer = quad.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = allowedMaterial != null
                    ? allowedMaterial
                    : new Material(shader) { color = new Color(0.5f, 1f, 0.5f, 0.3f) };

                tiles.Add(new GridTile(quad, renderer));
            }
        }
    }
}
