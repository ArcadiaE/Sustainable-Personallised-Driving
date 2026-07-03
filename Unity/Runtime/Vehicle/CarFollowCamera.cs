// 第三人称跟车相机：阻尼跟随 + 异常坐标保护；同时把车辆相机注册到
// CesiumCameraManager，让 Cesium 瓦片按跟车视角加载，禁用时解除注册。
using CesiumForUnity;
using UnityEngine;

// Third-person chase camera with NaN/teleport guards.
// Refuses to follow invalid or exploding target positions.
public class CarFollowCamera : MonoBehaviour
{
    public Transform target;
    public float distance = 6.5f;
    public float height = 2.8f;
    public float positionDamping = 5f;
    public float rotationDamping = 4f;
    public float lookHeightOffset = 1.2f;
    public CesiumGeoreference georeference;

    CesiumCameraManager cameraManager;
    Camera carCamera;
    bool cameraAdded;
    public float maxValidCoordinate = 100000f; // 超过视为物理爆炸，停止跟随

    void OnEnable()
    {
        RegisterCesiumCamera();
    }

    void Start()
    {
        RegisterCesiumCamera();
    }

    void OnDisable()
    {
        RemoveCesiumCamera();
    }

    void RegisterCesiumCamera()
    {
        if (carCamera == null) carCamera = GetComponent<Camera>();
        if (georeference == null) georeference = FindFirstObjectByType<CesiumGeoreference>();
        if (carCamera == null || georeference == null) return;

        cameraManager = CesiumCameraManager.GetOrCreate(georeference.gameObject);
        if (cameraManager == null || cameraManager.additionalCameras.Contains(carCamera)) return;

        cameraManager.additionalCameras.Add(carCamera);
        cameraAdded = true;
    }

    void RemoveCesiumCamera()
    {
        if (cameraAdded && cameraManager != null)
            cameraManager.additionalCameras.Remove(carCamera);

        cameraAdded = false;
        cameraManager = null;
    }

    void LateUpdate()
    {
        if (target == null) return;

        Vector3 tp = target.position;

        // 防线1：目标坐标含 NaN/Infinity → 本帧不动
        if (float.IsNaN(tp.x) || float.IsNaN(tp.y) || float.IsNaN(tp.z) ||
            float.IsInfinity(tp.x) || float.IsInfinity(tp.y) || float.IsInfinity(tp.z))
        {
            Debug.LogWarning("[CarFollowCamera] Target position is NaN/Inf, holding camera.");
            return;
        }

        // 防线2：目标飞出合理范围（物理爆炸/无限坠落）→ 本帧不动
        if (Mathf.Abs(tp.x) > maxValidCoordinate ||
            Mathf.Abs(tp.y) > maxValidCoordinate ||
            Mathf.Abs(tp.z) > maxValidCoordinate)
        {
            Debug.LogWarning("[CarFollowCamera] Target out of valid range: " + tp);
            return;
        }

        float targetYaw = target.eulerAngles.y;
        Quaternion flatRotation = Quaternion.Euler(0f, targetYaw, 0f);

        Vector3 desiredPosition = tp
                                  - flatRotation * Vector3.forward * distance
                                  + Vector3.up * height;

        float dt = Mathf.Min(Time.deltaTime, 0.05f); // 防线3：限制单帧步长
        transform.position = Vector3.Lerp(transform.position, desiredPosition, positionDamping * dt);

        Vector3 lookDir = (tp + Vector3.up * lookHeightOffset) - transform.position;
        if (lookDir.sqrMagnitude < 0.0001f) return; // LookRotation 零向量保护

        Quaternion desiredRotation = Quaternion.LookRotation(lookDir);
        transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, rotationDamping * dt);
    }
}
