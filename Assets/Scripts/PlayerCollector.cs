using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class PlayerCollector : MonoBehaviour
{
    [Header("Refs")]
    public BackpackStack backpack;

    [Header("Pickup")]
    [Tooltip("How often try to pick an item while inside the pile trigger.")]
    public float pickupInterval = 0.08f;

    [Tooltip("Scale-out duration at the pile (seconds).")]
    public float pileScaleOutDuration = 0.15f;

    // reservations to prevent over-collecting beyond capacity
    private int _reservedSlots = 0;
    // (optional) quick set to ignore same item twice
    private readonly HashSet<StackItem> _reservedItems = new HashSet<StackItem>();

    private float _timer;

    private void Reset()
    {
        var col = GetComponent<Collider>();
        col.isTrigger = true;
    }

    private void OnTriggerStay(Collider other)
    {
        if (!backpack) return;

        // current available considering reservations
        int freeSlots = Mathf.Max(0, backpack.capacity - (backpack.Count + _reservedSlots));
        if (freeSlots <= 0) return;

        var item = other.GetComponent<StackItem>();
        if (!item) item = other.GetComponentInParent<StackItem>();
        if (!item) return;

        // already carried?
        if (item.transform.IsChildOf(backpack.anchor)) return;

        // already reserved by us?
        if (_reservedItems.Contains(item)) return;

        _timer += Time.deltaTime;
        if (_timer < pickupInterval) return;
        _timer = 0f;

        // reserve slot and lock item immediately
        _reservedItems.Add(item);
        _reservedSlots++;

        // disable colliders right away so физика/другие триггеры не схватили ещё раз
        foreach (var c in item.GetComponentsInChildren<Collider>()) c.enabled = false;

        StartCoroutine(PickOne(item));
    }

    private IEnumerator PickOne(StackItem item)
    {
        if (!item || !backpack)
        {
            Unreserve(item, reenableColliders: true);
            yield break;
        }

        int freeSlots = Mathf.Max(0, backpack.capacity - (backpack.Count));
        if (freeSlots <= 0)
        {
            Unreserve(item, reenableColliders: true);
            yield break;
        }

        yield return backpack.TakeFromWorld(item, pileScaleOutDuration, backpack.scaleDuration);

        Unreserve(item, reenableColliders: false);
    }

    private void Unreserve(StackItem item, bool reenableColliders)
    {
        if (_reservedSlots > 0) _reservedSlots--;
        if (item) _reservedItems.Remove(item);

        if (reenableColliders && item)
        {
            foreach (var c in item.GetComponentsInChildren<Collider>()) c.enabled = true;
        }
    }
}
