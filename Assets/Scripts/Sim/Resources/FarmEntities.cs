using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WH30K.Gameplay.Construction;

namespace WH30K.Sim.Resources
{
    public sealed partial class IndustrySystem
    {
        private int farmEntityRevision=-1;
        private readonly Dictionary<ManagedBuilding,float[]> farmInputPools=new Dictionary<ManagedBuilding,float[]>();
        private float[] FarmInputs(ManagedBuilding farm)
        {
            var root=farm.EntityRoot??farm;
            if(!farmInputPools.TryGetValue(root,out var pool)){pool=new float[IndustryRules.MaterialCount];farmInputPools[root]=pool;}
            Array.Clear(pool,0,pool.Length);
            foreach(var site in FarmSites(root))
            {var index=Array.IndexOf(buildings,site);if(index<0)continue;for(var k=0;k<pool.Length;k++)pool[k]+=stocks[index].quantities[k];}
            return pool;
        }
        private void SpendFarmInput(ManagedBuilding root,int kind,float amount)
        {
            var used=amount;
            foreach(var site in FarmSites(root))
            {
                var index=Array.IndexOf(buildings,site);if(index<0)continue;
                var debit=Math.Min(stocks[index].quantities[kind],amount);stocks[index].quantities[kind]-=debit;amount-=debit;
                if(amount<=.00001f)break;
            }
            RecordConsumption(kind,used-amount);
        }
        public IEnumerable<ManagedBuilding> FarmSites(ManagedBuilding farm)
        {
            var root=farm.EntityRoot??farm;
            var registry=placement!=null?placement.Buildings:buildings;
            foreach(var site in registry)if(site!=null && site.Id=="Farm" && (site.EntityRoot??site)==root)yield return site;
        }
        public void RefreshFarmEntities()
        {
            if(settlement==null || placement==null || farmEntityRevision==settlement.ResidentialGeometryRevision)return;
            var farms=placement.Buildings.Where(b=>b.Id=="Farm").ToArray();
            foreach(var farm in farms)
            {
                var root=farms.FirstOrDefault(b=>b.State.instanceId==farm.State.farm.entityId);
                if(root==null || !FacilityIdentityRules.CompatibleFarms(farm.State.farm,root.State.farm))root=farm;
                farm.SetEntityRoot(root);
            }
            var changed=false;
            for(var a=0;a<farms.Length;a++)for(var b=a+1;b<farms.Length;b++)
            {
                var first=farms[a];var second=farms[b];
                if(first.EntityRoot==second.EntityRoot || !FacilityIdentityRules.CompatibleFarms(first.State.farm,second.State.farm)
                    || Vector3.Distance(first.transform.position,second.transform.position)>first.FootprintRadius+second.FootprintRadius+2
                    || !settlement.FarmSitesTouch(first,second))continue;
                var root=Array.IndexOf(farms,first.EntityRoot)<Array.IndexOf(farms,second.EntityRoot)?first.EntityRoot:second.EntityRoot;
                var losing=root==first.EntityRoot?second.EntityRoot:first.EntityRoot;
                root.State.farm.productiveSeconds=Mathf.Min(root.State.farm.productiveSeconds,losing.State.farm.productiveSeconds);
                foreach(var site in farms)if(site.EntityRoot==root || site.EntityRoot==losing)
                { site.SetEntityRoot(root);site.ApplyFarmOperation(root.State.focus,root.State.offline);if(site!=root)site.State.farm.productiveSeconds=0; }
                changed=true;
            }
            farmEntityRevision=settlement.ResidentialGeometryRevision;
            if(changed)
            {
                settlement.RefreshReadout();
                var selection=GetComponent<BuildingSelection>();
                if(selection!=null && selection.Selected!=null && selection.Selected.Id=="Farm")selection.Select(selection.Selected.EntityRoot);
                GetComponent<WH30K.UI.NewGameMenu>()?.AppendEventLog("Touching farms of the same technology combined into one estate.");
            }
        }
        public bool ExpandFarmEntity(ManagedBuilding farm)
        {
            Synchronize();RefreshFarmEntities();var root=farm.EntityRoot??farm;
            var target=NextFarmExpansion(root,true);
            if(target==null)return false;
            target.State.farm.addedFields++;root.State.farm.productiveSeconds=0;
            target.CommitFarmParcel();target.FarmChanged();return true;
        }
        private ManagedBuilding NextFarmExpansion(ManagedBuilding root,bool development=false)
        {
            foreach(var site in FarmSites(root))if(site.State.farm.addedFields<FarmRules.MaxAddedFields
                && CanFarmLayout(site.transform,site.State.farm.addedFields+1,site,development))return site;
            return null;
        }
        public float FarmStoredFood(ManagedBuilding farm)
        {
            var sum=0f;foreach(var site in FarmSites(farm)){var index=Array.IndexOf(buildings,site);if(index>=0)sum+=stocks[index].food;}
            return sum;
        }
    }
}
