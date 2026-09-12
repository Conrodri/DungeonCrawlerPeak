using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// Rolls a virtual d20 with a quick slot-machine-style spin (fast at first, slowing down) before
// settling on the result. Stays hidden whenever no roll is in progress.
public class DiceRollUI : MonoBehaviour
{
    public Text rollText;
    public Image background;
    public Color rollingColor = new Color(0.2f, 0.2f, 0.25f, 0.95f);
    public Color successColor = new Color(0.25f, 0.65f, 0.3f, 0.95f);
    public Color failureColor = new Color(0.65f, 0.2f, 0.2f, 0.95f);

    const int SpinSteps = 14;

    public IEnumerator Roll(int statValue, int proficiency, int dc, Action<int, bool> onResolved)
    {
        gameObject.SetActive(true);
        if (background != null) background.color = rollingColor;

        for (int i = 0; i < SpinSteps; i++)
        {
            rollText.text = UnityEngine.Random.Range(1, 21).ToString();
            float wait = Mathf.Lerp(0.03f, 0.14f, i / (float)(SpinSteps - 1));
            yield return new WaitForSeconds(wait);
        }

        int finalRoll = UnityEngine.Random.Range(1, 21);
        int total = finalRoll + statValue + proficiency;
        bool success = total >= dc;

        rollText.text = finalRoll + " + " + (statValue + proficiency) + " = " + total;
        if (background != null) background.color = success ? successColor : failureColor;

        yield return new WaitForSeconds(1.2f);

        gameObject.SetActive(false);
        onResolved?.Invoke(finalRoll, success);
    }
}
