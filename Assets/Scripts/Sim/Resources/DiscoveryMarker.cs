using UnityEngine;
using UnityEngine.EventSystems;

namespace WH30K.Sim.Resources
{
    public sealed class DiscoveryMarker : MonoBehaviour
    {
        private IndustrySystem industry;
        private int id;
        private bool deposit;

        public void Configure(IndustrySystem owner, int itemId, bool isDeposit)
        { industry = owner; id = itemId; deposit = isDeposit; }

        public void Inspect()
        {
            if(industry==null||!industry.CanInspectLocations)return;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
            if (deposit) industry?.SelectDeposit(id);
            else industry?.SelectDiscovery(id);
        }
    }
}
