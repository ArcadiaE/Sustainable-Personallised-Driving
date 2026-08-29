using System.Collections.Generic;
using UnityEngine;

public class RouteSet : MonoBehaviour
{
    [Tooltip("Final study routes file (route_candidates format, single option). Relative paths resolve against StreamingAssets, so the project runs from any location (reproducibility).")]
    public string jsonPath = "TrafficNet/final_routes.json";

    [Tooltip("Option id inside the file (final_routes.json always uses 1).")]
    public long startNode = 1;

    readonly List<Vector2[]> _routes = new();
    readonly List<float[]> _halfw = new();
    readonly List<string> _labels = new();

    public int Count => _routes.Count;
    public Vector2[] GetRoute(int i) => _routes[i];
    public float[] GetHalfWidths(int i) => _halfw[i];
    public string GetLabel(int i) => _labels[i];

    string ResolvedPath()
    {
        string p = jsonPath;
        if (!System.IO.Path.IsPathRooted(p))
            p = System.IO.Path.Combine(Application.streamingAssetsPath, p);
        if (!System.IO.File.Exists(p))
        {
            string bundled = System.IO.Path.Combine(Application.streamingAssetsPath,
                                                    "TrafficNet/final_routes.json");
            if (System.IO.File.Exists(bundled))
            {
                Debug.LogWarning($"[RouteSet] '{jsonPath}' not found — using bundled '{bundled}'.");
                p = bundled;
            }
        }
        return p;
    }

    void Awake()
    {
        _routes.Clear(); _halfw.Clear(); _labels.Clear();
        foreach (var opt in RouteCandidatesData.Load(ResolvedPath()))
        {
            if (opt.startNode != startNode) continue;
            foreach (var r in opt.routes)
            {
                _routes.Add(r.pts.ToArray());
                _halfw.Add(r.halfw.ToArray());
                _labels.Add($"R{r.routeIndex} {r.lengthM:F0}m {r.streets}");
            }
        }
        if (_routes.Count == 0)
            Debug.LogError($"[RouteSet] No routes for startNode {startNode} in {jsonPath} — " +
                           "regenerate route_candidates.json or update startNode.");
        else
            Debug.Log($"[RouteSet] {_routes.Count} designated routes loaded (start node {startNode}).");
    }
}
