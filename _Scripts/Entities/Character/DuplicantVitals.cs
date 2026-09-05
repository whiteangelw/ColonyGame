using System;
using UnityEngine;

[Flags]
public enum DuplicantNeedState
{
    Normal = 0,
    Hungry = 1 << 0,
    Starving = 1 << 1,
    Tired = 1 << 2,
    Exhausted = 1 << 3,
    EmergencyResting = 1 << 4
}

[DisallowMultipleComponent]
public class DuplicantVitals : MonoBehaviour
{
    [SerializeField] private float currentEnergy;
    [SerializeField] private float currentHunger;
    [SerializeField] private DuplicantNeedState currentNeedState;

    public float CurrentEnergy => currentEnergy;
    public float CurrentHunger => currentHunger;
    public float MaximumEnergy => lifeProfile != null
        ? Mathf.Max(1f, lifeProfile.maximumEnergy)
        : 100f;
    public float MaximumHunger => lifeProfile != null
        ? Mathf.Max(1f, lifeProfile.maximumHunger)
        : 100f;
    public float EnergyPercent => currentEnergy / MaximumEnergy;
    public float HungerPercent => currentHunger / MaximumHunger;
    public DuplicantNeedState CurrentNeedState => currentNeedState;
    public bool IsHungry => HasNeed(DuplicantNeedState.Hungry);
    public bool IsStarving => HasNeed(DuplicantNeedState.Starving);
    public bool IsTired => HasNeed(DuplicantNeedState.Tired);
    public bool IsExhausted => HasNeed(DuplicantNeedState.Exhausted);
    public bool IsEmergencyResting { get; private set; }

    public bool HasNeed(DuplicantNeedState need)
    {
        return need == DuplicantNeedState.Normal
            ? currentNeedState == DuplicantNeedState.Normal
            : (currentNeedState & need) == need;
    }

    public event Action<DuplicantVitals> OnVitalsChanged;
    public event Action<DuplicantVitals> OnEmergencyRestRequested;

    private DuplicantController controller;
    private DuplicantLifeProfile lifeProfile;
    private LifeCycleSettingsSO lastSettings;
    private bool initialized;
    private bool emergencyRequested;

    private void Awake()
    {
        controller = GetComponent<DuplicantController>();
    }

    private void OnEnable()
    {
        LifeCycleSystem.Instance?.Register(this);
    }

    private void OnDisable()
    {
        LifeCycleSystem.Instance?.Unregister(this);
    }

    public void Initialize(
        DuplicantLifeProfile profile,
        LifeCycleSettingsSO settings,
        bool resetValues = false)
    {
        lifeProfile = profile;
        lastSettings = settings;

        if (initialized && !resetValues)
        {
            ClampValues();
            EvaluateState();
            return;
        }

        float initialEnergy = settings != null
            ? settings.initialEnergyPercent
            : 1f;
        float initialHunger = settings != null
            ? settings.initialHungerPercent
            : 1f;

        currentEnergy = MaximumEnergy * initialEnergy;
        currentHunger = MaximumHunger * initialHunger;
        initialized = true;
        emergencyRequested = false;
        IsEmergencyResting = false;
        EvaluateState();
    }

    public void ApplyTick(LifeCycleSettingsSO settings, float elapsedSeconds)
    {
        if (settings == null || elapsedSeconds <= 0f)
        {
            return;
        }

        if (!initialized)
        {
            Initialize(controller != null ? controller.lifeProfile : null, settings);
        }

        lastSettings = settings;
        controller?.StatusEffects?.ApplyTick(elapsedSeconds);
        float hungerMultiplier = lifeProfile != null
            ? lifeProfile.hungerDrainMultiplier
            : 1f;
        currentHunger -= settings.hungerDrain
            * hungerMultiplier
            * elapsedSeconds;

        if (!IsEmergencyResting)
        {
            currentEnergy -= GetEnergyDrain(settings)
                * GetEnergyMultiplier()
                * elapsedSeconds;
        }

        ClampValues();
        EvaluateState();
        RequestEmergencyRestIfNeeded();
        OnVitalsChanged?.Invoke(this);
    }

    public void SetEnergyPercent(float percent)
    {
        EnsureInitialized();
        currentEnergy = MaximumEnergy * Mathf.Clamp01(percent);
        EvaluateState();
        RequestEmergencyRestIfNeeded();
        OnVitalsChanged?.Invoke(this);
    }

    public void SetHungerPercent(float percent)
    {
        EnsureInitialized();
        currentHunger = MaximumHunger * Mathf.Clamp01(percent);
        EvaluateState();
        OnVitalsChanged?.Invoke(this);
    }

