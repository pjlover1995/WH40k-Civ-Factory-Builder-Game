using UnityEngine;

namespace WH30K.Gameplay.Construction
{
    public sealed class ResidentialOwnedMesh : MonoBehaviour
    {
        private void OnDestroy()
        {
            var filter=GetComponent<MeshFilter>();
            if(filter!=null && filter.sharedMesh!=null)Destroy(filter.sharedMesh);
        }
    }
}
