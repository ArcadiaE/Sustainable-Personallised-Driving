using System.Collections.Generic;
using CesiumForUnity;
using UnityEngine;

public class RearMirror : MonoBehaviour
{
    [Header("Glass patch centres (car-local, from the FBX scan)")]
    public Vector3 interiorGlassPos = new(0f, 1.358f, 0.572f);
    public Vector3 leftGlassPos = new(-1.015f, 1.146f, 0.722f);
    public Vector3 rightGlassPos = new(1.015f, 1.146f, 0.722f);
    [Tooltip("A patch counts as 'the glass' when its centroid is within this distance of the expected centre.")]
    public float patchTolerance = 0.09f;
    [Tooltip("Shrink the overlay toward its centre so the model's own bezel stays visible around the feed.")]
    [Range(0f, 0.3f)] public float borderFraction = 0.12f;

    [Header("Cameras")]
    [Tooltip("Interior mirror feed: above the rear glass, like a reversing camera — the cabin shell blocks any camera placed inside.")]
    public Vector3 rearCamLocalPos = new(0f, 1.55f, -1.9f);
    public float rearCamFov = 26f;
    [Tooltip("Door mirror cameras look back and slightly outward.")]
    public float doorCamOutwardYawDeg = 15f;
    public float doorCamFov = 30f;

    readonly List<RenderTexture> _rts = new();
    CesiumCameraManager _cesiumCams;

    void Start()
    {
        var drv = FindFirstObjectByType<AutoDriver>();
        if (drv == null) { Debug.LogWarning("[RearMirror] no AutoDriver — mirrors not built."); return; }
        Transform car = drv.transform;

        var geo = FindFirstObjectByType<CesiumGeoreference>();
        if (geo != null) _cesiumCams = CesiumCameraManager.GetOrCreate(geo.gameObject);
        else Debug.LogWarning("[RearMirror] no CesiumGeoreference — mirrors will only show non-Cesium scenery.");

        var eye = car.GetComponentInChildren<Camera>();
        if (eye != null && eye.nearClipPlane > 0.2f) eye.nearClipPlane = 0.2f;

        var rearRt = MakeCam(car, "RearMirrorCamera", rearCamLocalPos,
                             Quaternion.Euler(4f, 180f, 0f), rearCamFov, 512, 192);
        bool a = BuildGlassOverlay(car, "RMCar05_Interior_LOD0", interiorGlassPos, "RearMirrorGlass", rearRt);

        var lRt = MakeCam(car, "LeftMirrorCamera", leftGlassPos + new Vector3(0f, 0f, -0.02f),
                          Quaternion.Euler(0f, 180f + doorCamOutwardYawDeg, 0f), doorCamFov, 256, 192);
        bool b = BuildGlassOverlay(car, "RMCar05_Body_LOD0", leftGlassPos, "LeftMirrorGlass", lRt);

        var rRt = MakeCam(car, "RightMirrorCamera", rightGlassPos + new Vector3(0f, 0f, -0.02f),
                          Quaternion.Euler(0f, 180f - doorCamOutwardYawDeg, 0f), doorCamFov, 256, 192);
        bool c = BuildGlassOverlay(car, "RMCar05_Body_LOD0", rightGlassPos, "RightMirrorGlass", rRt);

        Debug.Log($"[RearMirror] built on the model's own glass triangles: interior={a} left={b} right={c}");
    }

