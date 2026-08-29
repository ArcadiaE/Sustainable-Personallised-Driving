using System.Collections.Generic;
using UnityEngine;

public class CollisionLogger : MonoBehaviour
{
    [Tooltip("Minimum seconds between logs for the SAME other collider.")]
    public float perObjectCooldown = 2f;

    readonly Dictionary<Collider, float> _last = new();

    void Start()
    {
        var box = GetComponent<BoxCollider>();
        if (box != null)
            Debug.Log($"[CollisionLogger] body BoxCollider centre={box.center} size={box.size} " +
                      $"(visual half-width incl. door mirrors ≈ {box.size.x / 2f:F2} m)");
    }

    bool IsRollingContact(Collision col, Vector3 local)
        => col.collider.gameObject.layer == 15 && local.y < 0.12f;

    void OnCollisionEnter(Collision col)
    {
        if (col.collider == null) return;
        if (_last.TryGetValue(col.collider, out float t) && Time.time - t < perObjectCooldown) return;
        _last[col.collider] = Time.time;

        Vector3 p = col.contactCount > 0 ? col.GetContact(0).point : col.collider.ClosestPoint(transform.position);
        Vector3 local = transform.InverseTransformPoint(p);
        if (IsRollingContact(col, local)) return;
        string corner = $"{(local.z > 0.5f ? "front" : local.z < -0.5f ? "rear" : "side")}-{(local.x > 0.2f ? "right" : local.x < -0.2f ? "left" : "centre")}";

        Debug.LogWarning($"[CollisionLogger] HIT '{Path(col.collider.transform)}' " +
                         $"layer={LayerMask.LayerToName(col.collider.gameObject.layer)}({col.collider.gameObject.layer}) " +
                         $"at car-local ({local.x:F2},{local.y:F2},{local.z:F2}) [{corner}] " +
                         $"impact={col.relativeVelocity.magnitude:F1} m/s carPos={transform.position:F1}");
    }

    void OnCollisionStay(Collision col)
    {
        if (col.collider == null) return;
        if (_last.TryGetValue(col.collider, out float t) && Time.time - t < 5f) return;
        _last[col.collider] = Time.time;
        Vector3 p = col.contactCount > 0 ? col.GetContact(0).point : col.collider.ClosestPoint(transform.position);
        Vector3 local = transform.InverseTransformPoint(p);
        if (IsRollingContact(col, local)) return;
        Debug.LogWarning($"[CollisionLogger] STILL TOUCHING '{Path(col.collider.transform)}' " +
                         $"layer={LayerMask.LayerToName(col.collider.gameObject.layer)} " +
                         $"at car-local ({local.x:F2},{local.y:F2},{local.z:F2}) carPos={transform.position:F1}");
    }

    static string Path(Transform t)
    {
        string s = t.name;
        while (t.parent != null) { t = t.parent; s = t.name + "/" + s; }
        return s;
    }
}
