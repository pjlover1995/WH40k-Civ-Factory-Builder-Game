using UnityEngine;
using UnityEngine.EventSystems;

namespace WH30K.UI
{
    internal enum OutputHoverTopic { Colony, Food, Water }

    public sealed class ColonyOutputHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private NewGameMenu menu;
        private OutputHoverTopic topic;

        internal void Configure(NewGameMenu owner, OutputHoverTopic subject)
        {
            menu = owner;
            topic = subject;
        }

        public void OnPointerEnter(PointerEventData eventData) => menu?.ShowOutputTooltip((RectTransform)transform, topic);
        public void OnPointerExit(PointerEventData eventData) => menu?.HideOutputTooltip();
        private void OnDisable() => menu?.HideOutputTooltip();
    }
}
