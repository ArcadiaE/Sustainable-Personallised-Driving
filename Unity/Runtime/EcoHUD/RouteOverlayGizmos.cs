using System.Collections.Generic;
using UnityEngine;

public class RouteOverlayGizmos : MonoBehaviour
{
    [System.Serializable]
    public class RouteLine
    {
        public string label;
        public Color color = Color.red;
        public Vector3[] pts;
        public Vector3 labelPos;
        public bool startSphere = true;
    }

    public List<RouteLine> lines = new();
    public float lineWidth = 8f;
    public float sphereRadius = 2.5f;
    public bool drawSharedStart;
    public Vector3 sharedStartPos;

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        foreach (var l in lines)
        {
            if (l.pts == null || l.pts.Length < 2) continue;
            UnityEditor.Handles.color = l.color;
            UnityEditor.Handles.DrawAAPolyLine(lineWidth, l.pts);
            if (l.startSphere)
            {
                Gizmos.color = l.color;
                Gizmos.DrawSphere(l.pts[0] + Vector3.up * 1.5f, sphereRadius);
            }
            if (!string.IsNullOrEmpty(l.label))
            {
                var style = new GUIStyle(UnityEditor.EditorStyles.boldLabel)
                {
                    fontSize = 18,
                    normal = { textColor = l.color }
                };
                UnityEditor.Handles.Label(l.labelPos, l.label, style);
            }
        }
        if (drawSharedStart)
        {
            Gizmos.color = Color.white;
            Gizmos.DrawSphere(sharedStartPos, sphereRadius * 1.4f);
        }
    }
#endif
}
