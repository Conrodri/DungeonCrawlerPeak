using UnityEngine;
using UnityEngine.UI;

// The "[E] ..." proximity prompt every stationary interactable (Chest, Corpse, RestBed,
// TutorialNpc) builds at the bottom-center of the screen, hidden until the player is nearby - was
// copy-pasted identically in all four (2026-09-16 cleanup; RestBed's copy had already drifted to
// its own field names while keeping the exact same layout values, a sign the duplication was
// already starting to rot).
public static class InteractPromptUI
{
    // Returns the Text component (not just the GameObject) so a caller that needs to change the
    // wording later - RestBed shows a temporary message after resting, then reverts to the
    // default prompt - can keep it; every other caller can just discard the return value and
    // drive visibility through the GameObject alone.
    public static Text Build(Transform canvasParent, string name, string text)
    {
        Font font = Font.CreateDynamicFontFromOSFont("Arial", 28);

        GameObject promptGO = new GameObject(name, typeof(Text));
        promptGO.transform.SetParent(canvasParent, false);
        Text prompt = promptGO.GetComponent<Text>();
        prompt.font = font;
        prompt.fontSize = 28;
        prompt.alignment = TextAnchor.MiddleCenter;
        prompt.color = Color.white;
        prompt.text = text;
        RectTransform promptRect = prompt.rectTransform;
        promptRect.anchorMin = promptRect.anchorMax = new Vector2(0.5f, 0f);
        promptRect.pivot = new Vector2(0.5f, 0f);
        promptRect.anchoredPosition = new Vector2(0f, 120f);
        promptRect.sizeDelta = new Vector2(500f, 40f);
        promptGO.SetActive(false);
        return prompt;
    }
}
