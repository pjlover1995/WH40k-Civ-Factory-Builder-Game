using System;
using System.Collections.Generic;
using UnityEngine;
using WH30K.Planet;
using WH30K.Sim;

namespace WH30K.Gameplay.Construction
{
    public static partial class BuildingVisuals
    {
        private static Material[] farmMaterials;
        public static Material[] FarmMaterials
        {
            get
            {
                if (farmMaterials != null) return farmMaterials;
                farmMaterials = (Material[])ArchitectureMaterials.Clone();
                farmMaterials[Dark] = new Material(farmMaterials[Stone]) { name = "Farm / photographic soil", color = new Color(.22f, .12f, .055f) };
                farmMaterials[Cyan] = new Material(farmMaterials[Stone]) { name = "Farm / planted crop rows", color = new Color(.27f, .36f, .07f) };
                farmMaterials[Dark].SetFloat("_TintStrength", 1);
                farmMaterials[Cyan].SetFloat("_TintStrength", 1);
                // Ground overlays need a small render bias against the planet's coarser distant LOD triangles.
                foreach (var group in new[] { Dark, Cyan })
                { farmMaterials[group].SetFloat("_DepthFactor", -2); farmMaterials[group].SetFloat("_DepthUnits", -2); }
                return farmMaterials;
            }
        }
        public static Mesh FarmMesh(int added, Func<float, float, float> ground = null, int layoutSeed = 0, FarmState state = null)
        {
            var b = new Architecture();
            var heights = ground == null ? null : new Dictionary<(float, float), float>();
            float Y(float x, float z)
            {
                if (ground == null) return 0;
                if (!heights.TryGetValue((x,z),out var value)) { value=ground(x*.01f,z*.01f)*166; heights[(x,z)]=value; }
                return value;
            }
            for (var barn = 0; barn < FarmRules.Buildings(added); barn++)
            {
                FarmRules.BuildingPosition(barn, out var cx, out var cz); var x = cx * 100; var z = cz * 100;
                FarmBuilding(b, barn, x, z, Y(x,z), layoutSeed);
            }
            float LengthSquared(FarmPoint a, FarmPoint c) => (a.x-c.x)*(a.x-c.x)+(a.z-c.z)*(a.z-c.z);
            Vector3 Point(FarmPoint p, float lift) => new Vector3(p.x * 100, Y(p.x * 100, p.z * 100) + lift, p.z * 100);
            void Triangle(FarmPoint a, FarmPoint c, FarmPoint d, int material, float lift, int depth)
            {
                var ac = LengthSquared(a,c); var cd = LengthSquared(c,d); var da = LengthSquared(d,a);
                if (depth < 10 && Mathf.Max(ac, Mathf.Max(cd,da)) > .055f * .055f)
                {
                    if (ac >= cd && ac >= da)
                    { var m = FarmPoint.Lerp(a,c,.5f); Triangle(a,m,d,material,lift,depth+1); Triangle(m,c,d,material,lift,depth+1); }
                    else if (cd >= da)
                    { var m = FarmPoint.Lerp(c,d,.5f); Triangle(a,c,m,material,lift,depth+1); Triangle(a,m,d,material,lift,depth+1); }
                    else
                    { var m = FarmPoint.Lerp(d,a,.5f); Triangle(a,c,m,material,lift,depth+1); Triangle(m,c,d,material,lift,depth+1); }
                    return;
                }
                if (Mathf.Abs((c.x-a.x)*(d.z-a.z)-(c.z-a.z)*(d.x-a.x)) > 1e-10f)
                    b.Triangle(material, Point(a,lift), Point(c,lift), Point(d,lift));
            }
            void Surface(FarmPoint[] polygon, int material, float lift)
            {
                if (polygon.Length < 3) return;
                var center = FarmLayoutRules.Center(polygon);
                for (var i = 0; i < polygon.Length; i++) Triangle(center,polygon[i],polygon[(i+1)%polygon.Length],material,lift,0);
            }
            for (var field = 0; field < FarmRules.Fields(added); field++)
            {
                var parcel = FarmRules.Parcel(state,layoutSeed,field);
                Surface(parcel, Dark, .3f);
                // Soil parcels meet exactly. The narrow unplanted headland follows their shared irregular edges.
                var maturity = Mathf.Clamp01((FarmRules.Fields(added) - field/10*10 - 5) / 5f);
                var planted = FarmLayoutRules.Inset(parcel, Mathf.Lerp(.012f, .006f, maturity));
                var angle = FarmLayoutRules.RowAngle(layoutSeed, field);
                var nx = Mathf.Cos(angle); var nz = Mathf.Sin(angle);
                var min = float.MaxValue; var max = float.MinValue;
                foreach (var point in planted) { var t = point.x*nx+point.z*nz; min=Mathf.Min(min,t); max=Mathf.Max(max,t); }
                for (var row = min + .008f; row < max; row += .022f)
                {
                    var strip = FarmLayoutRules.Clip(planted,nx,nz,row+.0045f);
                    strip = FarmLayoutRules.Clip(strip,-nx,-nz,-row+.0045f);
                    Surface(strip,Cyan,1.6f);
                }
            }
            return b.Build("Standard farm / " + FarmRules.Fields(added) + " fields");
        }
    }
    public sealed class FarmVisual : MonoBehaviour
    {
        private ManagedBuilding owner;
        private LODPlanet planet;
        private Mesh ownedMesh;
        private int revision = -1;
        public void Configure(ManagedBuilding farm) { owner = farm; planet = GetComponentInParent<LODPlanet>(); Rebuild(); }
        private void Update() { if (planet != null && revision != planet.SurfaceRevision) Rebuild(); }
        public void Rebuild()
        {
            if (owner == null) return;
            float Ground(float x, float z)
            {
                if (planet == null) return 0;
                var direction = planet.transform.InverseTransformPoint(transform.TransformPoint(new Vector3(x, 0, z))).normalized;
                return transform.InverseTransformPoint(planet.transform.TransformPoint(planet.EvaluateSurfacePoint(direction))).y;
            }
            var old = ownedMesh; ownedMesh = BuildingVisuals.FarmMesh(owner.State.farm.addedFields, Ground, owner.FarmLayoutSeed, owner.State.farm);
            GetComponent<MeshFilter>().sharedMesh = ownedMesh;
            var collider = GetComponent<BoxCollider>(); collider.center = ownedMesh.bounds.center; collider.size = ownedMesh.bounds.size;
            if (old != null) Destroy(old);
            revision = planet != null ? planet.SurfaceRevision : -1;
        }
        private void OnDestroy() { if (ownedMesh != null) Destroy(ownedMesh); }
    }
}
