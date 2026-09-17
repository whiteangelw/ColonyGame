using System;
using UnityEngine;

[Serializable]
public sealed class DuplicantLogisticsRoutingSettings
{
    [Tooltip("Permite levar recursos coletados diretamente para blueprints.")]
    public bool enableDirectBlueprintDelivery = true;

    [Min(1)]
    [Tooltip("Distância máxima em células para procurar uma blueprint.")]
    public int maximumDirectDeliveryDistance = 24;

    [Min(1)]
    [Tooltip("Quantidade máxima de blueprints visitadas na mesma rota.")]
    public int maximumBlueprintStops = 6;

    [Min(1)]
    [Tooltip("Carga mínima necessária para considerar entrega direta.")]
    public int minimumCarriedAmount = 1;

    [Min(0)]
    [Tooltip("Passos extras permitidos em relação ao caminho até o baú.")]
    public int allowedExtraTravelSteps = 2;

    [Tooltip("Impede desviar para uma blueprint de prioridade menor que o transporte atual.")]
    public bool requireEqualOrHigherPriority = true;

    [Tooltip("Exibe no Console a decisão entre blueprint e baú.")]
    public bool enableDiagnostics = false;

    public int MaximumDirectDeliveryDistance =>
        Mathf.Max(1, maximumDirectDeliveryDistance);

    public int MaximumBlueprintStops =>
        Mathf.Max(1, maximumBlueprintStops);

    public int MinimumCarriedAmount =>
        Mathf.Max(1, minimumCarriedAmount);

    public int AllowedExtraTravelSteps =>
        Mathf.Max(0, allowedExtraTravelSteps);
}
