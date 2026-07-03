// =============================================================================
//  RoadBoundaryWalls.cs
//  Copyright (c) 2026 Yike Zhang. COMP0190 P87, UCL CS (supervisor: Dr Mark Colley).
//
//  Builds INVISIBLE collision walls along the road network so the car physically cannot
//  leave the carriageway and drive into buildings. The constraint comes from the road
//  network itself, not from modelling buildings: the cut reveals the CityGen road,
//  and these walls fence its edges.
//
//  Per carriageway mesh (MapRoads, Footpaths excluded) it finds the boundary edges (edges
//  used by a single triangle = the road outline), keeps the ones running ALONG the road
//  (the building-facing long sides) and drops the ones running ACROSS it (the ends), and
//  trims a little off each end so junctions stay open for turns. The kept edges are
//  extruded up into wall quads, merged into one mesh with a MeshCollider, no renderer.
//
//  IMPORTANT: build it in the EDITOR with the context menu "Build Walls". At runtime the
//  road meshes are static-batched into non-readable "Combined Mesh" objects, so a Start-time
//  build reads nothing. The editor build reads the original meshes and the result is saved
//  with the scene, so it is present in Play. Keep Build On Start OFF. Works for the
//  auto-driver AND for a human driver, which is what the study needs.
//
// =============================================================================

using System.Collections.Generic;
using UnityEngine;

public class RoadBoundaryWalls : MonoBehaviour
{
    [Header("Build")]
    public string roadsRootName = "MapRoads";
    public float wallHeight = 4f;
    [Tooltip("Edge counts as a road side (walled) when this parallel to the road axis. Lower = wall more.")]
    [Range(0.3f, 0.9f)] public float sideParallelism = 0.5f;
    [Tooltip("Trim this many metres off each road end so junctions stay open.")]
    public float junctionGap = 5f;
    [Tooltip("Only wall roads within this distance of the centre below (0 = whole map).")]
    public float limitRadius = 0f;
    public Vector2 limitCentreXZ = new(360f, 620f);
    [Tooltip("Leave OFF: at runtime road meshes are static-batched and unreadable. Build in the editor.")]
    public bool buildOnStart = false;

    GameObject built;

    void Start() { if (buildOnStart) Build(); }

    [ContextMenu("Build Walls")]
    public void Build()
    {
        GameObject root = GameObject.Find(roadsRootName);
        if (root == null)
        {
            Debug.LogError($"[RoadBoundaryWalls] '{roadsRootName}' not found.");
            return;
        }

        var verts = new List<Vector3>();
        var tris = new List<int>();
        Vector3 up = Vector3.up * wallHeight;
        int roadsUsed = 0;

        foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            if (mf.gameObject.name.IndexOf("Footpath", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;

            Mesh mesh = mf.sharedMesh;
            if (!mesh.isReadable)
            {
                Debug.LogWarning($"[RoadBoundaryWalls] mesh '{mesh.name}' is not Read/Write enabled; skipped.");
                continue;
            }

            Transform tr = mf.transform;
            Vector3[] lv = mesh.vertices;
            int[] mt = mesh.triangles;

            // world-space vertices
            var wv = new Vector3[lv.Length];
            for (int i = 0; i < lv.Length; i++) wv[i] = tr.TransformPoint(lv[i]);

            // limit to an area (optional)
            if (limitRadius > 0f)
            {
                Vector3 c = tr.TransformPoint(mesh.bounds.center);
                if ((new Vector2(c.x, c.z) - limitCentreXZ).sqrMagnitude > limitRadius * limitRadius) continue;
            }

            // principal (road) axis in XZ via 2D covariance
            float mx = 0, mz = 0;
            for (int i = 0; i < wv.Length; i++) { mx += wv[i].x; mz += wv[i].z; }
            mx /= wv.Length; mz /= wv.Length;
            float cxx = 0, czz = 0, cxz = 0;
            for (int i = 0; i < wv.Length; i++)
            {
                float dx = wv[i].x - mx, dz = wv[i].z - mz;
                cxx += dx * dx; czz += dz * dz; cxz += dx * dz;
            }
            float ang = 0.5f * Mathf.Atan2(2f * cxz, cxx - czz);
            Vector2 axis = new(Mathf.Cos(ang), Mathf.Sin(ang));

            // projection range along the axis (to trim ends)
            float tmin = float.MaxValue, tmax = float.MinValue;
            var proj = new float[wv.Length];
            for (int i = 0; i < wv.Length; i++)
            {
                float p = (wv[i].x - mx) * axis.x + (wv[i].z - mz) * axis.y;
                proj[i] = p; if (p < tmin) tmin = p; if (p > tmax) tmax = p;
            }

            // boundary edges = edges used by exactly one triangle
            var count = new Dictionary<long, int>();
            for (int i = 0; i < mt.Length; i += 3)
            {
                AddEdge(count, mt[i], mt[i + 1]);
                AddEdge(count, mt[i + 1], mt[i + 2]);
                AddEdge(count, mt[i + 2], mt[i]);
            }

            foreach (var kv in count)
            {
                if (kv.Value != 1) continue;
                int a = (int)(kv.Key >> 32), b = (int)(kv.Key & 0xffffffff);
                Vector3 wa = wv[a], wb = wv[b];

                Vector2 dir = new(wb.x - wa.x, wb.z - wa.z);
                float len = dir.magnitude;
                if (len < 0.01f) continue;
                dir /= len;
                if (Mathf.Abs(dir.x * axis.x + dir.y * axis.y) < sideParallelism) continue; // an end edge, skip

                float midp = ((proj[a] + proj[b]) * 0.5f);
                if (midp < tmin + junctionGap || midp > tmax - junctionGap) continue;       // junction throat, skip

                int baseI = verts.Count;
                verts.Add(wa); verts.Add(wb); verts.Add(wb + up); verts.Add(wa + up);
                tris.Add(baseI); tris.Add(baseI + 1); tris.Add(baseI + 2);
                tris.Add(baseI); tris.Add(baseI + 2); tris.Add(baseI + 3);
            }
            roadsUsed++;
        }

        if (verts.Count == 0)
        {
            Debug.LogWarning("[RoadBoundaryWalls] no wall geometry produced (road meshes are likely " +
                             "non-readable at runtime; build in the editor with the context menu). " +
                             "Existing walls left untouched.");
            return;   // do NOT clear: keep any walls built earlier in the editor
        }

        Clear();   // replace previous walls only now that we have new geometry

        var wallMesh = new Mesh { name = "RoadBoundaryWallMesh" };
        wallMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        wallMesh.SetVertices(verts);
        wallMesh.SetTriangles(tris, 0);

        built = new GameObject("RoadBoundaryWalls (generated)");
        built.transform.SetParent(transform, false);
        var mc = built.AddComponent<MeshCollider>();
        mc.sharedMesh = wallMesh;   // non-convex static collider; no renderer = invisible

        Debug.Log($"[RoadBoundaryWalls] built {tris.Count / 6} wall quads from {roadsUsed} roads.");
    }

    [ContextMenu("Clear Walls")]
    public void Clear()
    {
        if (built != null) { DestroyImmediate(built); built = null; }
        // also clear a leftover child from a previous build
        var old = transform.Find("RoadBoundaryWalls (generated)");
        if (old != null) DestroyImmediate(old.gameObject);
    }

    static void AddEdge(Dictionary<long, int> count, int i, int j)
    {
        long key = i < j ? ((long)i << 32) | (uint)j : ((long)j << 32) | (uint)i;
        count[key] = count.TryGetValue(key, out int c) ? c + 1 : 1;
    }
}
