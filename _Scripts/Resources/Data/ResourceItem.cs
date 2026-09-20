using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(SpriteRenderer), typeof(Rigidbody2D), typeof(BoxCollider2D))]
public class ResourceItem : MonoBehaviour, IFoodSource
{
    private static readonly HashSet<ResourceItem> activeItems =
        new HashSet<ResourceItem>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetActiveItemRegistry()
    {
        activeItems.Clear();
    }

    public ResourceType type;
    public int amount = 1;

    private Rigidbody2D rb;
    private BoxCollider2D itemCollider;
    private SpriteRenderer spriteRenderer;
    private GridManager gridManager;
    private bool hasQueuedAfterSettling;
    private DuplicantTaskRunner reservedBy;
    private DuplicantController foodReservedBy;
    private int reservedFoodPortions;
    private CreatureFeeding creatureFoodReservedBy;
    private int creatureReservedPortions;
    private float nextGroundCheckTime;
    private Vector3 previousPhysicsPosition;

    private const float GroundCheckInterval = 0.1f;
    private const float MaximumFallSpeed = 20f;

    public bool IsReadyForHaul => hasQueuedAfterSettling
        && amount > 0
        && foodReservedBy == null
        && creatureFoodReservedBy == null
        && gameObject.activeInHierarchy;
    public Vector2Int GridPosition => gridManager != null
        ? gridManager.WorldToGridPosition(transform.position)
        : Vector2Int.zero;
    public FoodSourceKind SourceKind => FoodSourceKind.GroundItem;
    public bool IsEmergencyOnly
    {
        get
        {
            ItemDataSO data = GetItemData();
            return data == null || !data.allowPreventiveConsumption;
        }
    }
    public bool HasFood
    {
        get
        {
            ItemDataSO data = GetItemData();
            return hasQueuedAfterSettling
                && gameObject.activeInHierarchy
                && reservedBy == null
                && amount - reservedFoodPortions - creatureReservedPortions > 0
                && data != null
                && data.isFood;
        }
    }

    private void OnEnable()
    {
        activeItems.Add(this);
        PerformanceMetricsService.ChangeActiveResourceItems(1);
        FoodSourceRegistry.Instance?.Register(this);
    }

    private void OnDisable()
    {
        activeItems.Remove(this);
        PerformanceMetricsService.ChangeActiveResourceItems(-1);
        FoodSourceRegistry.Instance?.Unregister(this);
        foodReservedBy = null;
        reservedFoodPortions = 0;
        creatureFoodReservedBy = null;
        creatureReservedPortions = 0;
    }

    public static void CopyActiveItemsTo(List<ResourceItem> destination)
    {
        if (destination == null) return;

        destination.Clear();
        foreach (ResourceItem item in activeItems)
        {
            if (item != null && item.gameObject.activeInHierarchy)
            {
                destination.Add(item);
            }
        }
    }

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        itemCollider = GetComponent<BoxCollider2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        gridManager = GridManager.Instance != null
            ? GridManager.Instance
            : FindFirstObjectByType<GridManager>();
        if (GridManager.Instance == null)
        {
            PerformanceMetricsService.RecordGlobalObjectSearch();
        }

        rb.gravityScale = 1.5f;
        rb.freezeRotation = true;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;

        // A queda e o apoio são resolvidos pelo Grid.
        // O trigger impede itens de formarem pilhas suspensas entre si.
        itemCollider.isTrigger = true;

