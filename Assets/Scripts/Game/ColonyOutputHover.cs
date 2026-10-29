using UnityEngine;
using UnityEngine.EventSystems;

namespace WH30K.UI
{
    public sealed class ColonyOutputHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private NewGameMenu menu;
        public void Configure(NewGameMenu owner) => menu = owner;
        public void OnPointerEnter(PointerEventData eventData) => menu?.ShowOutputTooltip((RectTransform)transform);
        public void OnPointerExit(PointerEventData eventData) => menu?.HideOutputTooltip();
        private void OnDisable() => menu?.HideOutputTooltip();
    }
}
