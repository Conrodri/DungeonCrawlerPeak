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
    // Potion d'Adrenaline (2026-09-21 request: "recupere 2x plus vite l'endurance pendant 2
    // minutes") - same "strongest wins, duration extends" pattern as PlayerController's
    // ApplySlow/ApplyHaste, just for regen speed instead of move speed.
    float regenBuffMultiplier = 1f;
    float regenBuffEndTime = -999f;

    void Awake()
    {
        currentStamina = maxStamina;
    }

    void Update()
    {
        if (currentStamina >= maxStamina || Time.time - lastDrainTime < regenDelay) return;
        float multiplier = Time.time < regenBuffEndTime ? regenBuffMultiplier : 1f;
        currentStamina = Mathf.Min(maxStamina, currentStamina + regenPerSecond * multiplier * Time.deltaTime);
        OnStaminaChanged?.Invoke(currentStamina, maxStamina);
    }

    public void ApplyRegenBuff(float multiplier, float duration)
    {
        if (Time.time >= regenBuffEndTime || multiplier > regenBuffMultiplier) regenBuffMultiplier = multiplier;
        regenBuffEndTime = Mathf.Max(regenBuffEndTime, Time.time + duration);
    }

    public void Drain(float amount)
    {
        if (amount <= 0f) return;
        currentStamina = Mathf.Max(0f, currentStamina - amount);
        lastDrainTime = Time.time;
        OnStaminaChanged?.Invoke(currentStamina, maxStamina);
    }
}
