using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
[ExecuteAlways]
public class GridCulledTile : MonoBehaviour {
    public Grid grid;
    public Vector3 fallbackCellSize = new Vector3(1, 1, 1);
    public LayerMask neighborLayer = ~0;
    public bool ignoreGridCellSize = false;
    public float epsilon = 1e-3f;
    public float neighborProbeRadius = 0.2f;
    public bool clipOutsideCell = false;
    public bool forceShowOriginal = false;

    private const float minAxis = 1e-3f;

    private Mesh originalMesh;
    private MeshFilter mf;
    private MeshCollider mc;
    private Vector3Int cell;
    private Vector3 lastPos, lastScale;
    private Quaternion lastRot;

    private static readonly Dictionary<Vector3Int, GridCulledTile> registry = new();
    private static readonly Dictionary<(Mesh, int, Vector3), Mesh> maskMeshCache = new();

    private void Awake() {
        mf = GetComponent<MeshFilter>();
        if (mf != null)
            originalMesh = mf.sharedMesh;
        if (mc == null)
            mc = GetComponent<MeshCollider>() ?? gameObject.AddComponent<MeshCollider>();
        if (grid == null)
            grid = FindFirstObjectByType<Grid>();
        cell = WorldToCell(transform.position);
        lastPos = transform.position;
        lastScale = transform.localScale;
        lastRot = transform.rotation;
    }

    private void OnEnable() {
        registry[cell] = this;
        RebuildSelfAndNeighbors();
    }

    private void OnDisable() {
        if (registry.TryGetValue(cell, out var cur) && cur == this)
            registry.Remove(cell);
        RebuildNeighborsOnly();
    }

    private void OnValidate() { SafeRefresh(); }

    private void Update() {
        if (transform.position != lastPos || transform.localScale != lastScale || transform.rotation != lastRot) {
            SafeRefresh();
        }
    }

    public void Refresh() {
        var newCell = WorldToCell(transform.position);
        if (newCell != cell) {
            if (registry.TryGetValue(cell, out var cur) && cur == this)
                registry.Remove(cell);
            cell = newCell;
            registry[cell] = this;
        }
        lastPos = transform.position;
        lastScale = transform.localScale;
        lastRot = transform.rotation;
        RebuildSelfAndNeighbors();
    }

    private void SafeRefresh() {
        if (mf == null || originalMesh == null)
            Awake();
        Refresh();
    }

    private void RebuildSelfAndNeighbors() {
        Rebuild();
        var dirs = SixDirs();
        for (int i = 0; i < 6; ++i) {
            var nCell = cell + dirs[i];
            if (registry.TryGetValue(nCell, out var t))
                t.Rebuild();
        }
    }

    private void RebuildNeighborsOnly() {
        var dirs = SixDirs();
        for (int i = 0; i < 6; ++i) {
            var nCell = cell + dirs[i];
            if (registry.TryGetValue(nCell, out var t))
                t.Rebuild();
        }
    }

    private void Rebuild() {
        if (originalMesh == null || mf == null)
            return;
        if (forceShowOriginal) { mf.sharedMesh = originalMesh; if (mc != null) mc.sharedMesh = originalMesh; return; }

        int mask = NeighborMask(cell);
        Mesh culled = TryGetCached(mask);
        if (culled == null) { culled = BuildCulledMesh(originalMesh, mask); TrySetCached(mask, culled); }

        mf.sharedMesh = culled;
        if (mc != null)
            mc.sharedMesh = culled;
    }

    private int NeighborMask(Vector3Int c) {
        int m = 0;
        var dirs = SixDirs();
        for (int i = 0; i < 6; ++i)
            if (HasNeighbor(c + dirs[i]))
                m |= (1 << i);
        return m;
    }

    private bool HasNeighbor(Vector3Int target) {
        if (registry.ContainsKey(target))
            return true;
        Vector3 center = CellCenterWS(target);
        float r = Mathf.Max(0.01f, neighborProbeRadius);
        Collider[] hits = Physics.OverlapSphere(center, r, neighborLayer, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hits.Length; ++i) {
            if (hits[i].GetComponent<MeshRenderer>() != null)
                return true;
        }
        return false;
    }

    private Vector3Int WorldToCell(Vector3 worldPos) {
        if (grid != null)
            return grid.WorldToCell(worldPos);
        Vector3 s = GetCellSizeResolved();
        return new Vector3Int(
            Mathf.RoundToInt(worldPos.x / Mathf.Max(minAxis, s.x)),
            Mathf.RoundToInt(worldPos.y / Mathf.Max(minAxis, s.y)),
            Mathf.RoundToInt(worldPos.z / Mathf.Max(minAxis, s.z))
        );
    }

    private Vector3 CellCenterWS(Vector3Int c) {
        Vector3 size = GetCellSizeResolved();
        return grid != null
            ? grid.GetCellCenterWorld(c)
            : new Vector3(c.x * size.x, c.y * size.y, c.z * size.z);
    }

    private Vector3 GetCellSizeResolved() {
        Vector3 fb = fallbackCellSize;
        if (fb.x == 0)
            fb.x = 1;
        if (fb.y == 0)
            fb.y = 1;
        if (fb.z == 0)
            fb.z = 1;

        if (grid == null || ignoreGridCellSize)
            return new Vector3(Mathf.Max(minAxis, Mathf.Abs(fb.x)), Mathf.Max(minAxis, Mathf.Abs(fb.y)), Mathf.Max(minAxis, Mathf.Abs(fb.z)));

        Vector3 s = grid.cellSize;
        float x = Mathf.Abs((s.x == 0) ? fb.x : s.x);
        float y = Mathf.Abs((s.y == 0) ? fb.y : s.y);
        float z = Mathf.Abs((s.z == 0) ? fb.z : s.z);
        return new Vector3(Mathf.Max(minAxis, x), Mathf.Max(minAxis, y), Mathf.Max(minAxis, z));
    }

