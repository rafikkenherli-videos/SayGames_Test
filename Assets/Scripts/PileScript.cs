using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
public class PileScript : MonoBehaviour
{
    public enum Footprint { Rect, Circle }

    [Header("Item / Prefab")]
    public StackItem itemPrefab;
    public int startCount = 500;

    [Header("Spawn Root")]
    public Transform spawnRoot;

    [Header("Footprint")]
    public Footprint footprint = Footprint.Rect;
    public Vector2 rectSize = new Vector2(4f, 4f);
    public float circleRadius = 2f;

    [Header("Packing")]
    public float spacingScale = 0.95f;
    public float VerticalSpacingFactor = 0.98f;
    public float HorizontalJitter = 0.002f;
    public bool parentToRoot = true;

    [Header("Physics")]
    public bool AddRigidbody = true;
    public bool addRandomImpulse = false;
    public float impulseStrength = 0.05f;
    public float StartRotationStrenght = 0.03f;

    [Header("Container")]
    public Collider containerCollider;
    public bool syncFootprintWithContainer = true;

    [Header("Walls")]
    public float wallThickness = 0.03f;
    public int circleWallSegments = 48;

    private Vector3 itemSize;
    private float cellX;
    private float cellZ;
    private float layerH;

    private const string WallsRootName = "_PileWalls";
    private const string WallPX = "Wall_PosX";
    private const string WallNX = "Wall_NegX";
    private const string WallPZ = "Wall_PosZ";
    private const string WallNZ = "Wall_NegZ";

    private readonly List<Rigidbody> spawnedBodies = new List<Rigidbody>();

    private void Awake()
    {
        if (!spawnRoot) spawnRoot = transform;
        CacheItemMetrics();
        SyncFromContainer();
        PrepareContainer();
        EnsureWallsExist();
        UpdateWalls();
    }

    private void OnValidate()
    {
        if (startCount < 1) startCount = 1;
        if (spacingScale <= 0f) spacingScale = 0.1f;
        if (VerticalSpacingFactor <= 0.1f) VerticalSpacingFactor = 0.1f;
        if (rectSize.x < 0.01f) rectSize.x = 0.01f;
        if (rectSize.y < 0.01f) rectSize.y = 0.01f;
        if (circleRadius < 0.01f) circleRadius = 0.01f;
        if (impulseStrength < 0f) impulseStrength = 0f;
        if (StartRotationStrenght < 0f) StartRotationStrenght = 0f;
        if (wallThickness < 0.005f) wallThickness = 0.005f;
        if (circleWallSegments < 8) circleWallSegments = 8;

        if (spawnRoot) CacheItemMetrics();
        SyncFromContainer();
        PrepareContainer();
        EnsureWallsExist();
        UpdateWalls();
    }

    private void Start()
    {
        if (!itemPrefab) return;
        SpawnDenseLayers();
    }

    private void CacheItemMetrics()
    {
        var prefab = itemPrefab ? itemPrefab.gameObject : null;
        if (!prefab)
        {
            itemSize = new Vector3(0.2f, 0.1f, 0.2f);
        }
        else
        {
            Bounds b = default;
            var r = prefab.GetComponentInChildren<Renderer>();
            if (r) b = r.bounds;
            if (b.size == Vector3.zero)
            {
                var c = prefab.GetComponentInChildren<Collider>();
                if (c) b = c.bounds;
            }
            itemSize = b.size == Vector3.zero ? new Vector3(0.2f, 0.1f, 0.2f) : b.size;
        }
        cellX = Mathf.Max(0.01f, itemSize.x * spacingScale);
        cellZ = Mathf.Max(0.01f, itemSize.z * spacingScale);
        layerH = Mathf.Max(0.005f, itemSize.y * VerticalSpacingFactor);
    }

    private void SyncFromContainer()
    {
        if (!syncFootprintWithContainer || !containerCollider) return;

        if (containerCollider is BoxCollider bc)
        {
            footprint = Footprint.Rect;
            rectSize = new Vector2(Mathf.Abs(bc.size.x), Mathf.Abs(bc.size.z));
        }
        else if (containerCollider is SphereCollider sc)
        {
            footprint = Footprint.Circle;
            circleRadius = Mathf.Abs(sc.radius);
        }
        else if (containerCollider is CapsuleCollider cc && cc.direction == 1)
        {
            footprint = Footprint.Circle;
            circleRadius = Mathf.Abs(cc.radius);
        }
    }

    private void PrepareContainer()
    {
        if (!containerCollider) return;
        containerCollider.isTrigger = true;
    }

