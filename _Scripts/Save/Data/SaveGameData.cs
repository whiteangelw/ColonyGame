using System;
using System.Collections.Generic;

[Serializable]
public class SaveGameData
{
    public int saveVersion = 1;
    public string savedAtUtc;
    public WorldSaveData world = new WorldSaveData();
    public List<StructureSaveData> structures = new List<StructureSaveData>();
    public List<BlueprintSaveData> blueprints = new List<BlueprintSaveData>();
    public List<ResourceItemSaveData> groundResources = new List<ResourceItemSaveData>();
    public List<DuplicantSaveData> duplicants = new List<DuplicantSaveData>();
    public bool hasCreatureState;
    public List<CreatureSaveData> creatures = new List<CreatureSaveData>();
    public List<FloraSaveData> flora = new List<FloraSaveData>();
    public List<TaskSaveData> persistentTasks = new List<TaskSaveData>();
    public PrintingPodSchedulerSaveData printingPodScheduler =
        new PrintingPodSchedulerSaveData();
    public DayNightCycleSaveData dayNightCycle =
        new DayNightCycleSaveData();
    public CameraSaveData camera = new CameraSaveData();
    public RecipeUnlockSaveData recipeUnlocks =
        new RecipeUnlockSaveData();
}

[Serializable]
public class RecipeUnlockSaveData
{
    public bool hasState;
    public List<string> unlockedDefinitionIds = new List<string>();
    public List<int> unlockedLegacyTileTypes = new List<int>();
}

[Serializable]
public class WorldSaveData
{
    public int width;
    public int height;
    public float cellSize;
    public float generationSeed;
    // Formato legado v1. Mantido para leitura de saves existentes.
    public List<TileSaveData> tiles = new List<TileSaveData>();

    // Formato compacto v2. O índice da célula é x * height + y.
    public int[] tileTypes;
    public int[] structureTileTypes;
    public int[] backWallTileTypes;
    public int[] decorationTileTypes;
    public int[] fogStates;
    public float[] liquidAmounts;
    public List<TileContentSaveData> contentCells =
        new List<TileContentSaveData>();
}

[Serializable]
public struct TileContentSaveData
{
    public int cellIndex;
    public string terrainContentId;
    public string structureContentId;
    public string backWallContentId;
    public string decorationContentId;
}

[Serializable]
public class TileSaveData
{
    public int x;
    public int y;
    public int tileType;
    public int structureTileType;
    public int backWallTileType;
    public int decorationTileType;
    public string terrainContentId;
    public string structureContentId;
    public string backWallContentId;
    public string decorationContentId;
    public int fogState;
    public float liquidAmount;
}

[Serializable]
public class StructureSaveData
{
    public int x;
    public int y;
    public int tileType;
    public string definitionId;
    public bool isBeingDismantled;
    public int maxCapacityPerResource;
    public bool hasPlanterState;
    public int planterState;
    public float planterRemainingGrowthTime;
    public string planterCropId;
    public List<ResourceAmountSaveData> planterDeliveredMaterials =
        new List<ResourceAmountSaveData>();
    public bool hasProductionState;
    public string productionRecipeId;
    public int productionRemainingBatches;
    public float productionCompletedWork;
    public List<ResourceAmountSaveData> productionInputs =
        new List<ResourceAmountSaveData>();
    public List<ResourceAmountSaveData> productionOutputs =
        new List<ResourceAmountSaveData>();
    public List<ResourceAmountSaveData> storedItems =
        new List<ResourceAmountSaveData>();
}

[Serializable]
public class ResourceAmountSaveData
{
    public int resourceType;
    public int amount;
}

[Serializable]
public class BlueprintSaveData
{
    public int x;
    public int y;
    public int targetTileType;
    public string buildDefinitionId;
    public int buildLayer;
    public int requiredResource;
    public int requiredAmount;
    public int deliveredAmount;
    public float totalWorkRequired;
    public float currentWorkDone;
}

[Serializable]
public class ResourceItemSaveData
{
    public int resourceType;
    public int amount;
    public float worldX;
    public float worldY;
    public float worldZ;
}

[Serializable]
public class DuplicantSaveData
{
    public string definitionId;
    public string displayName;
    public int gridX;
    public int gridY;
    public bool hasCarriedResource;
    public int carriedResourceType;
    public int carriedAmount;
    public int inventoryCapacity;
    public bool hasVitals;
    public float currentEnergy;
    public float currentHunger;
    // Intenção transitória: a reserva da cama é reconstruída após o load.
    public bool wasRestingInBed;
    public float rawFoodDiscomfortRemaining;
    public float rawFoodWorkPenaltyPercent;
}

[Serializable]
public class CreatureSaveData
{
    public string creatureId;
    public int gridX;
    public int gridY;
    public int homeX;
    public int homeY;
    public string spawnOrigin;
    public string biomeId;
    public CreatureDomesticationSaveData domestication =
        new CreatureDomesticationSaveData();
}

[Serializable]
public class FloraSaveData
{
    public string floraId;
    public int gridX;
    public int gridY;
    public int availablePortions;
}

[Serializable]
public class TaskSaveData
{
    public int taskType;
    public int gridX;
    public int gridY;
    public int buildTileType;
    public int targetLayer;
    public int priority;
    public float workRequired;
    public float workCompleted;
    public bool preserveWorkOnInterruption;
}

[Serializable]
public class PrintingPodSchedulerSaveData
{
    public bool hasState;
    public float timeRemaining;
    public bool isOfferReady;
    public bool isTimerRunning;
    public bool hasGeneratedFirstOffer;
}

[Serializable]
public class DayNightCycleSaveData
{
    public bool hasState;
    public int currentDay;
    public float currentTimeRatio;
}

[Serializable]
public class CameraSaveData
{
    public bool hasState;
    public float worldX;
    public float worldY;
    public float worldZ;
    public float orthographicSize;
}
