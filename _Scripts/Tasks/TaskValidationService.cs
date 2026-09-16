public sealed class TaskValidationService
{
    public bool IsValid(Task task)
    {
        if (task == null || GridManager.Instance == null)
        {
            return false;
        }

        switch (task.type)
        {
            case TaskType.HaulResource:
                return IsHaulTaskValid(task);

            case TaskType.BuildTile:
                return IsBuildTaskValid(task);

            case TaskType.Dig:
                return IsDigTaskValid(task);

            case TaskType.Dismantle:
                return IsDismantleTaskValid(task);

            default:
                return false;
        }
    }

    private static bool IsHaulTaskValid(Task task)
    {
        if (task.targetBlueprint != null)
        {
            return task.targetBlueprint.CurrentState
                       == BlueprintState.WaitingMaterials
                && task.targetBlueprint.deliveredAmount
                   < task.targetBlueprint.requiredAmount;
        }

        if (task.groundHaulPhase
            == GroundHaulPhase.CarryingToStorage)
        {
            return task.isAssigned
                && task.groundHaulRunner != null
                && task.groundHaulRunner
                    .CanContinueGroundHaul(task);
        }

        return task.targetItem != null
            && task.targetItem.gameObject.activeInHierarchy
            && task.targetItem.IsReadyForHaul;
    }

    private static bool IsBuildTaskValid(Task task)
    {
        return task.targetBlueprint != null
            && task.targetBlueprint.CurrentState
                == BlueprintState.ReadyToBuild;
    }

    private static bool IsDigTaskValid(Task task)
    {
        if (task.targetBlueprint != null
            && task.targetBlueprint.CurrentState
                != BlueprintState.WaitingForClearance)
        {
            return false;
        }

        return WorldInteractionService.Instance != null
            && WorldInteractionService.Instance.CanDigTile(
                task.gridPosition.x,
                task.gridPosition.y,
                out _);
    }

    private static bool IsDismantleTaskValid(Task task)
    {
        return WorldInteractionService.Instance != null
            && WorldInteractionService.Instance.CanDismantleAt(
                task.gridPosition,
                task.targetLayer);
    }
}