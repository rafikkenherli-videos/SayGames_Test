using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

[DisallowMultipleComponent]
public class ProcessingStation : MonoBehaviour
{
    [System.Serializable]
    public class GridPlatform
    {
        [Header("Root")]
        [Tooltip("Parent transform where items will be placed.")]
        public Transform root;

        [Header("Grid Size (counts per axis)")]
        [Min(1)] public int countX = 3;
        [Min(1)] public int countY = 1;
        [Min(1)] public int countZ = 3;

        [Header("Cell Spacing (local units)")]
        public float spacingX = 0.35f;
        public float spacingY = 0.20f;
        public float spacingZ = 0.35f;

        [Header("Fill Order (fastest -> slowest)")]
        public Axis fastest = Axis.X;
        public Axis middle = Axis.Z;
        public Axis slowest = Axis.Y;

        [Header("Anchor Offset (local)")]
        [Tooltip("Center offset of the whole grid in local space of 'root'.")]
        public Vector3 gridCenterOffset = Vector3.zero;

        [Header("Gizmos")]
        public bool drawGizmos = true;
        public Color gizmoColor = new Color(0, 1, 1, 0.9f);
        [Tooltip("Max cells to preview with gizmos for performance.")]
        public int gizmoMaxCells = 200;
        


        public int Capacity
        {
            get
            {
                long cap = (long)countX * countY * countZ;
                return (int)Mathf.Clamp(cap, 0, int.MaxValue);
            }
        }

        public enum Axis { X, Y, Z }

        // map logical (iX,iY,iZ) to index wise
        public Vector3 CellLocalPosition(int ix, int iy, int iz)
        {
            // compute extents centered around root + gridCenterOffset
            float width = (countX - 1) * spacingX;
            float height = (countY - 1) * spacingY;
            float depth = (countZ - 1) * spacingZ;

            float x = -width * 0.5f + ix * spacingX;
            float y = -height * 0.5f + iy * spacingY;
            float z = -depth * 0.5f + iz * spacingZ;

            return new Vector3(x, y, z) + gridCenterOffset;
        }

        // linear index -> (ix,iy,iz) according to fill order
        public void IndexToCoords(int index, out int ix, out int iy, out int iz)
        {
            // choose dims in the specified fastest/middle/slowest order
            int aCount = GetCount(fastest);
            int bCount = GetCount(middle);
            int cCount = GetCount(slowest);

            int a = index % aCount;
            int b = (index / aCount) % bCount;
            int c = (index / (aCount * bCount));

            // map back to ix/iy/iz
            ix = 0; iy = 0; iz = 0;
            SetByAxis(fastest, a, ref ix, ref iy, ref iz);
            SetByAxis(middle, b, ref ix, ref iy, ref iz);
            SetByAxis(slowest, c, ref ix, ref iy, ref iz);
        }

        public int GetCount(Axis ax) => ax == Axis.X ? countX : (ax == Axis.Y ? countY : countZ);

        void SetByAxis(Axis ax, int v, ref int ix, ref int iy, ref int iz)
        {
            switch (ax)
            {
                case Axis.X: ix = v; break;
                case Axis.Y: iy = v; break;
                case Axis.Z: iz = v; break;
            }
        }
    }

    [Header("Refs")]
    public BackpackStack playerBackpack;
    [Tooltip("Trigger that detects player arrival to start transfer.")]
    

    [Header("Platforms (Grid)")]
    public GridPlatform inputPlatform;
    public GridPlatform outputPlatform;

    [Header("Processing")]
    [Min(1)] public int batchSize = 4;
    [Min(0f)] public float processTime = 2.0f;
    [Min(1)] public int producedPerBatch = 1;
    public GameObject processedItemPrefab;

    [Header("Animations")]
    public float scaleDuration = 0.2f;
    public AnimationCurve scaleCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Output Space Policy")]
    public bool blockUntilOutputSpace = true;
    [Tooltip("0 = wait indefinitely")]
    public float outputWaitTimeout = 0f;

    [Header("UI Events")]
    public UnityEvent<int> onMoneyAdded;
    public int moneyPerBatch = 10;

    // Count-change events for simple UI binders
    public UnityEvent<int> onInputCountChanged;
    public UnityEvent<int> onOutputCountChanged;
    public bool autoStartProcessing = true;
    // runtime
    private readonly List<StackItem> _inputItems = new();
    private readonly List<GameObject> _outputItems = new();
    private bool _isProcessing;

    void Reset()
    {
        if (TryGetComponent(out Collider col)) col.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
    
        if (other.GetComponentInParent<PlayerCollector>() != null)
        {
            StartCoroutine(TransferFromBackpack());
            return;
        }

      
        var bp = other.GetComponentInParent<BackpackStack>();
        if (bp != null && bp == playerBackpack)
        {
            StartCoroutine(TransferFromBackpack());
        }
    }


