using UnityEngine;

public class BackpackFollowerSmooth : MonoBehaviour
{
    [Header("Target")]
    public Transform followTarget;
    [Tooltip("Offset in target's local space.")]
    public Vector3 localOffset = new Vector3(0f, 0.3f, -0.25f);

    [Header("Smoothing")]
    [Tooltip("Time for SmoothDamp position (smaller = быстрее, 0.08–0.18 обычно отлично).")]
    public float posSmoothTime = 0.12f;
    [Tooltip("Degrees per second for rotation slerp (360 = очень быстро).")]
    public float rotSmoothSpeed = 540f;
    [Tooltip("Максимальная скорость перемещения якоря (м/с). 0 = без лимита.")]
    public float maxSpeed = 0f;

    [Header("Update mode")]
    [Tooltip("Auto: если у цели есть Rigidbody — использовать FixedUpdate, иначе LateUpdate.")]
    public bool autoMatchFixedUpdateToRigidbody = true;

    Vector3 _vel; // SmoothDamp velocity
    Rigidbody _targetRb;

    void Awake()
    {
        if (autoMatchFixedUpdateToRigidbody && followTarget)
            _targetRb = followTarget.GetComponentInParent<Rigidbody>();
    }

    void FixedUpdate()
    {
        if (ShouldUseFixed()) Follow(Time.fixedDeltaTime);
    }

    void LateUpdate()
    {
        if (!ShouldUseFixed()) Follow(Time.deltaTime);
    }

    bool ShouldUseFixed() => autoMatchFixedUpdateToRigidbody && _targetRb != null;

    void Follow(float dt)
    {
        if (!followTarget) return;

        // desired pose (world)
        Vector3 desiredPos = followTarget.TransformPoint(localOffset);
        Quaternion desiredRot = followTarget.rotation;

        // smooth position
        float maxSpd = maxSpeed > 0f ? maxSpeed : Mathf.Infinity;
        Vector3 nextPos = Vector3.SmoothDamp(transform.position, desiredPos, ref _vel, posSmoothTime, maxSpd, dt);
        transform.position = nextPos;

        // smooth rotation
        if (rotSmoothSpeed <= 0f)
            transform.rotation = desiredRot;
        else
        {
            float t = Mathf.Clamp01(rotSmoothSpeed * Mathf.Deg2Rad * dt); // нормализация
            transform.rotation = Quaternion.Slerp(transform.rotation, desiredRot, t);
        }

        // гарантируем единичный скейл
        if (transform.localScale != Vector3.one) transform.localScale = Vector3.one;
    }
}
