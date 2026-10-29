using System.Collections.Generic;
using UnityEngine;

namespace WH30K.Sim.Resources
{
    public sealed partial class IndustrySystem
    {
        public const int LogisticsSegmentsPerFrame = 256;
        public const int LogisticsPairsPerFrame = 2048;
        public const int LogisticsSegmentLimit = 65536;
        public int LogisticsSegmentsLastUpdate { get; private set; }
        public int LogisticsPairsLastUpdate { get; private set; }
        public double LogisticsBuildMillisecondsLastUpdate { get; private set; }
        public bool LogisticsVisualTruncated { get; private set; }
        public bool LogisticsBuildPending => planet != null &&
            (logisticsVisualDirty || logisticsSurfaceRevision != planet.SurfaceRevision || logisticsBuilding);

        private readonly List<Vector3> logisticsVertices = new List<Vector3>();
        private readonly List<int> logisticsTriangles = new List<int>();
        private bool logisticsBuilding;
        private int logisticsBuildRevision, logisticsFrom, logisticsTo, logisticsSegment, logisticsSegments;
        private Vector3 logisticsA, logisticsB, logisticsPreviousNormal, logisticsPreviousPoint;

        private void CancelLogisticsBuild()
        {
            if (logisticsBuilding) logisticsVisualDirty = true;
            logisticsBuilding = false;
            logisticsVertices.Clear(); logisticsTriangles.Clear();
            logisticsSegments = logisticsSegment = 0;
            LogisticsSegmentsLastUpdate = LogisticsPairsLastUpdate = 0;
            LogisticsBuildMillisecondsLastUpdate = 0;
        }

        // Called once per rendered game frame, including while simulation is paused.
        // Only publish a complete graph; never expose a partly rebuilt/stale network.
        private void AdvanceLogisticsVisual()
        {
            if (visualRoot == null || !LogisticsBuildPending) return;
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            try { BuildLogisticsSlice(); }
            finally { LogisticsBuildMillisecondsLastUpdate = (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000d / System.Diagnostics.Stopwatch.Frequency; }
        }

        private void BuildLogisticsSlice()
        {
            if (logisticsVisualDirty || logisticsBuildRevision != planet.SurfaceRevision)
            {
                CancelLogisticsBuild();
                logisticsBuildRevision = planet.SurfaceRevision;
                logisticsFrom = 0; logisticsTo = 1;
                logisticsVisualDirty = false; logisticsBuilding = true;
                LogisticsVisualTruncated = false;
                if (logisticsRenderer != null) logisticsRenderer.enabled = false;
            }
            while (LogisticsSegmentsLastUpdate < LogisticsSegmentsPerFrame && LogisticsPairsLastUpdate < LogisticsPairsPerFrame)
            {
                if (logisticsSegment < logisticsSegments)
                {
                    var n = Vector3.Slerp(logisticsA, logisticsB, (logisticsSegment + 1f) / logisticsSegments).normalized;
                    var p = planet.transform.TransformPoint(planet.EvaluateSurfacePoint(n) + n * 4);
                    var normal = (logisticsPreviousNormal + n).normalized;
                    var side = Vector3.Cross(normal, (p - logisticsPreviousPoint).normalized).normalized * 1.5f;
                    var start = logisticsVertices.Count;
                    logisticsVertices.Add(visualRoot.InverseTransformPoint(logisticsPreviousPoint - side));
                    logisticsVertices.Add(visualRoot.InverseTransformPoint(logisticsPreviousPoint + side));
                    logisticsVertices.Add(visualRoot.InverseTransformPoint(p - side));
                    logisticsVertices.Add(visualRoot.InverseTransformPoint(p + side));
                    logisticsTriangles.Add(start); logisticsTriangles.Add(start + 2); logisticsTriangles.Add(start + 1);
                    logisticsTriangles.Add(start + 2); logisticsTriangles.Add(start + 3); logisticsTriangles.Add(start + 1);
                    logisticsPreviousNormal = n; logisticsPreviousPoint = p;
                    logisticsSegment++; LogisticsSegmentsLastUpdate++;
                    if (logisticsVertices.Count / 4 >= LogisticsSegmentLimit)
                    { LogisticsVisualTruncated = true; PublishLogisticsVisual(); return; }
                    continue;
                }
                if (logisticsFrom >= buildings.Length - 1) { PublishLogisticsVisual(); return; }
                if (logisticsTo >= buildings.Length) { logisticsFrom++; logisticsTo = logisticsFrom + 1; continue; }
                var a = buildings[logisticsFrom]; var b = buildings[logisticsTo++];
                LogisticsPairsLastUpdate++;
                if (!LogisticsNode(a.Id) || !LogisticsNode(b.Id)) continue;
                var range = a.Id == "StorageDepot" || b.Id == "StorageDepot" ? 1600f : 850f;
                if ((a.transform.position - b.transform.position).sqrMagnitude > range * range) continue;
                logisticsA = planet.transform.InverseTransformPoint(a.transform.position).normalized;
                logisticsB = planet.transform.InverseTransformPoint(b.transform.position).normalized;
                logisticsSegments = Mathf.Clamp(Mathf.CeilToInt(planet.SurfaceDistance(logisticsA, logisticsB) / 100f), 1, 32);
                logisticsSegment = 0;
                logisticsPreviousNormal = logisticsA;
                logisticsPreviousPoint = planet.transform.TransformPoint(planet.EvaluateSurfacePoint(logisticsA) + logisticsA * 4);
            }
        }

        private void PublishLogisticsVisual()
        {
            if (logisticsMesh == null)
            {
                var go = new GameObject("Industrial logistics links", typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(visualRoot, false);
                logisticsMesh = new Mesh { name = "Industrial logistics network", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                logisticsMesh.MarkDynamic(); go.GetComponent<MeshFilter>().sharedMesh = logisticsMesh;
                logisticsRenderer = go.GetComponent<MeshRenderer>(); logisticsRenderer.sharedMaterial = logisticsMaterial;
                logisticsRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                logisticsRenderer.receiveShadows = false;
            }
            logisticsMesh.Clear(); logisticsMesh.SetVertices(logisticsVertices); logisticsMesh.SetTriangles(logisticsTriangles, 0);
            logisticsMesh.RecalculateBounds();
            logisticsSurfaceRevision = logisticsBuildRevision; logisticsBuilding = false;
            logisticsVertices.Clear(); logisticsTriangles.Clear();
            logisticsRenderer.enabled = Overlay == 1;
        }
    }
}