    private IEnumerator TransferFromBackpack()
    {
        if (!playerBackpack || inputPlatform.root == null) yield break;

        // переносим LIFO: пока есть место на входе и есть предметы у игрока Ч забираем по одному с верха стека
        while (_inputItems.Count < inputPlatform.Capacity && playerBackpack.Count > 0)
        {
            var item = playerBackpack.PopTop(); // <-- LIFO
            if (!item) break;

            // плавно перемещаем под корень входа (короткий прыжок), Ѕ≈« scale-out из рюкзака
            item.scaleCurve = scaleCurve;
            yield return item.MoveToParent(inputPlatform.root, 0.18f, 0.15f, true);

            // ставим точно в €чейку сетки по текущему индексу
            PositionOnGrid(item.transform, inputPlatform, _inputItems.Count);

            // доводим масштаб до 1, если нужно
            if (item.transform.localScale != Vector3.one)
                yield return item.ScaleRoutine(item.transform.localScale, Vector3.one, scaleDuration * 0.6f);

            _inputItems.Add(item);
            onInputCountChanged?.Invoke(_inputItems.Count);
        }

        if (autoStartProcessing)
            TryStartProcessing();
    }


    private void TryStartProcessing()
    {
        if (_isProcessing) return;
        if (_inputItems.Count >= batchSize)
            StartCoroutine(ProcessLoop());
    }

    IEnumerator ProcessLoop()
    {
        _isProcessing = true;

        while (_inputItems.Count >= batchSize)
        {
            // 1) take batch
            var batch = _inputItems.GetRange(0, batchSize);
            _inputItems.RemoveRange(0, batchSize);

            // 2) consume
            foreach (var item in batch)
            {
                if (!item) continue;
                item.scaleCurve = scaleCurve;
                yield return item.ScaleRoutine(item.transform.localScale, Vector3.zero, scaleDuration);
                Destroy(item.gameObject);
            }
            // relayout remaining input items
            RelayoutGrid(inputPlatform, _inputItems);
            onInputCountChanged?.Invoke(_inputItems.Count);

            // 3) wait process time
            float t = 0f;
            while (t < processTime) { t += Time.deltaTime; yield return null; }

            // 4) output capacity
            int freeOut = Mathf.Max(0, outputPlatform.Capacity - _outputItems.Count);
            if (blockUntilOutputSpace)
            {
                float waited = 0f;
                while (freeOut < producedPerBatch)
                {
                    if (outputWaitTimeout > 0f && waited >= outputWaitTimeout) break;
                    waited += Time.deltaTime;
                    yield return null;
                    freeOut = Mathf.Max(0, outputPlatform.Capacity - _outputItems.Count);
                }
            }

            freeOut = Mathf.Max(0, outputPlatform.Capacity - _outputItems.Count);
            int toSpawn = Mathf.Min(producedPerBatch, freeOut);

            for (int i = 0; i < toSpawn; i++)
            {
                GameObject prod = Instantiate(processedItemPrefab, outputPlatform.root);
                PositionOnGrid(prod.transform, outputPlatform, _outputItems.Count);

                prod.transform.localScale = Vector3.zero;
                yield return ScaleRoutine(prod.transform, Vector3.zero, Vector3.one, scaleDuration);

                _outputItems.Add(prod);
                onOutputCountChanged?.Invoke(_outputItems.Count);
            }

            if (toSpawn > 0)
            {
                onMoneyAdded?.Invoke(moneyPerBatch);
            }
        }

        _isProcessing = false;
    }

    void PositionOnGrid(Transform t, GridPlatform p, int index)
    {
        p.IndexToCoords(index, out int ix, out int iy, out int iz);
        t.localPosition = p.CellLocalPosition(ix, iy, iz);
        t.localRotation = Quaternion.identity;
    }

    void RelayoutGrid(GridPlatform p, List<StackItem> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            var tr = list[i].transform;
            tr.SetParent(p.root, false);
            PositionOnGrid(tr, p, i);
            tr.localScale = Vector3.one;
        }
    }

    IEnumerator ScaleRoutine(Transform t, Vector3 from, Vector3 to, float duration)
    {
        if (duration <= 0f) { t.localScale = to; yield break; }
        float time = 0f;
        while (time < duration)
        {
            time += Time.deltaTime;
            float k = Mathf.Clamp01(time / duration);
            float e = scaleCurve.Evaluate(k);
            t.localScale = Vector3.LerpUnclamped(from, to, e);
            yield return null;
        }
        t.localScale = to;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        DrawGridGizmos(inputPlatform);
        DrawGridGizmos(outputPlatform);
    }

    void DrawGridGizmos(GridPlatform p)
    {
        if (p == null || p.root == null || !p.drawGizmos) return;
        var old = Gizmos.color;
        Gizmos.color = p.gizmoColor;

        // draw bounding box of the grid
        float width = (p.countX - 1) * p.spacingX;
        float height = (p.countY - 1) * p.spacingY;
        float depth = (p.countZ - 1) * p.spacingZ;
        Vector3 size = new Vector3(width, height, depth);
        Vector3 centerLocal = p.gridCenterOffset;
        Vector3 centerWorld = p.root.TransformPoint(centerLocal);

        // frame
        Gizmos.DrawWireCube(centerWorld, size + new Vector3(0.25f, 0.25f, 0.25f));

        // cells
        int cap = p.Capacity;
        int draw = Mathf.Min(cap, p.gizmoMaxCells);
        for (int i = 0; i < draw; i++)
        {
            p.IndexToCoords(i, out int ix, out int iy, out int iz);
            Vector3 lp = p.CellLocalPosition(ix, iy, iz);
            Vector3 wp = p.root.TransformPoint(lp);
            Gizmos.DrawWireCube(wp, new Vector3(0.2f, 0.1f, 0.2f));
        }

        Gizmos.color = old;
    }
#endif
}
