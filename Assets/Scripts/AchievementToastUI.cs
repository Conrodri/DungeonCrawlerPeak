using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Top-center achievement popup (2026-09-21 request: "affiche au milieu en haut de l'ecran").
// Unlike RoomAnnouncementUI/VictoryBannerUI's single overwrite-on-show, this queues - two
// achievements unlocking close together (e.g. a boss kill completing a slayer achievement the
// same frame a kill-count threshold ticks over) both get their own full display instead of one
// clobbering the other's text mid-banner.
public class AchievementToastUI : MonoBehaviour
{
    public GameObject root;
    public Text titleText;
    public Text descriptionText;
    public float displayDuration = 3.5f;

    readonly Queue<(string title, string description)> pending = new Queue<(string, string)>();
    bool showing;

    public void Enqueue(string title, string description)
    {
        pending.Enqueue((title, description));
        if (!showing) ShowNext();
    }

    void ShowNext()
    {
        if (pending.Count == 0)
        {
            showing = false;
            if (root != null) root.SetActive(false);
            return;
        }

        showing = true;
        (string title, string description) next = pending.Dequeue();
        if (titleText != null) titleText.text = "Succes debloque : " + next.title;
        if (descriptionText != null) descriptionText.text = next.description;
        if (root != null) root.SetActive(true);
        CancelInvoke(nameof(ShowNext));
        Invoke(nameof(ShowNext), displayDuration);
    }
}
