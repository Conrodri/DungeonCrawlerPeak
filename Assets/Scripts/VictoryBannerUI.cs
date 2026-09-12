using UnityEngine;
using UnityEngine.UI;

public class VictoryBannerUI : MonoBehaviour
{
    public GameObject root;
    public Text bannerText;
    public float displayDuration = 3f;

    public void ShowVictory(string message)
    {
        if (bannerText != null) bannerText.text = message;
        if (root != null) root.SetActive(true);
        CancelInvoke(nameof(Hide));
        Invoke(nameof(Hide), displayDuration);
    }

    void Hide()
    {
        if (root != null) root.SetActive(false);
    }
}
