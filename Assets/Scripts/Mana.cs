using System;
using UnityEngine;

// Generic mana pool, same split/shape as Stamina: this component just holds the numbers and
// regenerates them, PlayerStats (Intelligence) drives maxMana/regenPerSecond and PlayerController
// (Orbe de Foudre and any future spell) is what calls Drain.
public class Mana : MonoBehaviour
{
    public float maxMana = 40f;
    public float currentMana;
    public float regenPerSecond = 4f;
    // Same reasoning as Stamina.regenDelay - regen pauses briefly after a spend so spamming a
    // cheap spell doesn't visibly fight its own regen tick-for-tick.
    public float regenDelay = 0.5f;

    public event Action<float, float> OnManaChanged;

    float lastDrainTime = -999f;

    void Awake()
    {
        currentMana = maxMana;
    }

    void Update()
    {
        if (currentMana >= maxMana || Time.time - lastDrainTime < regenDelay) return;
        currentMana = Mathf.Min(maxMana, currentMana + regenPerSecond * Time.deltaTime);
        OnManaChanged?.Invoke(currentMana, maxMana);
    }

    public void Drain(float amount)
    {
        if (amount <= 0f) return;
        currentMana = Mathf.Max(0f, currentMana - amount);
        lastDrainTime = Time.time;
        OnManaChanged?.Invoke(currentMana, maxMana);
    }
}
