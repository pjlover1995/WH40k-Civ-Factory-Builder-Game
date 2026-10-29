using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WH30K.Gameplay.Construction;

namespace WH30K.Sim.Settlements
{
    public partial class Settlement
    {
        private sealed class LandClaim { public ManagedBuilding owner; public FarmPoint[] polygon; public Vector3 center; public float radius; public bool pending; public int order; }
        private readonly List<LandClaim> landClaims=new List<LandClaim>();
        private int landRevision=-1;
        private IEnumerable<ManagedBuilding> IdentityBuildings()
        {
            foreach(var b in coreBuildings)if(b!=null)yield return b;
            var placement=GetComponent<BuildingPlacementController>();
            if(placement!=null)foreach(var b in placement.Buildings)if(b!=null)yield return b;
        }
        public void EnsureIdentity(ManagedBuilding building)
        {
            var name=building.State.instanceName;
            if(string.IsNullOrEmpty(name))name=FacilityIdentityRules.NextName(building.Id=="Farm"?"Farm":building.DefinitionTitle,
                IdentityBuildings().Where(b=>b!=building && b.Id==building.Id).Select(b=>b.State.instanceName));
            building.SetIdentity(string.IsNullOrEmpty(building.State.instanceId)?Guid.NewGuid().ToString("N"):building.State.instanceId,name);
        }
        public bool Rename(ManagedBuilding building,string name)
        {
            building=building.EntityRoot;name=(name??"").Trim();
            if(string.IsNullOrEmpty(name) || !FacilityIdentityRules.ValidName(name)
                || IdentityBuildings().Any(b=>b!=building && b.Id==building.Id && string.Equals(b.State.instanceName,name,StringComparison.OrdinalIgnoreCase)))return false;
            building.SetIdentity(building.State.instanceId,name);return true;
        }
        public static FarmPoint[] LandRect(Bounds bounds) => new[]{new FarmPoint(bounds.min.x,bounds.min.z),new FarmPoint(bounds.min.x,bounds.max.z),
            new FarmPoint(bounds.max.x,bounds.max.z),new FarmPoint(bounds.max.x,bounds.min.z)};
        public IEnumerable<FarmPoint[]> FarmLand(ManagedBuilding farm)
        {
            var state=farm.State.farm;
            for(var field=0;field<FarmRules.Fields(state.addedFields);field++)yield return FarmRules.Parcel(state,farm.FarmLayoutSeed,field);
            for(var site=0;site<FarmRules.Buildings(FarmRules.MaxAddedFields);site++)
            {
                var patch=FarmRules.BuildingPatch(site);
                if(patch*10>=FarmRules.Fields(state.addedFields))continue;
                FarmRules.BuildingPosition(site,out var x,out var z);
                yield return LandRect(new Bounds(new Vector3(x,0,z),new Vector3(.46f,1,.30f)));
            }
        }
        private static FarmPoint[] ProjectLand(Transform basis,Transform source,FarmPoint[] points)
        {
            var result=new FarmPoint[points.Length];var delta=source.position-basis.position;
            // Keep polygon vertices relative to their facility. Adding metre-sized edges
            // to a multi-million-metre world position and subtracting it again rounds
            // adjoining fields into overlapping claims and can permanently halt growth.
            for(var i=0;i<points.Length;i++)
            {
                if(basis==source){result[i]=new FarmPoint(points[i].x*source.lossyScale.x,points[i].z*source.lossyScale.z);continue;}
                var d=delta+source.TransformVector(new Vector3(points[i].x,0,points[i].z));
                result[i]=new FarmPoint(Vector3.Dot(d,basis.right),Vector3.Dot(d,basis.forward));
            }
            return result;
        }
        private void EnsureLand()
        {
            if(landRevision==ResidentialGeometryRevision)return;
            landRevision=ResidentialGeometryRevision;landClaims.Clear();var order=0;
            foreach(var owner in AllBuildings)
            {
                void Add(FarmPoint[] polygon,bool pending=false)
                {
                    var localCenter=Vector3.zero;foreach(var p in polygon)localCenter+=new Vector3(p.x,0,p.z);localCenter/=polygon.Length;
                    var center=owner.transform.TransformPoint(localCenter);var radius=0f;
                    foreach(var p in polygon)radius=Mathf.Max(radius,owner.transform.TransformVector(new Vector3(p.x,0,p.z)-localCenter).magnitude);
                    landClaims.Add(new LandClaim{owner=owner,polygon=polygon,center=center,radius=radius,pending=pending,order=order});
                }
                if(owner.Id=="Farm")foreach(var polygon in FarmLand(owner))Add(polygon);
                else if(owner.Industrial!=null)
                {
                    Add(LandRect(new Bounds(new Vector3(0,.5f,0),Vector3.one)));
                    foreach(var bay in owner.State.manufactorum.bays)Add(LandRect(ManufactorumDevelopment.BayBounds(bay)));
                    if(owner.State.manufactorum.project!=null)Add(LandRect(ManufactorumDevelopment.BayBounds(owner.State.manufactorum.project)),true);
                }
                else if(owner.Residential!=null && owner.State.residential!=null)
                {
                    foreach(var module in owner.State.residential.modules)foreach(var cell in ResidentialRules.Cells(module,ResidentialDevelopment.Library))
                        Add(LandRect(ResidentialDevelopment.CellBounds(cell)));
                    if(owner.State.residential.project!=null)foreach(var cell in ResidentialRules.Cells(owner.State.residential.project.placement,ResidentialDevelopment.Library))
                        Add(LandRect(ResidentialDevelopment.CellBounds(cell)),true);
                }
                else
                {
                    Add(LandRect(owner.GrowthBounds(false)));
                    if(owner.State.improving)Add(LandRect(owner.GrowthBounds(true)),true);
                }
                order++;
            }
        }
        private bool VehicleClear(Transform basis,FarmPoint[] candidate)
        {
            var vehicle=GetComponent<EarthworksController>()?.Vehicle;if(vehicle==null)return true;
            var radius=EarthworksRules.VehicleRadius;
            return !TerritoryRules.Overlaps(candidate,ProjectLand(basis,vehicle,LandRect(new Bounds(Vector3.zero,new Vector3(radius*2,1,radius*2)))));
        }
        public bool TerritoryClear(Transform basis,FarmPoint[] polygon,ManagedBuilding owner=null,bool earlierReservations=false)
        {
            EnsureLand();var candidate=ProjectLand(basis,basis,polygon);
            if(!VehicleClear(basis,candidate))return false;
            var order=owner==null?int.MaxValue:landClaims.FindIndex(c=>c.owner==owner);
            var ownerOrder=owner==null || order<0?int.MaxValue:landClaims[order].order;
            foreach(var claim in landClaims)
            {
                if(claim.owner==owner || earlierReservations && claim.pending && claim.order>ownerOrder || !NearClaim(basis,candidate,claim))continue;
                if(TerritoryRules.Overlaps(candidate,ProjectLand(basis,claim.owner.transform,claim.polygon)))return false;
            }
            return true;
        }
        public bool PlanFarmParcel(ManagedBuilding farm,FarmPoint[] original,out FarmPoint[] result)
        {
            EnsureLand();var basis=farm.transform;var candidate=ProjectLand(basis,basis,original);var area=FarmLayoutRules.Area(candidate);
            foreach(var claim in landClaims)
            {
                if(!NearClaim(basis,candidate,claim))continue;
                var occupied=ProjectLand(basis,claim.owner.transform,claim.polygon);
                if(!TerritoryRules.Overlaps(candidate,occupied))continue;
                if(claim.owner.Id!="Farm" || !FacilityIdentityRules.CompatibleFarms(farm.State.farm,claim.owner.State.farm)) {result=null;return false;}
                candidate=TerritoryRules.SubtractLargest(candidate,TerritoryRules.Expand(occupied,.02f));
                if(FarmLayoutRules.Area(candidate)<area*.5f){result=null;return false;}
            }
            if(!VehicleClear(basis,candidate)){result=null;return false;}
            result=new FarmPoint[candidate.Length];for(var i=0;i<result.Length;i++)result[i]=new FarmPoint(candidate[i].x/basis.lossyScale.x,candidate[i].z/basis.lossyScale.z);
            return true;
        }
        public bool FarmSitesTouch(ManagedBuilding a,ManagedBuilding b)
        {
            if(!FacilityIdentityRules.CompatibleFarms(a.State.farm,b.State.farm))return false;
            EnsureLand();
            foreach(var ac in landClaims)if(ac.owner==a)foreach(var bc in landClaims)if(bc.owner==b
                && TerritoryRules.Touches(ProjectLand(a.transform,ac.owner.transform,ac.polygon),ProjectLand(a.transform,bc.owner.transform,bc.polygon)))return true;
            return false;
        }
        private static bool NearClaim(Transform basis,FarmPoint[] polygon,LandClaim claim)
        {
            var center=FarmLayoutRules.Center(polygon);var radius=0f;
            foreach(var p in polygon)radius=Mathf.Max(radius,Mathf.Sqrt((p.x-center.x)*(p.x-center.x)+(p.z-center.z)*(p.z-center.z)));
            var delta=claim.center-basis.position;var x=Vector3.Dot(delta,basis.right)-center.x;var z=Vector3.Dot(delta,basis.forward)-center.z;
            return x*x+z*z<=(radius+claim.radius+1)*(radius+claim.radius+1);
        }
    }
}
