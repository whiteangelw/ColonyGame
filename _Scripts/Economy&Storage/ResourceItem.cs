using UnityEngine;

[RequireComponent(typeof(SpriteRenderer), typeof(Rigidbody2D), typeof(BoxCollider2D))]
public class ResourceItem : MonoBehaviour
{
    public ResourceType type;
    public int amount = 1;

    private Rigidbody2D rb;
    private BoxCollider2D itemCollider;
    private SpriteRenderer spriteRenderer;
    private GridManager gridManager;
    private bool hasQueuedAfterSettling;
    private DuplicantTaskRunner reservedBy;
    private float nextGroundCheckTime;
    private Vector3 previousPhysicsPosition;

    private const float GroundCheckInterval = 0.1f;
    private const float MaximumFallSpeed = 20f;

    public bool IsReadyForHaul => hasQueuedAfterSettling
        && amount > 0
        && gameObject.activeInHierarchy;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        itemCollider = GetComponent<BoxCollider2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();

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
        gridManager = FindFirstObjectByType<GridManager>();
    }

    public void Initialize(ResourceType resourceType, Sprite icon, int count = 1)
    {
        this.type = resourceType;
        this.amount = Mathf.Max(1, count);
        hasQueuedAfterSettling = false;
        reservedBy = null;
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

        takenAmount = Mathf.Min(requestedAmount, amount);
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
        if (runner == null || !IsReadyForHaul)
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

    public void Recycle()
    {
        if (!gameObject.activeInHierarchy)
        {
            return;
        }

        amount = 0;
        reservedBy = null;

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
