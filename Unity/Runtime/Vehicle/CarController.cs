using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public class CarController : MonoBehaviour
{
    [Header("Driving Settings")]
    public float motorForce = 8000f;        // 引擎力（N）
    public float brakeForce = 15000f;       // 刹车力
    public float maxSpeed = 25f;            // 最大前进速度 (m/s)
    public float maxReverseSpeed = 10f;     // 最大倒车速度

    [Header("Steering")]
    public float maxSteerAngle = 35f;       // 最大前轮转角（度）
    public float steeringResponse = 5f;     // 方向盘响应速度
    public float turnRadiusFactor = 4f;     // 转弯半径系数（越小半径越小）

    [Header("Physics")]
    public float drag = 0.3f;
    public float groundedDrag = 1.5f;
    public float sidewaysFriction = 5f;     // 侧向摩擦（每秒消除侧滑的比例系数）
    public float absoluteMaxSpeed = 80f;    // 速度硬上限（m/s），防物理爆炸

    [Header("Auto Collider Setup")]
    public bool autoSetupColliders = true;  // 自动禁用 WheelCollider 并生成车身 BoxCollider
    public float bodyFriction = 0.1f;       // 车身碰撞体摩擦系数

    [Header("Current State (Read-Only)")]
    public float currentSpeed;              // km/h
    public float currentAcceleration;
    public float throttleInput;             // -1..1
    public float steerInput;                // -1..1
    public float currentSteerAngle;         // 实际前轮转角

    private Rigidbody rb;
    private float previousSpeed;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.isKinematic = false;
        rb.useGravity = true;
        rb.linearDamping = drag;
        rb.angularDamping = 3f;
        rb.centerOfMass = new Vector3(0, -0.5f, 0);
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        if (autoSetupColliders)
        {
            SetupColliders();
        }
    }

    // 禁用 prefab 自带的 WheelCollider（无人供给 motorTorque 时它们等于抱死的轮胎，
    // 会抵抗 AddForce 推进并产生翘头力矩），改用一个包住整车的低摩擦 BoxCollider。
    void SetupColliders()
    {
        // 1. 禁用所有 WheelCollider
        WheelCollider[] wheels = GetComponentsInChildren<WheelCollider>(true);
        foreach (WheelCollider wc in wheels)
        {
            wc.enabled = false;
        }
        if (wheels.Length > 0)
        {
            Debug.Log("[CarController] Disabled " + wheels.Length + " WheelCollider(s).");
        }

        // 2. 低摩擦物理材质（抓地与减速由脚本的侧向摩擦和发动机制动接管）
        PhysicsMaterial bodyMat = new PhysicsMaterial("CarBodyAuto");
        bodyMat.dynamicFriction = bodyFriction;
        bodyMat.staticFriction = bodyFriction;
        bodyMat.frictionCombine = PhysicsMaterialCombine.Minimum;
        bodyMat.bounceCombine = PhysicsMaterialCombine.Minimum;
        bodyMat.bounciness = 0f;

        // 3. 车身 BoxCollider：尺寸取所有 Renderer 的联合包围盒（含轮胎，底面即轮胎最低点）
        BoxCollider body = GetComponent<BoxCollider>();
        if (body == null)
        {
            body = gameObject.AddComponent<BoxCollider>();

            Renderer[] renderers = GetComponentsInChildren<Renderer>(false);
            if (renderers.Length > 0)
            {
                Bounds worldBounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                {
                    worldBounds.Encapsulate(renderers[i].bounds);
                }
                // 世界包围盒转回本物体局部空间（车未旋转时进行，Start 阶段成立）
                body.center = transform.InverseTransformPoint(worldBounds.center);
                Vector3 lossy = transform.lossyScale;
                body.size = new Vector3(
                    worldBounds.size.x / Mathf.Max(lossy.x, 0.0001f),
                    worldBounds.size.y / Mathf.Max(lossy.y, 0.0001f),
                    worldBounds.size.z / Mathf.Max(lossy.z, 0.0001f));
                Debug.Log("[CarController] Auto BoxCollider center=" + body.center + " size=" + body.size);
            }
        }
        body.material = bodyMat;
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        throttleInput = 0f;
        steerInput = 0f;

        if (kb.wKey.isPressed || kb.upArrowKey.isPressed)    throttleInput = 1f;
        if (kb.sKey.isPressed || kb.downArrowKey.isPressed)  throttleInput = -1f;
        if (kb.aKey.isPressed || kb.leftArrowKey.isPressed)  steerInput = -1f;
        if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) steerInput = 1f;
    }

    void FixedUpdate()
    {
        // —— 1. 计算当前速度（考虑前进/后退）——
        Vector3 velocity = rb.linearVelocity;
        float forwardSpeed = Vector3.Dot(velocity, transform.forward);   // 正=前进，负=后退
        currentSpeed = velocity.magnitude * 3.6f;

        currentAcceleration = (velocity.magnitude - previousSpeed) / Time.fixedDeltaTime;
        previousSpeed = velocity.magnitude;

        // —— 2. 油门/刹车 ——
        if (throttleInput > 0f)
        {
            // 前进
            if (forwardSpeed < maxSpeed)
            {
                rb.AddForce(transform.forward * motorForce * throttleInput, ForceMode.Force);
            }
        }
        else if (throttleInput < 0f)
        {
            if (forwardSpeed > 0.5f)
            {
                // 还在前进中，S 先起刹车作用
                rb.AddForce(-transform.forward * brakeForce, ForceMode.Force);
            }
            else if (forwardSpeed > -maxReverseSpeed)
            {
                // 停住或已经在倒车，继续倒车
                rb.AddForce(transform.forward * motorForce * throttleInput * 0.6f, ForceMode.Force);
            }
        }

        // 发动机制动（没踩油门时自然减速）
        rb.linearDamping = (throttleInput == 0f) ? groundedDrag : drag;

        // —— 3. 手刹 ——
        var kb = Keyboard.current;
        if (kb != null && kb.spaceKey.isPressed)
        {
            rb.linearVelocity *= 0.92f;
        }

        // —— 4. 方向盘平滑响应 ——
        float targetSteerAngle = steerInput * maxSteerAngle;
        currentSteerAngle = Mathf.Lerp(currentSteerAngle, targetSteerAngle,
                                       steeringResponse * Time.fixedDeltaTime);

        // —— 5. 基于行驶方向的真实转向 ——
        if (Mathf.Abs(forwardSpeed) > 0.3f)
        {
            // turnRate 单位是 弧度/秒，车尾跟手在倒车时自动成立（因 forwardSpeed 变负）
            float turnRateRadPerSec = (forwardSpeed / turnRadiusFactor)
                                    * Mathf.Tan(currentSteerAngle * Mathf.Deg2Rad);

            float deltaDegrees = turnRateRadPerSec * Mathf.Rad2Deg * Time.fixedDeltaTime;

            Quaternion turnRotation = Quaternion.Euler(0f, deltaDegrees, 0f);
            rb.MoveRotation(rb.rotation * turnRotation);
        }

        // —— 6. 侧向摩擦（防止车打滑）——
        // VelocityChange 不乘时间步长，必须自己乘 fixedDeltaTime 并钳制在 [0,1]，
        // 否则每帧消除量超过侧向速度本身会反向过冲并指数放大（旧版崩溃根因）。
        Vector3 rightVelocity = transform.right * Vector3.Dot(velocity, transform.right);
        float grip = Mathf.Clamp01(sidewaysFriction * Time.fixedDeltaTime);
        rb.AddForce(-rightVelocity * grip, ForceMode.VelocityChange);

        // —— 7. 速度安全钳制（防物理爆炸传播）——
        if (rb.linearVelocity.magnitude > absoluteMaxSpeed)
        {
            rb.linearVelocity = rb.linearVelocity.normalized * absoluteMaxSpeed;
        }
    }
}