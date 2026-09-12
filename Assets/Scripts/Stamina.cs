using System;
using UnityEngine;

// Generic stamina pool, same split as Health: this component just holds the numbers and drains/
// regenerates them, PlayerStats (Endurance) drives maxStamina/regenPerSecond and PlayerController
// (sprint) is the only thing that calls Drain today.
public class Stamina : MonoBehaviour
{
    public float maxStamina = 60f;
    public float currentStamina;
    public float regenPerSecond = 15f;
    // Regen pauses until this long after the last drain, so sprinting doesn't fight its own regen
    // tick-for-tick and stamina doesn't visibly creep back up mid-sprint.
    public float regenDelay = 0.5f;

    public event Action<float, float> OnStaminaChanged;

    float lastDrainTime = -999f;

    void Awake()
    {
        currentStamina = maxStamina;
    }

    void Update()
    {
        if (currentStamina >= maxStamina || Time.time - lastDrainTime < regenDelay) return;
        currentStamina = Mathf.Min(maxStamina, currentStamina + regenPerSecond * Time.deltaTime);
        OnStaminaChanged?.Invoke(currentStamina, maxStamina);
    }

    public void Drain(float amount)
    {
        if (amount <= 0f) return;
        currentStamina = Mathf.Max(0f, currentStamina - amount);
        lastDrainTime = Time.time;
        OnStaminaChanged?.Invoke(currentStamina, maxStamina);
    }
}