    private Transform GetWallsRoot()
    {
        if (!containerCollider) return null;
        var parent = containerCollider.transform;
        var t = parent.Find(WallsRootName);
        if (!t)
        {
            var go = new GameObject(WallsRootName);
            t = go.transform;
            t.SetParent(parent, false);
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;
        }
        return t;
    }

    private Transform EnsureChild(Transform parent, string name)
    {
        var c = parent.Find(name);
        if (!c)
        {
            var go = new GameObject(name);
            go.layer = gameObject.layer;
            c = go.transform;
            c.SetParent(parent, false);
            c.localPosition = Vector3.zero;
            c.localRotation = Quaternion.identity;
            c.localScale = Vector3.one;
            var col = go.AddComponent<BoxCollider>();
            col.isTrigger = false;
            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
        }
        return c;
    }

    private void EnsureWallsExist()
    {
        if (!containerCollider) return;
        var root = GetWallsRoot();
        if (!root) return;

        if (containerCollider is BoxCollider)
        {
            EnsureChild(root, WallPX);
            EnsureChild(root, WallNX);
            EnsureChild(root, WallPZ);
            EnsureChild(root, WallNZ);
            RemoveAllCircleSegments(root);
        }
        else
        {
            EnsureCircleSegments(root, circleWallSegments);
            RemoveRectWalls(root);
        }
    }

