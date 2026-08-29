using UnityEngine;

public class WheelPoseSync : MonoBehaviour
{
    [Tooltip("Physics wheels (the old car's WheelColliders), same order as visuals.")]
    public WheelCollider[] colliders;
    [Tooltip("Visual wheel containers on the new model (rotor + tyre), same order.")]
    public Transform[] visuals;

    Vector3[] _initLocalPos;
    float[] _restPoseLocalY;

    void Start()
    {
        int n = Mathf.Min(colliders?.Length ?? 0, visuals?.Length ?? 0);
        _initLocalPos = new Vector3[n];
        _restPoseLocalY = new float[n];
        for (int i = 0; i < n; i++)
        {
            if (visuals[i] == null || colliders[i] == null) continue;
            _initLocalPos[i] = visuals[i].localPosition;
            colliders[i].GetWorldPose(out Vector3 p, out _);
            _restPoseLocalY[i] = transform.InverseTransformPoint(p).y;
        }
    }

    Rigidbody _rb;
    CarController _car;
    float _spinDeg;

    void LateUpdate()
    {
        if (_initLocalPos == null) return;

        if (_rb == null) _rb = GetComponent<Rigidbody>();
        float fwd = _rb != null ? Vector3.Dot(_rb.linearVelocity, transform.forward) : 0f;
        _spinDeg += fwd / 0.33f * Mathf.Rad2Deg * Time.deltaTime;   // r ≈ 0.33 m

        for (int i = 0; i < _initLocalPos.Length; i++)
        {
            var col = colliders[i];
            var vis = visuals[i];
            if (vis == null) continue;

            if (col == null || !col.enabled || !col.gameObject.activeInHierarchy)
            {
                if (_car == null) _car = GetComponent<CarController>();
                float steer = (_car != null && _initLocalPos[i].z > 0f) ? _car.currentSteerAngle : 0f;
                vis.localPosition = _initLocalPos[i];
                vis.localRotation = Quaternion.Euler(_spinDeg % 360f, steer, 0f);
                continue;
            }

            col.GetWorldPose(out Vector3 pos, out Quaternion rot);
            vis.rotation = rot;
            Vector3 lp = _initLocalPos[i];
            lp.y += transform.InverseTransformPoint(pos).y - _restPoseLocalY[i];
            vis.localPosition = lp;
        }
    }
}
