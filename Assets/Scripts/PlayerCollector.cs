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

        var item = other.GetComponent<StackItem>() ?? other.GetComponentInParent<StackItem>();
        if (!item) return;

        // ��� ���� ���� �������? ����������
        if (item.transform.IsChildOf(backpack.anchor)) return;
        if (_reservedItems.Contains(item)) return;

        // ���� �� ����� � ������ ������?
        if (!backpack.HasFreeSlot) return;

        _timer += Time.deltaTime;
        if (_timer < pickupInterval) return;
        _timer = 0f;

        // ��������� ���� � ���� ��� ������
        int reservedIndex = backpack.ReserveSlot();
        if (reservedIndex < 0) return;

        _reservedItems.Add(item);
        // ��������� ����������, ����� ������� ������ �� �������
        foreach (var c in item.GetComponentsInChildren<Collider>()) c.enabled = false;
        DisablePhysicsForPickup(item);

        StartCoroutine(PickOne(item, reservedIndex));
    }

    private IEnumerator PickOne(StackItem item, int reservedIndex)
    {
        if (!item || !backpack)
        {
            if (backpack != null) backpack.CancelReserve();
            _reservedItems.Remove(item);
            if (item) foreach (var c in item.GetComponentsInChildren<Collider>()) c.enabled = true;
            yield break;
        }

        // 1) scale-out � ����
        yield return item.ScaleRoutine(item.transform.localScale, Vector3.zero, pileScaleOutDuration);

        // 2) ����� ������ ��� ����� �� ���� ����������������� �������
        item.transform.SetParent(backpack.anchor, false);
        item.transform.localRotation = Quaternion.identity;
        item.transform.localPosition = backpack.LocalPosForIndex(reservedIndex);
        item.transform.localScale = Vector3.zero;

        // 3) scale-in �� carriedScale
        item.scaleCurve = backpack.scaleCurve;
        yield return item.ScaleRoutine(Vector3.zero, backpack.carriedScale, backpack.scaleDuration);

        // 4) ������������ ���������� (������� ����� � ������ ��������� ���������)
        backpack.AddReserved(item);

        _reservedItems.Remove(item);
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
    private void DisablePhysicsForPickup(StackItem item)
    {
        foreach (var rb in item.GetComponentsInChildren<Rigidbody>())
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.useGravity = false;
            rb.isKinematic = true;
        }
        foreach (var c in item.GetComponentsInChildren<Collider>())
            c.enabled = false;
    }

    private void RestorePhysicsIfCanceled(StackItem item)
    {
        foreach (var rb in item.GetComponentsInChildren<Rigidbody>())
        {
            rb.isKinematic = false;
            rb.useGravity = true;
        }
        foreach (var c in item.GetComponentsInChildren<Collider>())
            c.enabled = true;
    }

}