    private void RemoveRectWalls(Transform root)
    {
        var p = root.Find(WallPX);
        var n = root.Find(WallNX);
        var pz = root.Find(WallPZ);
        var nz = root.Find(WallNZ);
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            if (p) DestroyImmediate(p.gameObject);
            if (n) DestroyImmediate(n.gameObject);
            if (pz) DestroyImmediate(pz.gameObject);
            if (nz) DestroyImmediate(nz.gameObject);
        }
        else
        {
            if (p) Destroy(p.gameObject);
            if (n) Destroy(n.gameObject);
            if (pz) Destroy(p.gameObject);
            if (nz) Destroy(nz.gameObject);
        }
#else
        if (p) Destroy(p.gameObject);
        if (n) Destroy(n.gameObject);
        if (pz) Destroy(pz.gameObject);
        if (nz) Destroy(nz.gameObject);
#endif
    }

    private void EnsureCircleSegments(Transform root, int target)
    {
        int current = 0;
        foreach (Transform c in root) if (c.name.StartsWith("Seg_")) current++;
        if (current == target) return;

        RemoveAllCircleSegments(root);

        for (int i = 0; i < target; i++)
        {
            var go = new GameObject($"Seg_{i:D3}");
            go.layer = gameObject.layer;
            var t = go.transform;
            t.SetParent(root, false);
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;

            var col = go.AddComponent<BoxCollider>();
            col.isTrigger = false;

            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
        }
    }

    private void RemoveAllCircleSegments(Transform root)
    {
        var toRemove = new List<GameObject>();
        foreach (Transform c in root) if (c.name.StartsWith("Seg_")) toRemove.Add(c.gameObject);
#if UNITY_EDITOR
        if (!Application.isPlaying) foreach (var go in toRemove) DestroyImmediate(go);
        else foreach (var go in toRemove) Destroy(go);
#else
        foreach (var go in toRemove) Destroy(go);
#endif
    }

    private void UpdateWalls()
    {
        if (!containerCollider) return;
        var root = GetWallsRoot();
        if (!root) return;

        if (containerCollider is BoxCollider bc)
        {
            float w = Mathf.Abs(bc.size.x);
            float h = Mathf.Abs(bc.size.y);
            float d = Mathf.Abs(bc.size.z);
            float t = Mathf.Abs(wallThickness);

            var px = EnsureChild(root, WallPX);
            var nx = EnsureChild(root, WallNX);
            var pz = EnsureChild(root, WallPZ);
            var nz = EnsureChild(root, WallNZ);

            Vector3 posPX = new Vector3(+0.5f * w + 0.5f * t, 0f, 0f);
            Vector3 posNX = new Vector3(-0.5f * w - 0.5f * t, 0f, 0f);
            Vector3 posPZ = new Vector3(0f, 0f, +0.5f * d + 0.5f * t);
            Vector3 posNZ = new Vector3(0f, 0f, -0.5f * d - 0.5f * t);

            SetupWall(px, posPX, Quaternion.identity, new Vector3(t, h, d));
            SetupWall(nx, posNX, Quaternion.identity, new Vector3(t, h, d));
            SetupWall(pz, posPZ, Quaternion.identity, new Vector3(w + 2f * t, h, t));
            SetupWall(nz, posNZ, Quaternion.identity, new Vector3(w + 2f * t, h, t));

            root.localPosition = bc.center;
            root.localRotation = Quaternion.identity;
            root.localScale = Vector3.one;
        }
        else if (containerCollider is SphereCollider sc)
        {
            float r = Mathf.Abs(sc.radius);
            float h = 2f * r;
            float t = Mathf.Abs(wallThickness);
            int n = circleWallSegments;

            EnsureCircleSegments(root, n);

            int idx = 0;
            for (int i = 0; i < root.childCount; i++)
            {
                var c = root.GetChild(i);
                if (!c.name.StartsWith("Seg_")) continue;
                float a = idx * Mathf.PI * 2f / n;
                float R = r + 0.5f * t;
                float seg = 2f * Mathf.PI * R / n;

                c.localPosition = new Vector3(Mathf.Cos(a) * R, 0f, Mathf.Sin(a) * R);
                c.localRotation = Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f);
                var col = c.GetComponent<BoxCollider>();
                col.size = new Vector3(seg, h, t);
                idx++;
            }

            root.localPosition = sc.center;
            root.localRotation = Quaternion.identity;
            root.localScale = Vector3.one;
        }
        else if (containerCollider is CapsuleCollider cc)
        {
            int d = cc.direction;
            float r = Mathf.Abs(cc.radius);
            float h = Mathf.Abs(cc.height);
            float totalH = Mathf.Max(h, 2f * r);
            float t = Mathf.Abs(wallThickness);
            int n = circleWallSegments;

            EnsureCircleSegments(root, n);

            int idx = 0;
            for (int i = 0; i < root.childCount; i++)
            {
                var c = root.GetChild(i);
                if (!c.name.StartsWith("Seg_")) continue;
                float a = idx * Mathf.PI * 2f / n;
                float R = r + 0.5f * t;
                float seg = 2f * Mathf.PI * R / n;

                if (d == 1)
                {
                    c.localPosition = new Vector3(Mathf.Cos(a) * R, 0f, Mathf.Sin(a) * R);
                    c.localRotation = Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f);
                    var col = c.GetComponent<BoxCollider>();
                    col.size = new Vector3(seg, totalH, t);
                }
                else if (d == 0)
                {
                    c.localPosition = new Vector3(0f, Mathf.Cos(a) * R, Mathf.Sin(a) * R);
                    c.localRotation = Quaternion.Euler(0f, 0f, -a * Mathf.Rad2Deg);
                    var col = c.GetComponent<BoxCollider>();
                    col.size = new Vector3(t, seg, 2f * R);
                }
                else
                {
                    c.localPosition = new Vector3(Mathf.Cos(a) * R, Mathf.Sin(a) * R, 0f);
                    c.localRotation = Quaternion.Euler(-a * Mathf.Rad2Deg, 0f, 0f);
                    var col = c.GetComponent<BoxCollider>();
                    col.size = new Vector3(seg, 2f * R, t);
                }
                idx++;
            }

            root.localPosition = cc.center;
            root.localRotation = Quaternion.identity;
            root.localScale = Vector3.one;
        }
    }


    private void SetupWall(Transform wall, Vector3 localPos, Quaternion localRot, Vector3 size)
    {
        wall.localPosition = localPos;
        wall.localRotation = localRot;
        wall.localScale = Vector3.one;
        var col = wall.GetComponent<BoxCollider>();
        if (!col) col = wall.gameObject.AddComponent<BoxCollider>();
        col.isTrigger = false;
        col.size = size;
        wall.gameObject.layer = gameObject.layer;
        var rb = wall.GetComponent<Rigidbody>();
        if (!rb) rb = wall.gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
    }

    public void SpawnDenseLayers()
    {
        spawnedBodies.Clear();

        Vector3 basePos = spawnRoot.position;
        Quaternion baseRot = spawnRoot.rotation;

        int spawned = 0;
        int maxLayers = Mathf.CeilToInt(startCount / Mathf.Max(1f, EstimatePerLayer()));
        for (int layer = 0; spawned < startCount && layer < maxLayers + 200; layer++)
        {
            Vector2Int grid = EstimateGridForFootprint();
            for (int z = 0; z < grid.y && spawned < startCount; z++)
            {
                for (int x = 0; x < grid.x && spawned < startCount; x++)
                {
                    Vector3 local = CellLocalPosition(x, z, grid);
                    if (!InsideFootprint(local)) continue;

                    Vector3 jitter = new Vector3(Random.Range(-HorizontalJitter, HorizontalJitter), 0f, Random.Range(-HorizontalJitter, HorizontalJitter));
                    Vector3 pos = basePos + new Vector3(local.x, layer * layerH, local.z) + jitter;
                    Quaternion rot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * baseRot;

                    StackItem item = Instantiate(itemPrefab, pos, rot, parentToRoot ? spawnRoot : null);

                    Rigidbody rb = item.GetComponent<Rigidbody>();
                    if (!rb && AddRigidbody) rb = item.gameObject.AddComponent<Rigidbody>();
                    if (rb)
                    {
                        rb.isKinematic = false;
                        rb.useGravity = true;
                        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                        rb.interpolation = RigidbodyInterpolation.Interpolate;

                        if (addRandomImpulse)
                        {
                            Vector3 f = Random.insideUnitSphere * impulseStrength;
                            Vector3 t = Random.insideUnitSphere * StartRotationStrenght;
                            rb.AddForce(f, ForceMode.Impulse);
                            rb.AddTorque(t, ForceMode.Impulse);
                        }
                        spawnedBodies.Add(rb);
                    }

                    spawned++;
                }
            }
        }
    }

    private float EstimatePerLayer()
    {
        if (footprint == Footprint.Circle)
        {
            float area = Mathf.PI * circleRadius * circleRadius;
            float cellArea = cellX * cellZ;
            return Mathf.Max(1f, area / Mathf.Max(0.0001f, cellArea));
        }
        else
        {
            float area = rectSize.x * rectSize.y;
            float cellArea = cellX * cellZ;
            return Mathf.Max(1f, area / Mathf.Max(0.0001f, cellArea));
        }
    }

    private Vector2Int EstimateGridForFootprint()
    {
        if (footprint == Footprint.Rect)
        {
            int gx = Mathf.Max(1, Mathf.FloorToInt(rectSize.x / cellX));
            int gz = Mathf.Max(1, Mathf.FloorToInt(rectSize.y / cellZ));
            return new Vector2Int(gx, gz);
        }
        else
        {
            float d = circleRadius * 2f;
            int gx = Mathf.Max(1, Mathf.FloorToInt(d / cellX));
            int gz = Mathf.Max(1, Mathf.FloorToInt(d / cellZ));
            return new Vector2Int(gx, gz);
        }
    }

    private Vector3 CellLocalPosition(int x, int z, Vector2Int grid)
    {
        float width = (grid.x - 1) * cellX;
        float depth = (grid.y - 1) * cellZ;
        float lx = -width * 0.5f + x * cellX;
        float lz = -depth * 0.5f + z * cellZ;

        if (footprint == Footprint.Rect)
        {
            lx = Mathf.Clamp(lx, -rectSize.x * 0.5f, rectSize.x * 0.5f);
            lz = Mathf.Clamp(lz, -rectSize.y * 0.5f, rectSize.y * 0.5f);
        }
        else
        {
            float r = circleRadius;
            lx = Mathf.Clamp(lx, -r, r);
            lz = Mathf.Clamp(lz, -r, r);
        }
        return new Vector3(lx, 0f, lz);
    }

    private bool InsideFootprint(Vector3 local)
    {
        if (containerCollider)
        {
            Vector3 world = spawnRoot.TransformPoint(local);
            if (!PointInsideCollider(containerCollider, world)) return false;
        }

        if (footprint == Footprint.Rect)
        {
            return Mathf.Abs(local.x) <= rectSize.x * 0.5f && Mathf.Abs(local.z) <= rectSize.y * 0.5f;
        }
        float r = circleRadius;
        return (local.x * local.x + local.z * local.z) <= r * r;
    }

    private bool PointInsideCollider(Collider col, Vector3 world)
    {
        if (col is BoxCollider bc)
        {
            Vector3 lp = col.transform.InverseTransformPoint(world) - bc.center;
            Vector3 e = bc.size * 0.5f;
            return Mathf.Abs(lp.x) <= e.x && Mathf.Abs(lp.y) <= e.y && Mathf.Abs(lp.z) <= e.z;
        }
        if (col is SphereCollider sc)
        {
            Vector3 lp = col.transform.InverseTransformPoint(world) - sc.center;
            return lp.sqrMagnitude <= sc.radius * sc.radius;
        }
        if (col is CapsuleCollider cc)
        {
            Vector3 lp = col.transform.InverseTransformPoint(world) - cc.center;
            if (cc.direction == 1)
            {
                float h2 = Mathf.Max(0f, cc.height * 0.5f - cc.radius);
                float y = Mathf.Clamp(lp.y, -h2, h2);
                Vector3 d = new Vector3(lp.x, lp.y - y, lp.z);
                return d.sqrMagnitude <= cc.radius * cc.radius;
            }
            if (cc.direction == 0)
            {
                float h2 = Mathf.Max(0f, cc.height * 0.5f - cc.radius);
                float x = Mathf.Clamp(lp.x, -h2, h2);
                Vector3 d = new Vector3(lp.x - x, lp.y, lp.z);
                return d.sqrMagnitude <= cc.radius * cc.radius;
            }
            {
                float h2 = Mathf.Max(0f, cc.height * 0.5f - cc.radius);
                float z = Mathf.Clamp(lp.z, -h2, h2);
                Vector3 d = new Vector3(lp.x, lp.y, lp.z - z);
                return d.sqrMagnitude <= cc.radius * cc.radius;
            }
        }
        Bounds b = col.bounds;
        return world.x >= b.min.x && world.x <= b.max.x && world.y >= b.min.y && world.y <= b.max.y && world.z >= b.min.z && world.z <= b.max.z;
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        Transform root = spawnRoot ? spawnRoot : transform;

        Vector2Int grid = EstimateGridForFootprint();
        for (int z = 0; z < grid.y; z++)
        {
            for (int x = 0; x < grid.x; x++)
            {
                Vector3 local = CellLocalPosition(x, z, grid);
                if (!InsideFootprint(local)) continue;
                Vector3 worldPos = root.TransformPoint(local + Vector3.up * 0.02f);
                Gizmos.color = new Color(1f, 0.92f, 0.16f, 1f);
                Gizmos.DrawWireCube(worldPos, new Vector3(itemSize.x * 0.8f, 0.02f, itemSize.z * 0.8f));
            }
        }

        float estLayers = Mathf.Max(1f, startCount / Mathf.Max(1f, EstimatePerLayer()));
        float estHeight = estLayers * layerH;
        Vector3 volCenter = new Vector3(root.position.x, root.position.y + estHeight * 0.5f, root.position.z);

        Gizmos.color = new Color(0f, 1f, 0f, 0.4f);
        if (footprint == Footprint.Rect)
        {
            Gizmos.DrawWireCube(volCenter, new Vector3(rectSize.x, estHeight, rectSize.y));
        }
        else
        {
            Handles.color = new Color(0f, 1f, 0f, 0.8f);
            Handles.DrawWireDisc(root.position, Vector3.up, circleRadius);
            Handles.DrawWireDisc(volCenter + Vector3.up * (estHeight * 0.5f - 0.01f), Vector3.up, circleRadius);
            Handles.DrawWireDisc(volCenter - Vector3.up * (estHeight * 0.5f - 0.01f), Vector3.up, circleRadius);
        }

        if (containerCollider)
        {
            Gizmos.color = Color.cyan;
            var b = containerCollider.bounds;
            Gizmos.DrawWireCube(b.center, b.size);
        }
    }