    public void RestoreHunger(float amount)
    {
        EnsureInitialized();
        currentHunger = Mathf.Min(MaximumHunger, currentHunger + Mathf.Max(0f, amount));
        EvaluateState();
        OnVitalsChanged?.Invoke(this);
    }

    public float EstimateHungerAfterSeconds(
        float seconds,
        LifeCycleSettingsSO settings = null)
    {
        LifeCycleSettingsSO effectiveSettings = settings ?? lastSettings;
        if (effectiveSettings == null || seconds <= 0f) return currentHunger;

        float multiplier = lifeProfile != null
            ? lifeProfile.hungerDrainMultiplier
            : 1f;
        return Mathf.Max(
            0f,
            currentHunger
                - effectiveSettings.hungerDrain * multiplier * seconds);
    }

    public void Restore(float energy, float hunger, bool hasSavedVitals)
    {
        lifeProfile = controller != null ? controller.lifeProfile : lifeProfile;
        lastSettings = LifeCycleSystem.Instance != null
            ? LifeCycleSystem.Instance.Settings
            : lastSettings;
        initialized = true;
        currentEnergy = hasSavedVitals ? energy : MaximumEnergy;
        currentHunger = hasSavedVitals ? hunger : MaximumHunger;
        IsEmergencyResting = false;
        emergencyRequested = false;
        ClampValues();
        EvaluateState();
        OnVitalsChanged?.Invoke(this);
    }

    public void BeginEmergencyRest()
    {
        IsEmergencyResting = true;
        currentNeedState = DuplicantNeedState.EmergencyResting;
        OnVitalsChanged?.Invoke(this);
    }

    public void CompleteEmergencyRest(float recoveryPercent)
    {
        float multiplier = lifeProfile != null
            ? lifeProfile.restRecoveryMultiplier
            : 1f;
        currentEnergy = Mathf.Min(
            MaximumEnergy,
            currentEnergy + MaximumEnergy * recoveryPercent * multiplier
        );
        IsEmergencyResting = false;
        emergencyRequested = false;
        EvaluateState();
        OnVitalsChanged?.Invoke(this);
    }

    public void CancelEmergencyRest()
    {
        IsEmergencyResting = false;
        emergencyRequested = false;
        EvaluateState();
    }

    private float GetEnergyDrain(LifeCycleSettingsSO settings)
    {
        if (controller == null) return settings.idleEnergyDrain;

        switch (controller.currentState)
        {
            case DuplicantController.WorkerState.Moving:
            case DuplicantController.WorkerState.Falling:
                return settings.movingEnergyDrain;
            case DuplicantController.WorkerState.Working:
            case DuplicantController.WorkerState.Eating:
                return settings.workingEnergyDrain;
            default:
                return settings.idleEnergyDrain;
        }
    }

    private float GetEnergyMultiplier()
    {
        return lifeProfile != null
            ? lifeProfile.energyDrainMultiplier
            : 1f;
    }

    private void RequestEmergencyRestIfNeeded()
    {
        if (currentEnergy > 0f || emergencyRequested || IsEmergencyResting)
        {
            return;
        }

        emergencyRequested = true;
        OnEmergencyRestRequested?.Invoke(this);
    }

    private void EvaluateState()
    {
        if (IsEmergencyResting)
        {
            currentNeedState = DuplicantNeedState.EmergencyResting;
            return;
        }

        float hungry = lastSettings != null ? lastSettings.hungryThreshold : 0.35f;
        float starving = lastSettings != null ? lastSettings.starvingThreshold : 0.10f;
        float tired = lastSettings != null ? lastSettings.tiredThreshold : 0.30f;
        float exhausted = lastSettings != null ? lastSettings.exhaustedThreshold : 0.10f;

        currentNeedState = DuplicantNeedState.Normal;

        if (HungerPercent <= starving)
        {
            currentNeedState |= DuplicantNeedState.Starving;
        }
        else if (HungerPercent <= hungry)
        {
            currentNeedState |= DuplicantNeedState.Hungry;
        }

        if (EnergyPercent <= exhausted)
        {
            currentNeedState |= DuplicantNeedState.Exhausted;
        }
        else if (EnergyPercent <= tired)
        {
            currentNeedState |= DuplicantNeedState.Tired;
        }
    }

    private void EnsureInitialized()
    {
        if (!initialized)
        {
            Initialize(
                controller != null ? controller.lifeProfile : null,
                LifeCycleSystem.Instance != null
                    ? LifeCycleSystem.Instance.Settings
                    : null
            );
        }
    }

    private void ClampValues()
    {
        currentEnergy = Mathf.Clamp(currentEnergy, 0f, MaximumEnergy);
        currentHunger = Mathf.Clamp(currentHunger, 0f, MaximumHunger);
    }
}
