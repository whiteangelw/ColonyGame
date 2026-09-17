#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public enum BuildContentValidationSeverity
{
    Info,
    Warning,
    Error
}

public struct BuildContentValidationIssue
{
    public BuildContentValidationSeverity Severity { get; }
    public UnityEngine.Object Context { get; }
    public string Message { get; }

    public BuildContentValidationIssue(
        BuildContentValidationSeverity severity,
        UnityEngine.Object context,
        string message)
    {
        Severity = severity;
        Context = context;
        Message = message;
    }
}

/// <summary>
/// Regras técnicas do conteúdo de construção. Não define regras de gameplay.
/// </summary>
public static class BuildContentValidator
{
    public static List<BuildContentValidationIssue> Validate(
        BuildCatalogSO catalog)
    {
        List<BuildContentValidationIssue> issues =
            new List<BuildContentValidationIssue>();

        if (catalog == null)
        {
            issues.Add(new BuildContentValidationIssue(
                BuildContentValidationSeverity.Error,
                null,
                "Build Catalog ausente."));
            return issues;
        }

        Dictionary<string, BuildDefinitionSO> definitionsById =
            new Dictionary<string, BuildDefinitionSO>(StringComparer.Ordinal);

        IReadOnlyList<BuildDefinitionSO> definitions = catalog.Definitions;
        for (int index = 0; index < definitions.Count; index++)
        {
            BuildDefinitionSO definition = definitions[index];
            if (definition == null)
            {
                issues.Add(new BuildContentValidationIssue(
                    BuildContentValidationSeverity.Error,
                    catalog,
                    "Entrada nula no catálogo, índice " + index + "."));
                continue;
            }

            ValidateDefinition(definition, issues);

            string id = definition.DefinitionId;
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            if (definitionsById.TryGetValue(id, out BuildDefinitionSO other))
            {
                issues.Add(new BuildContentValidationIssue(
                    BuildContentValidationSeverity.Error,
                    definition,
                    "Definition Id duplicado '" + id + "'. Também usado por '"
                    + other.name + "'."));
                continue;
            }

            definitionsById.Add(id, definition);
        }

        return issues;
    }

    public static List<BuildContentValidationIssue> Validate(
        BuildDefinitionSO definition)
    {
        List<BuildContentValidationIssue> issues =
            new List<BuildContentValidationIssue>();
        ValidateDefinition(definition, issues);
        return issues;
    }

    private static void ValidateDefinition(
        BuildDefinitionSO definition,
        List<BuildContentValidationIssue> issues)
    {
        if (definition == null)
        {
            issues.Add(new BuildContentValidationIssue(
                BuildContentValidationSeverity.Error,
                null,
                "Build Definition ausente."));
            return;
        }

        if (string.IsNullOrWhiteSpace(definition.DefinitionId))
        {
            AddError(issues, definition, "Definition Id não pode ficar vazio.");
        }

        if (!Enum.IsDefined(typeof(TileType), definition.TileType))
        {
            AddError(issues, definition, "Tile Type possui um valor inválido.");
        }
        else if (definition.TileType == TileType.Empty)
        {
            AddError(issues, definition, "Tile Type Empty não é uma construção válida.");
        }

        if (!Enum.IsDefined(typeof(GridLayer), definition.PlacementLayer))
        {
            AddError(issues, definition, "Placement Layer possui um valor inválido.");
        }

        if (definition.RequiresTileAsset && definition.TileAsset == null)
        {
            AddError(issues, definition,
                "A definição exige Tile Asset, mas nenhum foi configurado.");
        }

        if (definition.RequiresPhysicalPrefab && definition.Prefab == null)
        {
            AddError(issues, definition,
                "A definição exige prefab físico, mas nenhum foi configurado.");
        }

        if (definition.Prefab == null
            && definition.TileAsset == null
            && !definition.RequiresPhysicalPrefab
            && !definition.RequiresTileAsset)
        {
            AddWarning(issues, definition,
                "A definição não possui prefab nem Tile Asset; ela não terá representação visual própria.");
        }

        if (definition.Prefab != null
            && definition.PlacementLayer != GridLayer.Terrain
            && definition.PlacementLayer != GridLayer.Structure)
        {
            AddError(issues, definition,
                "Prefabs físicos só são instanciados nas layers Terrain ou Structure.");
        }

        if (definition.Prefab != null
            && definition.Prefab.GetComponent<ConfiguredStructure>() == null)
        {
            AddError(issues, definition,
                "O prefab físico precisa ter ConfiguredStructure no objeto raiz.");
        }

        if (definition.Prefab != null && !definition.RequiresPhysicalPrefab)
        {
            AddInfo(issues, definition,
                "Há um prefab configurado. Marque Requires Physical Prefab para torná-lo obrigatório.");
        }

        StructureFootprintDefinition footprint = definition.Footprint;
        if (footprint == null)
        {
            AddError(issues, definition, "Footprint ausente.");
            return;
        }

        if (footprint.width < 1 || footprint.height < 1)
        {
            AddError(issues, definition,
                "Footprint inválido: Width e Height devem ser maiores que zero.");
        }

        if (footprint.supportsWeight && !footprint.blocksMovement)
        {
            AddInfo(issues, definition,
                "Supports Weight está ativo sem Blocks Movement. Isso é permitido, mas confirme se é intencional.");
        }
    }

