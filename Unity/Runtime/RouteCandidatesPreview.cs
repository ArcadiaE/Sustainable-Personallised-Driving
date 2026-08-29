using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
public class RouteCandidatesPreview : MonoBehaviour
{
    static string JsonPath => System.IO.Path.Combine(Application.streamingAssetsPath,
                                                     "TrafficNet/route_candidates.json");
    const int GroundMask = (1 << 30) | (1 << 15);   // Highway | Landscape

    [Tooltip("1 or 2 = show that start option only; 0 = show both.")]
    public int showOption = 0;

    [System.Serializable] class RouteJ
    {
        public int route; public float lengthM; public string[] streets; public float[][] pts;
    }

    class Drawn
    {
        public string label;
        public Color color;
        public List<Vector3> pts = new();
    }

    readonly List<Drawn> _routes = new();
    readonly List<Vector3> _starts = new();
    bool _loaded;

    static readonly Color[] Palette = {
        new(0.78f, 0.16f, 0.16f), new(0.08f, 0.42f, 0.75f), new(0.42f, 0.12f, 0.63f),
        new(0.75f, 0.54f, 0.00f), new(0.00f, 0.55f, 0.42f),
    };

    void OnEnable() { _loaded = false; }

    void Load()
    {
        _loaded = true;
        _routes.Clear(); _starts.Clear();
        if (!System.IO.File.Exists(JsonPath))
        {
            Debug.LogError("[RouteCandidatesPreview] JSON not found: " + JsonPath);
            return;
        }
        string txt = System.IO.File.ReadAllText(JsonPath);
        var opts = MiniJsonRoutes(txt);
        foreach (var (optIdx, routeIdx, lengthM, streets, xz) in opts)
        {
            if (showOption != 0 && optIdx != showOption) continue;
            var d = new Drawn
            {
                label = $"Opt{optIdx}-R{routeIdx}  {lengthM:F0}m  {streets}",
                color = Palette[(routeIdx - 1) % Palette.Length],
            };
            float lastY = 60f;
            foreach (var (x, z) in xz)
            {
                if (Physics.Raycast(new Vector3(x, lastY + 150f, z), Vector3.down,
                                    out RaycastHit hit, 400f, GroundMask)) lastY = hit.point.y;
                d.pts.Add(new Vector3(x, lastY + 0.6f, z));
            }
            if (d.pts.Count > 1)
            {
                _routes.Add(d);
                if (routeIdx == 1) _starts.Add(d.pts[0]);
            }
        }
        Debug.Log($"[RouteCandidatesPreview] {_routes.Count} routes draped (option filter: {showOption}).");
    }

    static List<(int opt, int route, float len, string streets, List<(float, float)> pts)> MiniJsonRoutes(string txt)
    {
        var res = new List<(int, int, float, string, List<(float, float)>)>();
        var opts = txt.Split(new[] { "\"startOption\":" }, System.StringSplitOptions.None);
        for (int o = 1; o < opts.Length; o++)
        {
            int optIdx = int.Parse(opts[o].TrimStart().Substring(0, 1));
            var routes = opts[o].Split(new[] { "\"route\":" }, System.StringSplitOptions.None);
            for (int r = 1; r < routes.Length; r++)
            {
                string blk = routes[r];
                int routeIdx = int.Parse(blk.TrimStart().Substring(0, 1));
                float len = ExtractF(blk, "\"lengthM\":");
                string streets = ExtractStreets(blk);
                var pts = new List<(float, float)>();
                int pi = blk.IndexOf("\"pts\":");
                if (pi >= 0)
                {
                    int depth = 0; int i = blk.IndexOf('[', pi);
                    int end = i;
                    for (; end < blk.Length; end++)
                    {
                        if (blk[end] == '[') depth++;
                        else if (blk[end] == ']' && --depth == 0) break;
                    }
                    string arr = System.Text.RegularExpressions.Regex.Replace(
                        blk.Substring(i + 1, end - i - 1), @"\s+", "");
                    foreach (var pair in arr.Split(new[] { "],[" }, System.StringSplitOptions.None))
                    {
                        var nums = pair.Trim('[', ']').Split(',');
                        if (nums.Length >= 2 &&
                            float.TryParse(nums[0], System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out float x) &&
                            float.TryParse(nums[1], System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out float z))
                            pts.Add((x, z));
                    }
                }
                res.Add((optIdx, routeIdx, len, streets, pts));
            }
        }
        return res;
    }

    static float ExtractF(string blk, string key)
    {
        int i = blk.IndexOf(key);
        if (i < 0) return 0f;
        i += key.Length;
        while (i < blk.Length && char.IsWhiteSpace(blk[i])) i++;   // "key": 311.4
        int j = i;
        while (j < blk.Length && (char.IsDigit(blk[j]) || blk[j] == '.' || blk[j] == '-')) j++;
        float.TryParse(blk.Substring(i, j - i), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out float v);
        return v;
    }

    static string ExtractStreets(string blk)
    {
        int i = blk.IndexOf("\"streets\":");
        if (i < 0) return "";
        int a = blk.IndexOf('[', i), b = blk.IndexOf(']', a);
        string s = blk.Substring(a + 1, b - a - 1).Replace("\"", "");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ").Trim();
        return s.Replace(" ,", ",").Replace(", ", " → ").Replace(",", " → ");
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        if (!_loaded) Load();
        foreach (var d in _routes)
        {
            Handles.color = d.color;
            Handles.DrawAAPolyLine(9f, d.pts.ToArray());
            for (int i = 6; i < d.pts.Count - 1; i += 6)
            {
                Vector3 dir = (d.pts[i + 1] - d.pts[i]).normalized;
                Handles.ConeHandleCap(0, d.pts[i] + Vector3.up * 0.4f,
                    Quaternion.LookRotation(dir), 2.2f, EventType.Repaint);
            }
            var style = new GUIStyle(EditorStyles.boldLabel);
            style.normal.textColor = d.color;
            Handles.Label(d.pts[d.pts.Count - 1] + Vector3.up * 4f, "END  " + d.label, style);
            Handles.Label(d.pts[Mathf.Min(3, d.pts.Count - 1)] + Vector3.up * 4f, d.label, style);
        }
        Handles.color = Color.black;
        foreach (var s in _starts)
        {
            Handles.SphereHandleCap(0, s + Vector3.up * 1.5f, Quaternion.identity, 4f, EventType.Repaint);
            var st = new GUIStyle(EditorStyles.whiteLargeLabel);
            Handles.Label(s + Vector3.up * 8f, "SHARED START", st);
        }
    }

    public static class Menu
    {
        const string GoName = "RouteCandidatesPreview";

        [MenuItem("Tools/Sustainable Driving/Route Candidates - Show")]
        public static void Show()
        {
            Hide();
            var go = new GameObject(GoName);
            go.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
            go.AddComponent<RouteCandidatesPreview>();
            Debug.Log("[RouteCandidatesPreview] Shown. Fly the Scene view along the coloured lines; " +
                      "set 'showOption' on the object to 1 or 2 to filter. Not saved into the scene.");
        }

        [MenuItem("Tools/Sustainable Driving/Route Candidates - Hide")]
        public static void Hide()
        {
            var go = GameObject.Find(GoName);
            if (go != null) Object.DestroyImmediate(go);
        }
    }
#endif
}
