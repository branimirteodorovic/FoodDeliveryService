using System.Collections;

namespace FoodDeliveryService.Modules.Orders.IntegrationTests.Abstractions;

/// <summary>
/// The collection an OpenTelemetry in-memory exporter writes into. A plain <see cref="List{T}"/>
/// works for metrics, which are only ever appended during a <c>ForceFlush</c> the test itself
/// triggers — but spans end on whatever thread ran the work (a request thread, a Quartz job, a
/// MassTransit consumer), so the trace exporter appends concurrently and a bare list would tear.
/// </summary>
internal sealed class ExportCollection<T> : ICollection<T>
{
    private readonly List<T> _items = [];
    private readonly Lock _gate = new();

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _items.Count;
            }
        }
    }

    public bool IsReadOnly => false;

    public void Add(T item)
    {
        lock (_gate)
        {
            _items.Add(item);
        }
    }

    /// <summary>A point-in-time copy, safe to enumerate while exporting continues.</summary>
    public IReadOnlyList<T> Snapshot()
    {
        lock (_gate)
        {
            return [.. _items];
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _items.Clear();
        }
    }

    public bool Contains(T item)
    {
        lock (_gate)
        {
            return _items.Contains(item);
        }
    }

    public void CopyTo(T[] array, int arrayIndex)
    {
        lock (_gate)
        {
            _items.CopyTo(array, arrayIndex);
        }
    }

    public bool Remove(T item)
    {
        lock (_gate)
        {
            return _items.Remove(item);
        }
    }

    public IEnumerator<T> GetEnumerator() => Snapshot().GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
