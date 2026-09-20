using System;

public sealed class TaskGridSubscription
{
    private GridManager subscribedGrid;

    public void Refresh(
        Action<int, int, TileType> onTileChanged,
        Action onGridRebuilt)
    {
        if (subscribedGrid == GridManager.Instance)
        {
            return;
        }

        Disconnect(onTileChanged, onGridRebuilt);

        subscribedGrid = GridManager.Instance;

        if (subscribedGrid == null)
        {
            return;
        }

        subscribedGrid.OnTileChanged += onTileChanged;
        subscribedGrid.OnGridRebuilt += onGridRebuilt;
    }

    public void Disconnect(
        Action<int, int, TileType> onTileChanged,
        Action onGridRebuilt)
    {
        if (subscribedGrid == null)
        {
            return;
        }

        subscribedGrid.OnTileChanged -= onTileChanged;
        subscribedGrid.OnGridRebuilt -= onGridRebuilt;

        subscribedGrid = null;
    }
}