#endif
}

#if UNITY_EDITOR
[CustomEditor(typeof(PileScript))]
public class PileScriptEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.PropertyField(serializedObject.FindProperty("itemPrefab"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("startCount"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("spawnRoot"));

        var fp = serializedObject.FindProperty("footprint");
        EditorGUILayout.PropertyField(fp);

        if ((PileScript.Footprint)fp.enumValueIndex == PileScript.Footprint.Rect)
            EditorGUILayout.PropertyField(serializedObject.FindProperty("rectSize"));
        else
            EditorGUILayout.PropertyField(serializedObject.FindProperty("circleRadius"));

        EditorGUILayout.PropertyField(serializedObject.FindProperty("spacingScale"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("VerticalSpacingFactor"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("HorizontalJitter"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("parentToRoot"));

        EditorGUILayout.Space();
        EditorGUILayout.PropertyField(serializedObject.FindProperty("AddRigidbody"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("addRandomImpulse"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("impulseStrength"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("StartRotationStrenght"));

        EditorGUILayout.Space();
        EditorGUILayout.PropertyField(serializedObject.FindProperty("containerCollider"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("syncFootprintWithContainer"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("wallThickness"));
        if (((PileScript)target).containerCollider is SphereCollider || ((PileScript)target).containerCollider is CapsuleCollider)
            EditorGUILayout.PropertyField(serializedObject.FindProperty("circleWallSegments"));

        serializedObject.ApplyModifiedProperties();
    }
}
#endif
