using UnityEngine;

public static class WorldClickInteractionResolver
{
    public static bool TryInteract(
        Camera camera,
        Vector2 screenPosition,
        Vector2 worldPosition,
        Vector2Int gridPosition,
        float tolerance,
        bool logDiagnostics)
    {
        // Estruturas possuem um registro autoritativo. Testar os Bounds
        // diretamente evita diferenças entre consultas de ponto, triggers e
        // configurações globais do Physics 2D.
        if (TryInteractWithRegisteredStructureBounds(
                worldPosition,
                logDiagnostics))
        {
            return true;
        }

        // O raio começa na câmera, fora dos colliders 2D, e atravessa o eixo Z.
        // Isso evita depender do comportamento de OverlapPointAll quando a
        // consulta nasce dentro da forma.
        if (camera != null)
        {
            Ray pointerRay = camera.ScreenPointToRay(screenPosition);
            RaycastHit2D[] exactHits = Physics2D.GetRayIntersectionAll(
                pointerRay,
                Mathf.Infinity);
            if (TryInteractWithRayHits(
                    exactHits,
                    "raio exato",
                    logDiagnostics))
            {
                return true;
            }
        }

        // O grid é determinístico e entende footprints de estruturas.
        if (TryInteractWithGrid(gridPosition, logDiagnostics))
        {
            return true;
        }

        // Pequena tolerância para sprites maiores que o collider ou tile-base.
        if (tolerance > 0f)
        {
            Collider2D[] nearbyHits = Physics2D.OverlapCircleAll(
                worldPosition,
                tolerance);
            if (TryInteractWithColliders(
                    nearbyHits,
                    worldPosition,
                    "tolerância",
                    logDiagnostics))
            {
                return true;
            }
        }

        if (logDiagnostics)
        {
            Debug.Log(
                $"[WorldClick] Nenhum IInteractable em "
                    + $"world={worldPosition}, grid={gridPosition}.");
        }

        return false;
    }

    private static bool TryInteractWithRegisteredStructureBounds(
        Vector2 worldPosition,
        bool logDiagnostics)
    {
        StructureManager structures = StructureManager.Instance;
        if (structures == null) return false;

        StorageStructure bestStorage = null;
        float bestStorageDistance = float.PositiveInfinity;
        foreach (StorageStructure storage in structures.GetRegisteredStorages())
        {
            if (!TryGetContainingBoundsDistance(
                    storage,
                    worldPosition,
                    out float distance)
                || distance >= bestStorageDistance)
            {
                continue;
            }

            bestStorage = storage;
            bestStorageDistance = distance;
        }

        if (bestStorage != null)
        {
            Interact(
                bestStorage,
                "bounds registrados/baú",
                logDiagnostics,
                bestStorage);
            return true;
        }

        ConfiguredStructure bestStructure = null;
        float bestStructureDistance = float.PositiveInfinity;
        foreach (ConfiguredStructure structure in
                 structures.GetRegisteredConfiguredStructures())
        {
            if (!TryGetContainingBoundsDistance(
                    structure,
                    worldPosition,
                    out float distance)
                || distance >= bestStructureDistance)
            {
                continue;
            }

            bestStructure = structure;
            bestStructureDistance = distance;
        }

        if (bestStructure == null) return false;

        Interact(
            bestStructure,
            "bounds registrados/estrutura",
            logDiagnostics,
            bestStructure);
        return true;
    }

    private static bool TryGetContainingBoundsDistance(
        Component root,
        Vector2 worldPosition,
        out float distance)
    {
        distance = float.PositiveInfinity;
        if (root == null || !root.gameObject.activeInHierarchy) return false;

        bool containsPoint = false;
        Collider2D[] colliders = root.GetComponentsInChildren<Collider2D>(true);
        foreach (Collider2D collider in colliders)
        {
            if (collider == null || !collider.enabled) continue;

            Bounds bounds = collider.bounds;
            if (worldPosition.x < bounds.min.x
                || worldPosition.x > bounds.max.x
                || worldPosition.y < bounds.min.y
                || worldPosition.y > bounds.max.y)
            {
                continue;
            }

            containsPoint = true;
            Vector2 center = new Vector2(bounds.center.x, bounds.center.y);
            distance = Mathf.Min(
                distance,
                (center - worldPosition).sqrMagnitude);
        }

        return containsPoint;
    }

    private static bool TryInteractWithRayHits(
        RaycastHit2D[] hits,
        string source,
        bool logDiagnostics)
    {
        foreach (RaycastHit2D hit in hits)
        {
            StorageStructure storage =
                hit.collider?.GetComponentInParent<StorageStructure>();
            if (storage == null) continue;

            Interact(storage, source, logDiagnostics, storage);
            return true;
        }

        foreach (RaycastHit2D hit in hits)
        {
            Collider2D collider = hit.collider;
            if (collider == null) continue;

            IInteractable interactable = ResolveInteractable(collider);
            if (interactable == null) continue;

            Interact(
                interactable,
                source,
                logDiagnostics,
                interactable as Component);
            return true;
        }

        return false;
    }

    private static bool TryInteractWithGrid(
        Vector2Int gridPosition,
        bool logDiagnostics)
    {
        StructureManager structures = StructureManager.Instance;
        if (structures == null) return false;

        StorageStructure storage = structures.GetStorageAt(gridPosition);
        if (storage != null)
        {
            Interact(storage, "grid/baú", logDiagnostics);
            return true;
        }

        ConfiguredStructure configured =
            structures.GetConfiguredStructureAt(gridPosition);
        if (configured == null) return false;

        Interact(configured, "grid/estrutura", logDiagnostics);
        return true;
    }

    private static bool TryInteractWithColliders(
        Collider2D[] colliders,
        Vector2 worldPosition,
        string source,
        bool logDiagnostics)
    {
        IInteractable bestInteractable = null;
        Component bestComponent = null;
        float bestDistance = float.PositiveInfinity;

        foreach (Collider2D collider in colliders)
        {
            if (collider == null) continue;

            IInteractable interactable = ResolveInteractable(collider);
            if (interactable == null) continue;

            Vector2 closestPoint = collider.ClosestPoint(worldPosition);
            float distance = (closestPoint - worldPosition).sqrMagnitude;
            if (distance >= bestDistance) continue;

            bestDistance = distance;
            bestInteractable = interactable;
            bestComponent = interactable as Component;
        }

        if (bestInteractable == null) return false;

        Interact(bestInteractable, source, logDiagnostics, bestComponent);
        return true;
    }

    private static IInteractable ResolveInteractable(Collider2D collider)
    {
        // Baús ficam explícitos porque também podem estar em uma estrutura
        // configurada no mesmo GameObject.
        StorageStructure storage =
            collider.GetComponentInParent<StorageStructure>();
        if (storage != null) return storage;

        MonoBehaviour[] behaviours =
            collider.GetComponentsInParent<MonoBehaviour>(true);
        foreach (MonoBehaviour behaviour in behaviours)
        {
            if (behaviour is IInteractable interactable)
            {
                return interactable;
            }
        }

        return null;
    }

    private static void Interact(
        IInteractable interactable,
        string source,
        bool logDiagnostics,
        Component component = null)
    {
        if (logDiagnostics)
        {
            component ??= interactable as Component;
            string targetName = component != null
                ? component.gameObject.name
                : interactable.GetType().Name;
            Debug.Log(
                $"[WorldClick] {source} -> {targetName} "
                    + $"({interactable.GetType().Name}).",
                component);
        }

        interactable.OnInteract();
    }
}
