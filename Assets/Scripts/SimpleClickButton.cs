using System;
using UnityEngine;
using UnityEngine.EventSystems;

// Generic "click runs this callback" component - for the handful of UI buttons built purely from
// code (training-room spawner lists, the Grimoire's spell chips) that don't need a dedicated
// per-slot handler class the way SpellSlotUI/InventorySlotUI do (those also track slot state, this
// one is pure fire-and-forget).
public class SimpleClickButton : MonoBehaviour, IPointerClickHandler
{
    public Action onClick;

    public void OnPointerClick(PointerEventData eventData) => onClick?.Invoke();
}