        previousPhysicsPosition = transform.position;
    }

    private void Start()
    {
        if (gridManager == null)
        {
            PerformanceMetricsService.RecordGlobalObjectSearch();
            gridManager = FindFirstObjectByType<GridManager>();
        }
    }

    public void Initialize(ResourceType resourceType, Sprite icon, int count = 1)
    {
        this.type = resourceType;
        this.amount = Mathf.Max(1, count);
        hasQueuedAfterSettling = false;
        reservedBy = null;
        foodReservedBy = null;
        reservedFoodPortions = 0;
        creatureFoodReservedBy = null;
        creatureReservedPortions = 0;
        nextGroundCheckTime = Time.time + Random.Range(0f, GroundCheckInterval);
        previousPhysicsPosition = transform.position;

        if (rb != null)
        {
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }

        if (itemCollider != null)
        {
            itemCollider.isTrigger = true;
        }

        if (icon != null && spriteRenderer != null)
        {
            spriteRenderer.sprite = icon;
        }

        // Reduzida a força do impulso para evitar atravessar a física do tilemap
        Vector2 force = new Vector2(Random.Range(-0.5f, 0.5f), Random.Range(0.8f, 1.8f));
        rb.AddForce(force, ForceMode2D.Impulse);
    }

    public void InitializeRestored(
        ResourceType resourceType,
        Sprite icon,
        int count)
    {
        type = resourceType;
        amount = Mathf.Max(1, count);
        hasQueuedAfterSettling = true;
        reservedBy = null;
        foodReservedBy = null;
        reservedFoodPortions = 0;
        creatureFoodReservedBy = null;
        creatureReservedPortions = 0;
        nextGroundCheckTime = Time.time + GroundCheckInterval;
        previousPhysicsPosition = transform.position;

        if (icon != null && spriteRenderer != null)
        {
            spriteRenderer.sprite = icon;
        }

        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
            rb.bodyType = RigidbodyType2D.Kinematic;
        }
    }

    public bool TryTake(int requestedAmount, out int takenAmount)
    {
        takenAmount = 0;

        if (requestedAmount <= 0
            || amount <= 0
            || !gameObject.activeInHierarchy)
        {
            return false;
        }

        int available = amount - reservedFoodPortions - creatureReservedPortions;
        takenAmount = Mathf.Min(requestedAmount, Mathf.Max(0, available));
        amount -= takenAmount;

        return takenAmount > 0;
    }

    public void ReturnAmount(int returnedAmount)
    {
        if (returnedAmount <= 0) return;

        amount += returnedAmount;
        StockpileManager.Instance?.RequestRefresh();
    }

    public bool TryReserve(DuplicantTaskRunner runner)
    {
        if (runner == null || !IsReadyForHaul || foodReservedBy != null
            || creatureFoodReservedBy != null)
        {
            return false;
        }

        if (reservedBy != null && reservedBy != runner)
        {
            return false;
        }

        reservedBy = runner;
        return true;
    }

    public void ReleaseReservation(DuplicantTaskRunner runner)
    {
        if (reservedBy == runner)
        {
            reservedBy = null;
        }
    }

    public void CollectFoodOptions(List<FoodOption> results)
    {
        ItemDataSO data = GetItemData();
        int available = amount - reservedFoodPortions - creatureReservedPortions;
        if (results == null || !HasFood || data == null || available <= 0)
        {
            return;
        }

        results.Add(new FoodOption
        {
            resourceType = type,
            availablePortions = available,
            hungerRestoredPerPortion = data.hungerRestored,
            isRawFood = data.isRawFood,
            allowPreventiveConsumption =
                data.allowPreventiveConsumption,
            quality = data.foodQuality
        });
    }

    public bool TryReserveMeal(
        DuplicantController duplicant,
        ResourceType foodType,
        int requestedPortions,
        out int reservedAmount)
    {
        reservedAmount = 0;
        if (duplicant == null || foodType != type || requestedPortions <= 0
            || reservedBy != null
            || creatureFoodReservedBy != null
            || (foodReservedBy != null && foodReservedBy != duplicant))
        {
            return false;
        }

        if (foodReservedBy == duplicant)
        {
            reservedAmount = reservedFoodPortions;
            return reservedAmount > 0;
        }

        ItemDataSO data = GetItemData();
        if (!IsReadyForHaul || data == null || !data.isFood) return false;

        reservedAmount = Mathf.Min(requestedPortions, amount);
        if (reservedAmount <= 0) return false;

        foodReservedBy = duplicant;
        reservedFoodPortions = reservedAmount;
        return true;
    }

    public int GetReservedPortionCount(DuplicantController duplicant)
    {
        return foodReservedBy == duplicant ? reservedFoodPortions : 0;
    }

    public bool TryConsumeReservedPortion(
        DuplicantController duplicant,
        out float hungerRestored,
        out bool isRawFood)
    {
        hungerRestored = 0f;
        isRawFood = false;
        ItemDataSO data = GetItemData();

        if (duplicant == null || foodReservedBy != duplicant
            || reservedFoodPortions <= 0 || amount <= 0
            || data == null || !data.isFood)
        {
            return false;
        }

        amount--;
        reservedFoodPortions--;
        hungerRestored = Mathf.Max(0f, data.hungerRestored);
        isRawFood = data.isRawFood;

        if (reservedFoodPortions <= 0) foodReservedBy = null;
        StockpileManager.Instance?.RequestRefresh();

        if (amount <= 0) Recycle();
        return true;
    }

    public void ReleaseReservation(DuplicantController duplicant)
    {
        if (foodReservedBy != duplicant) return;
        foodReservedBy = null;
        reservedFoodPortions = 0;
        if (amount > 0) TaskManager.Instance?.AddHaulTask(this);
    }

    public bool CanReserveCreaturePortion(
        CreatureFeeding requester,
        ResourceType expectedType)
    {
        return requester != null
            && expectedType == type
            && gameObject.activeInHierarchy
            && hasQueuedAfterSettling
            && amount > 0
            && reservedBy == null
            && foodReservedBy == null
            && (creatureFoodReservedBy == null
                || creatureFoodReservedBy == requester);
    }

    public bool TryReserveCreaturePortion(
        CreatureFeeding requester,
        ResourceType expectedType)
    {
        if (!CanReserveCreaturePortion(requester, expectedType)) return false;

        if (creatureFoodReservedBy == requester)
            return creatureReservedPortions > 0;

        creatureFoodReservedBy = requester;
        creatureReservedPortions = 1;
        return true;
    }

    public bool TryConsumeCreatureReservedPortion(CreatureFeeding requester)
    {
        if (requester == null
            || creatureFoodReservedBy != requester
            || creatureReservedPortions <= 0
            || amount <= 0
            || !gameObject.activeInHierarchy)
        {
            return false;
        }

        amount--;
        creatureReservedPortions = 0;
        creatureFoodReservedBy = null;
        StockpileManager.Instance?.RequestRefresh();

        if (amount <= 0) Recycle();
        else TaskManager.Instance?.AddHaulTask(this);
        return true;
    }

    public void ReleaseCreatureReservation(CreatureFeeding requester)
    {
        if (creatureFoodReservedBy != requester) return;

        creatureFoodReservedBy = null;
        creatureReservedPortions = 0;
        if (amount > 0) TaskManager.Instance?.AddHaulTask(this);
    }

    private ItemDataSO GetItemData()
    {
        return ItemSpawner.Instance != null
            ? ItemSpawner.Instance.GetItemData(type)
            : null;
    }

    public void Recycle()
    {
        if (!gameObject.activeInHierarchy)
        {
            return;
        }

        amount = 0;
        reservedBy = null;
        foodReservedBy = null;
        reservedFoodPortions = 0;
        creatureFoodReservedBy = null;
        creatureReservedPortions = 0;

        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
            rb.bodyType = RigidbodyType2D.Kinematic;
        }

        if (ItemPoolManager.Instance != null)
        {
            ItemPoolManager.Instance.ReturnToPool(this);
        }
        else
        {
            gameObject.SetActive(false);
        }
    }

    private void FixedUpdate()
    {
        if (gridManager == null || rb == null) return;

        if (rb.bodyType == RigidbodyType2D.Dynamic)
        {
            LimitFallSpeed();
            ResolveCrossedSolidTiles();
            previousPhysicsPosition = transform.position;
            return;
        }

        // Itens parados só precisam confirmar periodicamente se ainda têm apoio.
        if (Time.time < nextGroundCheckTime) return;
        nextGroundCheckTime = Time.time + GroundCheckInterval;

        Vector2Int gridPos = gridManager.WorldToGridPosition(transform.position);
        Tile tileBelow = gridManager.GetTile(gridPos.x, gridPos.y - 1);

        if (!IsSolidSupport(tileBelow))
        {
            rb.bodyType = RigidbodyType2D.Dynamic;
            hasQueuedAfterSettling = false;
            previousPhysicsPosition = transform.position;
        }
    }

    private void ResolveCrossedSolidTiles()
    {
        if (rb.linearVelocity.y > 0f) return;

        Vector2Int previousGridPosition =
            gridManager.WorldToGridPosition(previousPhysicsPosition);
        Vector2Int currentGridPosition =
            gridManager.WorldToGridPosition(transform.position);

        int highestSupportY = previousGridPosition.y - 1;
        int lowestSupportY = currentGridPosition.y - 1;

        for (int supportY = highestSupportY;
             supportY >= lowestSupportY;
             supportY--)
        {
            Tile supportTile = gridManager.GetTile(
                currentGridPosition.x,
                supportY
            );

            if (!IsSolidSupport(supportTile)) continue;

            SettleOnTile(currentGridPosition.x, supportY + 1);
            return;
        }
    }

    private void SettleOnTile(int gridX, int itemGridY)
    {
        float targetX = gridX * gridManager.cellSize
            + gridManager.cellSize * 0.5f;
        float targetY = itemGridY * gridManager.cellSize + 0.2f;

        rb.linearVelocity = Vector2.zero;
        rb.bodyType = RigidbodyType2D.Kinematic;
        transform.position = new Vector3(
            targetX,
            targetY,
            transform.position.z
        );

        previousPhysicsPosition = transform.position;

        if (hasQueuedAfterSettling) return;

        hasQueuedAfterSettling = true;
        TaskManager.Instance?.AddHaulTask(this);
        StockpileManager.Instance?.RequestRefresh();
    }

    private void LimitFallSpeed()
    {
        if (rb.linearVelocity.y >= -MaximumFallSpeed) return;

        rb.linearVelocity = new Vector2(
            rb.linearVelocity.x,
            -MaximumFallSpeed
        );
    }

    private static bool IsSolidSupport(Tile tile)
    {
        return tile != null
            && tile.type != TileType.Empty
            && tile.type != TileType.Ladder;
    }
}