    private Vector3 CellSizeWS() => GetCellSizeResolved();

    private Mesh TryGetCached(int mask) {
        Vector3 scaleKey = transform.lossyScale;
        if (maskMeshCache.TryGetValue((originalMesh, mask, scaleKey), out var m))
            return m;
        return null;
    }

    private void TrySetCached(int mask, Mesh m) {
        Vector3 scaleKey = transform.lossyScale;
        var key = (originalMesh, mask, scaleKey);
        if (!maskMeshCache.ContainsKey(key))
            maskMeshCache[key] = m;
    }

    private Mesh BuildCulledMesh(Mesh src, int mask) {
        var v = src.vertices;
        var n = src.normals;
        var uv = new List<Vector2>();
        src.GetUVs(0, uv);
        var tri = src.triangles;

        bool hasNormals = n != null && n.Length == v.Length;
        bool hasUV = uv != null && uv.Count == v.Length;

        var keepTris = new List<int>(tri.Length);

        Vector3 center = CellCenterWS(cell);
        Vector3 half = 0.5f * Abs(CellSizeWS());
        float minX = center.x - half.x, maxX = center.x + half.x;
        float minY = center.y - half.y, maxY = center.y + half.y;
        float minZ = center.z - half.z, maxZ = center.z + half.z;

        bool clamp = clipOutsideCell;

        for (int i = 0; i < tri.Length; i += 3) {
            int ia = tri[i], ib = tri[i + 1], ic = tri[i + 2];
            Vector3 wa = transform.TransformPoint(v[ia]);
            Vector3 wb = transform.TransformPoint(v[ib]);
            Vector3 wc = transform.TransformPoint(v[ic]);

            if (clamp) {
                if (Outside(wa, minX, maxX, minY, maxY, minZ, maxZ) ||
                    Outside(wb, minX, maxX, minY, maxY, minZ, maxZ) ||
                    Outside(wc, minX, maxX, minY, maxY, minZ, maxZ))
                    continue;
            }

            Vector3 na = hasNormals ? transform.TransformDirection(n[ia]) : Vector3.zero;
            Vector3 nb = hasNormals ? transform.TransformDirection(n[ib]) : Vector3.zero;
            Vector3 nc = hasNormals ? transform.TransformDirection(n[ic]) : Vector3.zero;

            if (CullOnPlane(mask, wa, wb, wc, na, nb, nc, 0, maxX, +1, 0))
                continue;
            if (CullOnPlane(mask, wa, wb, wc, na, nb, nc, 3, minX, -1, 0))
                continue;
            if (CullOnPlane(mask, wa, wb, wc, na, nb, nc, 1, maxY, +1, 1))
                continue;
            if (CullOnPlane(mask, wa, wb, wc, na, nb, nc, 4, minY, -1, 1))
                continue;
            if (CullOnPlane(mask, wa, wb, wc, na, nb, nc, 2, maxZ, +1, 2))
                continue;
            if (CullOnPlane(mask, wa, wb, wc, na, nb, nc, 5, minZ, -1, 2))
                continue;

            keepTris.Add(ia);
            keepTris.Add(ib);
            keepTris.Add(ic);
        }

        if (keepTris.Count == 0)
            return src;

        var outMesh = new Mesh { indexFormat = (v.Length > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16) };
        outMesh.vertices = v;
        if (hasNormals)
            outMesh.normals = n;
        else
            outMesh.RecalculateNormals();
        if (hasUV)
            outMesh.SetUVs(0, uv);
        outMesh.triangles = keepTris.ToArray();
        outMesh.RecalculateBounds();
        return outMesh;
    }

    private bool CullOnPlane(int mask, Vector3 a, Vector3 b, Vector3 c, Vector3 na, Vector3 nb, Vector3 nc, int bit, float planePos, int dir, int axis) {
        if (((mask >> bit) & 1) == 0)
            return false;
        float ax = axis == 0 ? a.x : (axis == 1 ? a.y : a.z);
        float bx = axis == 0 ? b.x : (axis == 1 ? b.y : b.z);
        float cx = axis == 0 ? c.x : (axis == 1 ? c.y : c.z);

        bool onPlane = Mathf.Abs(ax - planePos) <= epsilon && Mathf.Abs(bx - planePos) <= epsilon && Mathf.Abs(cx - planePos) <= epsilon;
        if (!onPlane)
            return false;

        Vector3 nAvg = na + nb + nc;
        float comp = axis == 0 ? nAvg.x : (axis == 1 ? nAvg.y : nAvg.z);
        if (dir > 0 && comp > 0f)
            return true;
        if (dir < 0 && comp < 0f)
            return true;
        return false;
    }

    private static Vector3 Abs(Vector3 v) => new(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
    private bool Outside(Vector3 w, float minX, float maxX, float minY, float maxY, float minZ, float maxZ) {
        return (w.x < minX - epsilon) || (w.x > maxX + epsilon) ||
               (w.y < minY - epsilon) || (w.y > maxY + epsilon) ||
               (w.z < minZ - epsilon) || (w.z > maxZ + epsilon);
    }
    private static Vector3Int[] SixDirs() {
        return new[] {
            new Vector3Int(+1,0,0),
            new Vector3Int(0,+1,0),
            new Vector3Int(0,0,+1),
            new Vector3Int(-1,0,0),
            new Vector3Int(0,-1,0),
            new Vector3Int(0,0,-1)
        };
    }
}
