public sealed class DuplicantRestDecision
{
    public bool ShouldSeekPreventiveRest(
        DuplicantController controller,
        DuplicantVitals vitals,
        LifeCycleSettingsSO settings)
    {
        if (controller == null || vitals == null || settings == null)
        {
            return false;
        }

        return controller.currentState == DuplicantController.WorkerState.Idle
            && controller.currentTask == null
            && !vitals.IsEmergencyResting
            && !vitals.IsBedResting
            && !vitals.IsStarving
            && vitals.EnergyPercent <= settings.seekBedBelowEnergyPercent;
    }
}
