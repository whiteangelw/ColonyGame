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
    public List<FloraSaveData> flora = new List<FloraSaveData>();
    public List<TaskSaveData> persistentTasks = new List<TaskSaveData>();
    public PrintingPodSchedulerSaveData printingPodScheduler =
        new PrintingPodSchedulerSaveData();
    public DayNightCycleSaveData dayNightCycle =
        new DayNightCycleSaveData();
    public CameraSaveData camera = new CameraSaveData();
}

[Serializable]
public class WorldSaveData
{
    public int width;
    public int height;
    public float cellSize;
    public float generationSeed;
    public List<TileSaveData> tiles = new List<TileSaveData>();
}

[Serializable]
public class TileSaveData
{
    public int x;
    public int y;
    public int tileType;
    public int backWallTileType;
    public int decorationTileType;
    public int fogState;
    public float liquidAmount;
}

[Serializable]
public class StructureSaveData
{
    public int x;
    public int y;
    public int tileType;
    public bool isBeingDismantled;
    public int maxCapacityPerResource;
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
    public float rawFoodDiscomfortRemaining;
    public float rawFoodWorkPenaltyPercent;
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
    public int priority;
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
