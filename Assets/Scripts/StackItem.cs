using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public class StackItem : MonoBehaviour
{
    [Header("Scale Animation")]
    [Tooltip("Default duration for scale animations.")]
    public float scaleDuration = 0.20f;

    [Tooltip("Curve for scale animations (0..1).")]
    public AnimationCurve scaleCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Move Animation")]
    [Tooltip("Default duration for move-to-target.")]
    public float moveDuration = 0.25f;

    [Tooltip("Local jump arc height (0 = straight move).")]
    public float arc = 0.35f;

    [Tooltip("Zero-out local rotation on arrive.")]
    public bool freezeRotationOnArrive = true;

    private Coroutine _running;

    /// <summary>Stop current animation if any.</summary>
    public void StopAnim()
    {
        if (_running != null)
        {
            StopCoroutine(_running);
            _running = null;
        }
    }

    /// <summary>Animate local scale from 'from' to 'to'.</summary>
    public IEnumerator ScaleRoutine(Vector3 from, Vector3 to, float duration)
    {
        if (duration <= 0f)
        {
            transform.localScale = to;
            yield break;
        }

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / duration);
            float e = scaleCurve.Evaluate(k);
            transform.localScale = Vector3.LerpUnclamped(from, to, e);
            yield return null;
        }
        transform.localScale = to;
    }

    /// <summary>World-space scale out to zero and then destroy this GameObject.</summary>
    public IEnumerator ScaleOutAndDestroy(float duration)
    {
        // keep world pose intact while scaling: do it in local, it's okay visually
        Vector3 start = transform.localScale;
        yield return ScaleRoutine(start, Vector3.zero, duration);
        Destroy(gameObject);
    }

    /// <summary>
    /// Move this item under 'parent', animating in LOCAL space from its current local pose to Vector3.zero,
    /// simulating a small jump by adding an upward offset (arc).
    /// </summary>
    public IEnumerator MoveToParent(Transform parent, float duration, float jumpArc, bool zeroLocalRotation = true)
    {
        // cache current world pose
        Vector3 worldPos = transform.position;
        Quaternion worldRot = transform.rotation;

        // compute local pose relative to the target parent
        Vector3 localStartPos = parent.InverseTransformPoint(worldPos);
        Quaternion localStartRot = Quaternion.Inverse(parent.rotation) * worldRot;

        // parent while preserving world pose by applying computed local pose
        transform.SetParent(parent, false);
        transform.localPosition = localStartPos;
        transform.localRotation = localStartRot;

        // animate to zero in local-space with optional arc
        Vector3 from = localStartPos;
        Vector3 to = Vector3.zero;

        if (duration <= 0f)
        {
            transform.localPosition = to;
            if (zeroLocalRotation) transform.localRotation = Quaternion.identity;
            yield break;
        }

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / duration);
            float e = scaleCurve.Evaluate(k);

            // parabolic arc on Y
            float yArc = jumpArc > 0f ? Mathf.Sin(k * Mathf.PI) * jumpArc : 0f;

            Vector3 p = Vector3.LerpUnclamped(from, to, e);
            p.y += yArc;

            transform.localPosition = p;

            if (zeroLocalRotation)
            {
                // smooth rotate to identity using the same curve
                transform.localRotation = Quaternion.Slerp(localStartRot, Quaternion.identity, e);
            }

            yield return null;
        }

        transform.localPosition = to;
        if (zeroLocalRotation) transform.localRotation = Quaternion.identity;
    }

    /// <summary>
    /// Helper: scale to zero (world), reparent, then scale in (local) to target scale.
    /// </summary>
    public IEnumerator PickupScaleSwap(Transform newParent, Vector3 carriedLocalScale, float outDuration, float inDuration, float jumpArc = 0f)
    {
        // scale-out in place
        yield return ScaleRoutine(transform.localScale, Vector3.zero, outDuration);

        // reparent and place at local zero
        yield return MoveToParent(newParent, 0f, 0f, true);

        // scale-in to carried scale
        transform.localScale = Vector3.zero;
        yield return ScaleRoutine(Vector3.zero, carriedLocalScale, inDuration);
    }

    private void OnDisable() => StopAnim();
}