    RenderTexture MakeCam(Transform car, string name, Vector3 localPos, Quaternion localRot,
                          float fov, int w, int h)
    {
        var rt = new RenderTexture(w, h, 16) { name = name + "RT" };
        _rts.Add(rt);
        var go = new GameObject(name);
        go.transform.SetParent(car, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = localRot;
        var cam = go.AddComponent<Camera>();
        cam.fieldOfView = fov;
        cam.nearClipPlane = 0.3f;
        cam.targetTexture = rt;
        cam.depth = -10f;
        if (_cesiumCams != null && !_cesiumCams.additionalCameras.Contains(cam))
            _cesiumCams.additionalCameras.Add(cam);
        return rt;
    }

    bool BuildGlassOverlay(Transform car, string rendererName, Vector3 carLocalCentre,
                           string overlayName, RenderTexture rt)
    {
        MeshFilter src = null;
        foreach (var mf in car.GetComponentsInChildren<MeshFilter>(true))
            if (mf.gameObject.name == rendererName) { src = mf; break; }
        if (src == null || src.sharedMesh == null)
        { Debug.LogWarning($"[RearMirror] renderer '{rendererName}' not found."); return false; }

        var mesh = src.sharedMesh;
        var verts = mesh.vertices;
        var tris = mesh.GetTriangles(0);
        int nT = tris.Length / 3;

        int[] parent = new int[nT];
        for (int i = 0; i < nT; i++) parent[i] = i;
        System.Func<int, int> find = i =>
        {
            while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; }
            return i;
        };
        var owner = new Dictionary<int, int>();
        for (int t = 0; t < nT; t++)
            for (int k = 0; k < 3; k++)
            {
                int vi = tris[t * 3 + k];
                if (owner.TryGetValue(vi, out int o)) { int x = find(o), y = find(t); if (x != y) parent[x] = y; }
                else owner[vi] = t;
            }
        var clusters = new Dictionary<int, List<int>>();
        for (int t = 0; t < nT; t++)
        {
            int r = find(t);
            if (!clusters.TryGetValue(r, out var lst)) clusters[r] = lst = new List<int>();
            lst.Add(t);
        }

        Vector3 want = src.transform.InverseTransformPoint(car.TransformPoint(carLocalCentre));
        List<int> best = null; float bestD = float.MaxValue;
        foreach (var kv in clusters)
        {
            if (kv.Value.Count < 6) continue;
            Vector3 c = Vector3.zero; int cnt = 0;
            foreach (int t in kv.Value)
                for (int k = 0; k < 3; k++) { c += verts[tris[t * 3 + k]]; cnt++; }
            c /= cnt;
            float d = (c - want).magnitude;
            if (d < bestD) { bestD = d; best = kv.Value; }
        }
        if (best == null || bestD > patchTolerance)
        { Debug.LogWarning($"[RearMirror] no glass patch within {patchTolerance} m of {carLocalCentre} (closest {bestD:F3})."); return false; }

        var norms = mesh.normals;
        Vector3 nAvg = Vector3.zero;
        var map = new Dictionary<int, int>();
        var nv = new List<Vector3>();
        var ntris = new List<int>();
        foreach (int t in best)
            for (int k = 0; k < 3; k++)
            {
                int vi = tris[t * 3 + k];
                if (!map.TryGetValue(vi, out int ni))
                {
                    ni = nv.Count; map[vi] = ni;
                    nv.Add(verts[vi]);
                    if (norms != null && vi < norms.Length) nAvg += norms[vi];
                }
                ntris.Add(ni);
            }
        nAvg = nAvg.normalized;

        Vector3 upM = src.transform.InverseTransformDirection(car.up);
        Vector3 u = Vector3.Cross(upM, nAvg).normalized;
        if (u.sqrMagnitude < 1e-6f) u = Vector3.Cross(Vector3.right, nAvg).normalized;
        Vector3 v = Vector3.Cross(nAvg, u).normalized;
        float minU = float.MaxValue, maxU = float.MinValue, minV = float.MaxValue, maxV = float.MinValue;
        foreach (var p in nv)
        {
            float pu = Vector3.Dot(p, u), pv = Vector3.Dot(p, v);
            minU = Mathf.Min(minU, pu); maxU = Mathf.Max(maxU, pu);
            minV = Mathf.Min(minV, pv); maxV = Mathf.Max(maxV, pv);
        }
        Vector3 c3 = Vector3.zero;
        foreach (var p in nv) c3 += p;
        c3 /= nv.Count;

        var uvs = new Vector2[nv.Count];
        for (int i = 0; i < nv.Count; i++)
        {
            float pu = Mathf.InverseLerp(minU, maxU, Vector3.Dot(nv[i], u));
            float pv = Mathf.InverseLerp(minV, maxV, Vector3.Dot(nv[i], v));
            uvs[i] = new Vector2(1f - pu, pv);
            nv[i] = c3 + (nv[i] - c3) * (1f - borderFraction);
            nv[i] += nAvg * 0.002f;
        }

        var m = new Mesh { name = overlayName + "Mesh" };
        m.SetVertices(nv);
        m.SetTriangles(ntris, 0);
        m.SetUVs(0, new List<Vector2>(uvs));
        m.RecalculateNormals();
        m.RecalculateBounds();

        var go = new GameObject(overlayName);
        go.transform.SetParent(src.transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = m;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { mainTexture = rt };
        return true;
    }

    void OnDestroy()
    {
        foreach (var rt in _rts) if (rt != null) rt.Release();
    }
}
