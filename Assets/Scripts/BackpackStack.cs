using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Holds items behind the player (stack). 
/// Manages capacity, layout and scale animations.
/// </summary>
public class BackpackStack : MonoBehaviour
{
    [Header("Anchor & Layout")]
    [Tooltip("Point behind the player where items will be stacked.")]
    public Transform anchor;
    [Tooltip("Vertical spacing between stacked items.")]
    public float verticalSpacing = 0.15f;
    [Tooltip("Maximum number of items allowed in stack.")]
    public int capacity = 20;

    [Header("Carried Visuals")]
    [Tooltip("Target scale for carried items.")]
    public Vector3 carriedScale = Vector3.one * 0.75f;
    [Tooltip("Duration of scale animations when adding/removing.")]
    public float scaleDuration = 0.2f;
    [Tooltip("Curve for scale animations.")]
    public AnimationCurve scaleCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    // Runtime storage
    private readonly List<StackItem> _items = new();

    public int Count => _items.Count;
    public bool IsFull => _items.Count >= capacity;
    // Внутри BackpackStack
    private int _reservedSlots = 0;

    // Есть ли свободное место с учётом уже зарезервированных, но ещё не добавленных?
    public bool HasFreeSlot => (Count + _reservedSlots) < capacity;

    // Забронировать слот. Возвращает зарезервированный индекс (0..), либо -1 если нет места.
    public int ReserveSlot()
    {
        if (!HasFreeSlot) return -1;
        _reservedSlots++;
        // индекс, на который встанет предмет, пока он «в пути»
        return Count + _reservedSlots - 1;
    }

    // Отменить бронь (если что-то пошло не так)
    public void CancelReserve()
    {
        if (_reservedSlots > 0) _reservedSlots--;
    }

    // Подтвердить добавление уже поставленного предмета (снимает одну бронь)
    public void AddReserved(StackItem item)
    {
        if (!item) return;
        _items.Add(item);
        if (_reservedSlots > 0) _reservedSlots--;
        Relayout(); // финальная нормализация
    }

    // Удобство: посчитать локальную позицию по индексу
    public Vector3 LocalPosForIndex(int index) => new Vector3(0f, verticalSpacing * index, 0f);

    private void OnValidate()
    {
        if (capacity < 0) capacity = 0; // clamp to non-negative
    }

    /// <summary>
    /// Add item from world into backpack with animation.
    /// </summary>
    public IEnumerator TakeFromWorld(StackItem item, float worldOutDuration, float inDuration)
    {
        if (IsFull || !item) yield break;

        TogglePhysics(item, false);

        // Scale-out at current position
        yield return item.ScaleRoutine(item.transform.localScale, Vector3.zero, worldOutDuration);

        // Parent to anchor
        item.transform.SetParent(anchor, false);

        // Place at correct local slot position
        int index = _items.Count;
        item.transform.localPosition = new Vector3(0f, verticalSpacing * index, 0f);
        item.transform.localRotation = Quaternion.identity;
        item.transform.localScale = Vector3.zero;

        // Scale-in
        item.scaleCurve = scaleCurve;
        yield return item.ScaleRoutine(Vector3.zero, carriedScale, inDuration);

        _items.Add(item);
        Relayout();
    }

    /// <summary>
    /// Remove and return up to 'count' items from top.
    /// </summary>
    public List<StackItem> PopMany(int count)
    {
        int n = Mathf.Min(count, _items.Count);
        var result = _items.GetRange(_items.Count - n, n);
        _items.RemoveRange(_items.Count - n, n);
        Relayout();
        return result;
    }

    /// <summary>
    /// Remove and return all items.
    /// </summary>
    public List<StackItem> PopAll()
    {
        var result = new List<StackItem>(_items);
        _items.Clear();
        return result;
    }

    /// <summary>
    /// Scale-out carried item and destroy it (e.g., consumed by station).
    /// </summary>
    public IEnumerator ScaleOutAndDestroy(StackItem item)
    {
        if (!item) yield break;
        yield return item.ScaleRoutine(item.transform.localScale, Vector3.zero, scaleDuration);
        Destroy(item.gameObject);
    }

    /// <summary>
    /// Re-align all items vertically behind the anchor.
    /// </summary>
    private void Relayout()
    {
        for (int i = 0; i < _items.Count; i++)
        {
            var t = _items[i].transform;
            t.SetParent(anchor, false);
            t.localPosition = new Vector3(0f, verticalSpacing * i, 0f);
            t.localRotation = Quaternion.identity;
            t.localScale = carriedScale;
        }
    }
    // Возврат предметов на верх стопки в исходном порядке (0-й станет нижним из возвращаемых)
    public void PushBackTop(List<StackItem> items)
    {
        if (items == null || items.Count == 0) return;
        foreach (var it in items)
        {
            if (!it) continue;
            // родительство и нормализация позы под якорь
            it.transform.SetParent(anchor, false);
            it.transform.localRotation = Quaternion.identity;
            it.transform.localScale = carriedScale;
            _items.Add(it);
        }
        // разложить по высоте заново
        for (int i = 0; i < _items.Count; i++)
        {
            var t = _items[i].transform;
            t.localPosition = new Vector3(0f, verticalSpacing * i, 0f);
        }
    }

    /// <summary>
    /// Enable/disable physics for item when carried.
    /// </summary>
    private void TogglePhysics(StackItem item, bool enable)
    {
        foreach (var rb in item.GetComponentsInChildren<Rigidbody>())
            rb.isKinematic = !enable;
        foreach (var c in item.GetComponentsInChildren<Collider>())
            c.enabled = enable;
    }
    // Returns the last collected item (top of the stack), or null if empty.
    public StackItem PopTop()
    {
        if (_items.Count == 0) return null;
        int last = _items.Count - 1;
        var it = _items[last];
        _items.RemoveAt(last);
        Relayout(); // keep layout consistent after removal
        return it;
    }

}
