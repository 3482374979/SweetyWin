using System.Collections.Generic;

namespace SweetyWin.Translation;

/// <summary>
/// (v0.2.0) Simple thread-safe LRU cache — LinkedList(order) + Dictionary(O(1) lookup).
/// Translation 결과 캐싱 — 같은 텍스트 반복 번역 시 API 호출 0회.
/// </summary>
internal sealed class LruCache<TKey, TValue> where TKey : notnull
{
    private readonly int _capacity;
    private readonly LinkedList<KeyValuePair<TKey, TValue>> _list = new();
    private readonly Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>> _map = new();
    private readonly object _sync = new();

    public LruCache(int capacity) { _capacity = capacity; }

    public bool TryGet(TKey key, out TValue value)
    {
        lock (_sync)
        {
            if (_map.TryGetValue(key, out var node))
            {
                // Move to head — most recently used
                _list.Remove(node);
                _list.AddFirst(node);
                value = node.Value.Value;
                return true;
            }
            value = default!;
            return false;
        }
    }

    public void Put(TKey key, TValue value)
    {
        lock (_sync)
        {
            if (_map.TryGetValue(key, out var existing))
            {
                _list.Remove(existing);
                _map.Remove(key);
            }

            var node = new LinkedListNode<KeyValuePair<TKey, TValue>>(
                new KeyValuePair<TKey, TValue>(key, value));
            _list.AddFirst(node);
            _map[key] = node;

            // Evict tail if over capacity
            while (_list.Count > _capacity)
            {
                var tail = _list.Last;
                if (tail == null) break;
                _list.RemoveLast();
                _map.Remove(tail.Value.Key);
            }
        }
    }

    public int Count
    {
        get { lock (_sync) return _list.Count; }
    }
}
