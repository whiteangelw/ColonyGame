using System;

public interface IHeapItem<T> : IComparable<T>
{
    int HeapIndex { get; set; }
}

public class MinHeap<T> where T : IHeapItem<T>
{
    private T[] items;
    private int currentItemCount;

    public MinHeap(int maxHeapSize)
    {
        items = new T[Math.Max(1, maxHeapSize)];
    }

    public int Count => currentItemCount;

    public void Add(T item)
    {
        if (currentItemCount == items.Length)
        {
            Array.Resize(ref items, items.Length * 2);
        }

        item.HeapIndex = currentItemCount;
        items[currentItemCount] = item;
        currentItemCount++;

        SortUp(item);
    }

    public T RemoveFirst()
    {
        if (currentItemCount == 0)
            return default;

        T firstItem = items[0];

        currentItemCount--;

        if (currentItemCount > 0)
        {
            T lastItem = items[currentItemCount];

            items[0] = lastItem;
            lastItem.HeapIndex = 0;

            items[currentItemCount] = default;

            SortDown(lastItem);
        }
        else
        {
            items[0] = default;
        }

        firstItem.HeapIndex = -1;

        return firstItem;
    }

    public void UpdateItem(T item)
    {
        SortUp(item);
    }

    public bool Contains(T item)
    {
        int index = item.HeapIndex;

        return index >= 0 &&
               index < currentItemCount &&
               ReferenceEquals(items[index], item);
    }

    public void Clear()
    {
        for (int i = 0; i < currentItemCount; i++)
        {
            items[i].HeapIndex = -1;
            items[i] = default;
        }

        currentItemCount = 0;
    }

    private void SortDown(T item)
    {
        while (true)
        {
            int childIndexLeft = item.HeapIndex * 2 + 1;
            int childIndexRight = childIndexLeft + 1;

            if (childIndexLeft >= currentItemCount)
                return;

            int swapIndex = childIndexLeft;

            if (childIndexRight < currentItemCount &&
                items[childIndexLeft].CompareTo(items[childIndexRight]) > 0)
            {
                swapIndex = childIndexRight;
            }

            if (item.CompareTo(items[swapIndex]) <= 0)
                return;

            Swap(item, items[swapIndex]);
        }
    }

    private void SortUp(T item)
    {
        while (item.HeapIndex > 0)
        {
            int parentIndex = (item.HeapIndex - 1) / 2;
            T parentItem = items[parentIndex];

            if (item.CompareTo(parentItem) >= 0)
                return;

            Swap(item, parentItem);
        }
    }

    private void Swap(T itemA, T itemB)
    {
        int indexA = itemA.HeapIndex;
        int indexB = itemB.HeapIndex;

        items[indexA] = itemB;
        items[indexB] = itemA;

        itemA.HeapIndex = indexB;
        itemB.HeapIndex = indexA;
    }
}
