using System.Collections.Generic;
using UnityEngine;

public static class RouteCandidatesData
{
    public class Option
    {
        public int optionIndex;
        public long startNode;
        public List<Route> routes = new();
    }

    public class Route
    {
        public int routeIndex;
        public float lengthM;
        public string streets;
        public List<Vector2> pts = new();
        public List<float> halfw = new();
    }

    public static List<Option> Load(string path)
    {
        var result = new List<Option>();
        if (!System.IO.File.Exists(path))
        {
            Debug.LogError("[RouteCandidatesData] JSON not found: " + path);
            return result;
        }
        string txt = System.IO.File.ReadAllText(path);
        var opts = txt.Split(new[] { "\"startOption\":" }, System.StringSplitOptions.None);
        for (int o = 1; o < opts.Length; o++)
        {
            var opt = new Option
            {
                optionIndex = (int)ExtractNum(opts[o], null),
                startNode = (long)ExtractNum(opts[o], "\"startNode\":"),
            };
            var routes = opts[o].Split(new[] { "\"route\":" }, System.StringSplitOptions.None);
            for (int r = 1; r < routes.Length; r++)
            {
                string blk = routes[r];
                var route = new Route
                {
                    routeIndex = (int)ExtractNum(blk, null),
                    lengthM = (float)ExtractNum(blk, "\"lengthM\":"),
                    streets = ExtractStreets(blk),
                };
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
                            route.pts.Add(new Vector2(x, z));
                    }
                }
                int hi = blk.IndexOf("\"halfw\":");
                if (hi >= 0)
                {
                    int a2 = blk.IndexOf('[', hi), b2 = blk.IndexOf(']', a2);
                    foreach (var num in blk.Substring(a2 + 1, b2 - a2 - 1).Split(','))
                        if (float.TryParse(num.Trim(), System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out float h))
                            route.halfw.Add(h);
                }
                if (route.pts.Count > 1) opt.routes.Add(route);
            }
            result.Add(opt);
        }
        return result;
    }

    static double ExtractNum(string blk, string key)
    {
        int i = 0;
        if (key != null)
        {
            i = blk.IndexOf(key);
            if (i < 0) return 0;
            i += key.Length;
        }
        while (i < blk.Length && char.IsWhiteSpace(blk[i])) i++;
        int j = i;
        while (j < blk.Length && (char.IsDigit(blk[j]) || blk[j] == '.' || blk[j] == '-')) j++;
        double.TryParse(blk.Substring(i, j - i), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double v);
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
}
