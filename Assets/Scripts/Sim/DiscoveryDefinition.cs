using UnityEngine;

namespace WH30K.Sim
{
    [CreateAssetMenu(menuName="WH30K/Exploration/Discovery",fileName="DiscoveryDefinition")]
    public sealed class DiscoveryDefinition : ScriptableObject
    {
        [SerializeField] private DiscoveryKind kind;
        [SerializeField] private string displayName;
        [SerializeField,TextArea(2,5)] private string description;
        [SerializeField] private string cautiousAction="CAUTIOUS SEARCH";
        [SerializeField] private string rapidAction="RAPID SALVAGE";
        [SerializeField] private Color markerColor=new Color(.84f,.72f,.35f);
        [SerializeField] private DiscoveryRewardProfile rewards;
        public DiscoveryKind Kind=>kind;
        public string DisplayName=>displayName;
        public string Description=>description;
        public string CautiousAction=>cautiousAction;
        public string RapidAction=>rapidAction;
        public Color MarkerColor=>markerColor;
        public DiscoveryRewardProfile Rewards=>ExplorationRules.ValidProfile(rewards)?rewards:ExplorationRules.DefaultProfile(kind);
    }
}
