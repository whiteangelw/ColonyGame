using System;
using System.Collections.Generic;
using UnityEngine;

public enum StructureSupportRule
{
    None,
    AnyBottomCell,
    EveryBottomCell
}

[Serializable]
public sealed class StructureFootprintDefinition
{
    public TileType tileType;

    [Min(1)] public int width = 1;
    [Min(1)] public int height = 1;

    [Tooltip("Deslocamento da célula inferior esquerda em relação à célula clicada.")]
    public Vector2Int originOffset = Vector2Int.zero;

    [Tooltip("Impede duplicants de ocupar as células da estrutura pronta.")]
    public bool blocksMovement = true;

    [Tooltip("Permite que outras entidades se apoiem no topo da estrutura.")]
    public bool supportsWeight = true;

    public StructureSupportRule supportRule =
        StructureSupportRule.EveryBottomCell;

    public bool IsMultiCell => width > 1 || height > 1;

    public Vector2Int GetMinimumCell(Vector2Int anchor)
    {
        return anchor + originOffset;
    }
}

/// <summary>
/// Catálogo editável das dimensões e regras físicas das estruturas.
/// Se não existir na cena, valores seguros são usados como fallback.
/// </summary>
public class StructureFootprintSettings : Singleton<StructureFootprintSettings>
{
    [SerializeField]
    private List<StructureFootprintDefinition> definitions =
        new List<StructureFootprintDefinition>();

    public StructureFootprintDefinition GetDefinition(TileType tileType)
    {
        foreach (StructureFootprintDefinition definition in definitions)
        {
            if (definition != null && definition.tileType == tileType)
            {
                Sanitize(definition);
                return definition;
            }
        }

        return CreateFallback(tileType);
    }

    public static StructureFootprintDefinition Resolve(TileType tileType)
    {
        return Instance != null
            ? Instance.GetDefinition(tileType)
            : CreateFallback(tileType);
    }

    public static bool IsWorldStructure(TileType tileType)
    {
        return tileType == TileType.Chest
            || tileType == TileType.PrintingPod;
    }

    private static void Sanitize(StructureFootprintDefinition definition)
    {
        definition.width = Mathf.Max(1, definition.width);
        definition.height = Mathf.Max(1, definition.height);
    }

    private static StructureFootprintDefinition CreateFallback(
        TileType tileType)
    {
        if (tileType == TileType.PrintingPod)
        {
            return new StructureFootprintDefinition
            {
                tileType = tileType,
                width = 3,
                height = 3,
                originOffset = new Vector2Int(-1, 0),
                blocksMovement = true,
                supportsWeight = true,
                supportRule = StructureSupportRule.EveryBottomCell
            };
        }

        if (tileType == TileType.Chest)
        {
            return new StructureFootprintDefinition
            {
                tileType = tileType,
                width = 1,
                height = 1,
                originOffset = Vector2Int.zero,
                blocksMovement = true,
                supportsWeight = true,
                supportRule = StructureSupportRule.EveryBottomCell
            };
        }

        return new StructureFootprintDefinition
        {
            tileType = tileType,
            width = 1,
            height = 1,
            originOffset = Vector2Int.zero,
            blocksMovement = tileType != TileType.Ladder,
            supportsWeight = tileType != TileType.Ladder,
            supportRule = StructureSupportRule.None
        };
    }
}
