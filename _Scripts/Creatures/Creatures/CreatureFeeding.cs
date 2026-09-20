using System.Collections;
using UnityEngine;

[RequireComponent(typeof(CreatureController))]
public sealed class CreatureFeeding : MonoBehaviour
{
    private CreatureController controller;
    private Coroutine eatingRoutine;
    private float satisfiedUntil;

    public ResourceItem ReservedItem { get; private set; }
    public bool IsEating => eatingRoutine != null;
    public bool IsSatisfied => Time.time < satisfiedUntil;
    public float SatisfiedRemaining => Mathf.Max(0f, satisfiedUntil - Time.time);
    public string LastResult { get; private set; } = "Nenhuma alimentação";

    private void Awake() => controller = GetComponent<CreatureController>();

    private void OnDisable() => CancelCurrentFeeding();

    public bool TryReserve(ResourceItem item)
    {
        if (item == null || IsEating || IsSatisfied) return false;
        if (ReservedItem == item) return true;

        ReleaseReservation();
        if (!item.TryReserveCreaturePortion(this, item.type))
        {
            LastResult = "Não foi possível reservar " + item.type;
            return false;
        }

        ReservedItem = item;
        LastResult = "Porção reservada: " + item.type;
        return true;
    }

    public bool HasValidReservation()
    {
        return ReservedItem != null
            && ReservedItem.gameObject.activeInHierarchy
            && ReservedItem.amount > 0;
    }

    public bool TryBeginEating()
    {
        if (IsEating || !HasValidReservation()) return false;
        if (Manhattan(controller.GridPosition, ReservedItem.GridPosition) > 1)
            return false;

        eatingRoutine = StartCoroutine(EatRoutine());
        return true;
    }

    public void ReceiveDirectMeal(ResourceType foodType, bool uncomfortable = false)
    {
        CancelCurrentFeeding();
        eatingRoutine = StartCoroutine(DirectMealRoutine(foodType, uncomfortable));
    }

    public void CancelCurrentFeeding()
    {
        if (eatingRoutine != null)
        {
            StopCoroutine(eatingRoutine);
            eatingRoutine = null;
            LastResult = "Alimentação interrompida";
        }
        ReleaseReservation();
    }

    private IEnumerator EatRoutine()
    {
        ResourceItem meal = ReservedItem;
        controller.SetDecision(CreatureState.Eating,
            "Comendo " + meal.type, meal.GridPosition);

        float duration = controller.Definition != null
            ? Mathf.Max(0.1f, controller.Definition.eatingDuration)
            : 2f;
        yield return new WaitForSeconds(duration);

        bool consumed = meal != null
            && meal.TryConsumeCreatureReservedPortion(this);
        ReservedItem = null;
        eatingRoutine = null;

        if (!consumed)
        {
            LastResult = "A porção reservada deixou de existir";
            controller.SetDecision(CreatureState.Idle,
                "Alimentação não concluída");
            controller.Brain?.ForceDecision();
            yield break;
        }

        float satisfaction = controller.Definition != null
            ? Mathf.Max(0f, controller.Definition.satisfiedDuration)
            : 0f;
        satisfiedUntil = Time.time + satisfaction;
        LastResult = "Comeu uma porção; sem progresso de domesticação";
        controller.SetDecision(CreatureState.Satisfied,
            "Satisfeita após comer alimento do chão");
        controller.Brain?.ForceDecision();
    }

    private IEnumerator DirectMealRoutine(ResourceType foodType, bool uncomfortable)
    {
        controller.SetDecision(CreatureState.Eating,
            "Recebendo alimentação direta: " + foodType);

        float duration = controller.Definition != null
            ? Mathf.Max(0.1f, controller.Definition.eatingDuration)
            : 2f;
        yield return new WaitForSeconds(duration);

        eatingRoutine = null;
        float satisfaction = controller.Definition != null
            ? Mathf.Max(0f, controller.Definition.satisfiedDuration)
            : 0f;
        satisfiedUntil = Time.time + satisfaction;
        LastResult = uncomfortable
            ? "Aceitou alimento em excesso: " + foodType
            : "Alimentação direta recebida: " + foodType;
        controller.SetDecision(
            uncomfortable ? CreatureState.Uncomfortable : CreatureState.Satisfied,
            uncomfortable
                ? "Desconfortável após excesso de alimento"
                : "Satisfeita após alimentação direta");
        controller.Brain?.ForceDecision();
    }

    private void ReleaseReservation()
    {
        if (ReservedItem != null)
            ReservedItem.ReleaseCreatureReservation(this);
        ReservedItem = null;
    }

    private static int Manhattan(Vector2Int a, Vector2Int b)
        => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
}