    private static void AddError(
        List<BuildContentValidationIssue> issues,
        UnityEngine.Object context,
        string message)
    {
        issues.Add(new BuildContentValidationIssue(
            BuildContentValidationSeverity.Error, context, message));
    }

    private static void AddInfo(
        List<BuildContentValidationIssue> issues,
        UnityEngine.Object context,
        string message)
    {
        issues.Add(new BuildContentValidationIssue(
            BuildContentValidationSeverity.Info, context, message));
    }

    private static void AddWarning(
        List<BuildContentValidationIssue> issues,
        UnityEngine.Object context,
        string message)
    {
        issues.Add(new BuildContentValidationIssue(
            BuildContentValidationSeverity.Warning, context, message));
    }
}

public static class BuildContentValidationMenu
{
    [MenuItem("Tools/Colony/Validate Build Content")]
    private static void ValidateBuildContent()
    {
        string[] catalogGuids = AssetDatabase.FindAssets("t:BuildCatalogSO");
        List<BuildContentValidationIssue> issues =
            new List<BuildContentValidationIssue>();

        foreach (string guid in catalogGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            BuildCatalogSO catalog = AssetDatabase.LoadAssetAtPath<BuildCatalogSO>(path);
            issues.AddRange(BuildContentValidator.Validate(catalog));
        }

        if (catalogGuids.Length == 0)
        {
            Debug.LogWarning("[Build Content Validator] Nenhum BuildCatalogSO foi encontrado.");
            return;
        }

        int errors = 0;
        int warnings = 0;
        int infos = 0;

        foreach (BuildContentValidationIssue issue in issues)
        {
            switch (issue.Severity)
            {
                case BuildContentValidationSeverity.Error:
                    errors++;
                    Debug.LogError("[Build Content Validator] " + issue.Message,
                        issue.Context);
                    break;
                case BuildContentValidationSeverity.Warning:
                    warnings++;
                    Debug.LogWarning("[Build Content Validator] " + issue.Message,
                        issue.Context);
                    break;
                default:
                    infos++;
                    Debug.Log("[Build Content Validator] " + issue.Message,
                        issue.Context);
                    break;
            }
        }

        string summary = "[Build Content Validator] Validação concluída: "
            + errors + " erro(s), " + warnings + " aviso(s), " + infos + " informação(ões).";

        if (errors > 0) Debug.LogError(summary);
        else if (warnings > 0) Debug.LogWarning(summary);
        else Debug.Log(summary);
    }
}

[CustomEditor(typeof(BuildCatalogSO))]
public sealed class BuildCatalogSOEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        DrawIssues(BuildContentValidator.Validate((BuildCatalogSO)target));
    }

    private static void DrawIssues(List<BuildContentValidationIssue> issues)
    {
        foreach (BuildContentValidationIssue issue in issues)
        {
            EditorGUILayout.HelpBox(issue.Message, ToMessageType(issue.Severity));
        }
    }

    private static MessageType ToMessageType(BuildContentValidationSeverity severity)
    {
        if (severity == BuildContentValidationSeverity.Error) return MessageType.Error;
        if (severity == BuildContentValidationSeverity.Warning) return MessageType.Warning;
        return MessageType.Info;
    }
}

[CustomEditor(typeof(BuildDefinitionSO))]
public sealed class BuildDefinitionSOEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        foreach (BuildContentValidationIssue issue in
            BuildContentValidator.Validate((BuildDefinitionSO)target))
        {
            MessageType type = issue.Severity == BuildContentValidationSeverity.Error
                ? MessageType.Error
                : issue.Severity == BuildContentValidationSeverity.Warning
                    ? MessageType.Warning
                    : MessageType.Info;
            EditorGUILayout.HelpBox(issue.Message, type);
        }
    }
}
#endif
