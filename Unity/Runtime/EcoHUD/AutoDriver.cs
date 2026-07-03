// =============================================================================
//  AutoDriver.cs
//  Copyright (c) 2026 Yike Zhang. COMP0190 P87, UCL CS (supervisor: Dr Mark Colley).
//
//  DEPLOY: class name is kept as `AutoDriver` so this file's contents replace
//  Assets/Scripts/EcoHUD/AutoDriver.cs in place (the car's component + RoundController
//  reference stay valid; the .meta / GUID is untouched). The archive copy is named
//  AutoDriver_v2.cs; v1 is kept as AutoDriver.cs in the same folder for reference.
//
// =============================================================================

using UnityEngine;

[DefaultExecutionOrder(100)]
public class AutoDriver : MonoBehaviour
{
    [Header("Source")]
    public CarController car;                  // auto-found if null

    [Header("Driving (tuned to hug bends, not clip the kerb)")]
    public bool engaged = true;                // off = keyboard drives
    public float targetSpeedKmh = 16f;
    public float lookAhead = 6f;               // metres ahead along the path to aim at (small = hugs the line)
    public float fullSteerAngle = 24f;         // heading error (deg) giving full lock (small = commits to turns sooner)
    [Range(0f, 1f)] public float turnSlowdown = 0.7f;
    public float cornerSpeedKmh = 10f;         // brakes down to this through the sharpest corners
    public float cornerBendDeg = 55f;          // summed upcoming bend (deg) over the look window that forces the full slowdown
    public float cornerLookAhead = 16f;        // metres of path scanned ahead to anticipate a bend (so it brakes early)
    public bool loop = true;
    public bool snapToStartOnPlay = true;

    [Header("Record mode: tick this + untick 'engaged', drive the loop by keyboard; it writes Assets/recorded_route.txt")]
    public bool recordMode = false;
    public float recordSpacing = 2f;           // log a point every this many metres driven
    System.Text.StringBuilder _rec;
    Vector2 _lastRec = new(9e9f, 9e9f);

    public int laps { get; private set; }
    public event System.Action onLapComplete;

    // Route snapped to CityGen road centrelines (see header). Same loop as v1. World XZ.
    static readonly Vector2[] route = {
        new(552.27f,99.98f), new(551.32f,99.76f), new(550.40f,99.97f), new(549.45f,100.54f), new(548.29f,101.34f), new(547.23f,102.35f), new(546.21f,103.45f), new(545.19f,104.55f),
        new(544.18f,105.66f), new(543.17f,106.77f), new(542.17f,107.89f), new(541.19f,109.02f), new(540.20f,110.15f), new(539.22f,111.29f), new(538.24f,112.43f), new(537.26f,113.56f),
        new(536.28f,114.70f), new(535.30f,115.83f), new(534.32f,116.97f), new(533.35f,118.10f), new(532.37f,119.24f), new(531.39f,120.38f), new(530.41f,121.51f), new(529.43f,122.65f),
        new(528.45f,123.78f), new(527.47f,124.92f), new(526.49f,126.06f), new(525.51f,127.19f), new(524.53f,128.33f), new(523.55f,129.46f), new(522.57f,130.60f), new(521.59f,131.73f),
        new(520.61f,132.87f), new(519.63f,134.01f), new(518.65f,135.14f), new(517.67f,136.28f), new(516.69f,137.41f), new(515.71f,138.55f), new(514.73f,139.69f), new(513.75f,140.82f),
        new(512.77f,141.96f), new(511.79f,143.09f), new(510.81f,144.23f), new(509.84f,145.37f), new(508.86f,146.50f), new(507.88f,147.64f), new(506.90f,148.78f), new(505.92f,149.91f),
        new(504.94f,151.05f), new(503.96f,152.19f), new(502.97f,153.31f), new(501.96f,154.42f), new(500.91f,155.49f), new(499.84f,156.53f), new(498.72f,157.53f), new(497.58f,158.50f),
        new(496.43f,159.47f), new(495.29f,160.43f), new(494.14f,161.40f), new(492.99f,162.36f), new(491.84f,163.33f), new(490.69f,164.29f), new(489.54f,165.25f), new(488.39f,166.22f),
        new(487.24f,167.18f), new(486.09f,168.15f), new(484.94f,169.11f), new(483.80f,170.08f), new(482.65f,171.04f), new(481.50f,172.00f), new(480.58f,173.07f), new(479.92f,174.24f),
        new(479.52f,175.51f), new(479.51f,176.82f), new(479.96f,178.14f), new(480.62f,179.37f), new(481.48f,180.50f), new(482.54f,181.53f), new(483.65f,182.54f), new(484.75f,183.56f),
        new(485.84f,184.59f), new(486.92f,185.63f), new(488.00f,186.67f), new(489.07f,187.72f), new(490.15f,188.77f), new(491.22f,189.81f), new(492.30f,190.86f), new(493.37f,191.91f),
        new(494.45f,192.95f), new(495.52f,194.00f), new(496.59f,195.05f), new(497.67f,196.09f), new(498.74f,197.14f), new(499.82f,198.19f), new(500.89f,199.23f), new(501.97f,200.28f),
        new(503.04f,201.33f), new(504.12f,202.37f), new(505.19f,203.42f), new(506.27f,204.47f), new(507.34f,205.51f), new(508.41f,206.56f), new(509.49f,207.61f), new(510.56f,208.65f),
        new(511.64f,209.70f), new(512.71f,210.75f), new(513.79f,211.79f), new(514.86f,212.84f), new(515.94f,213.89f), new(517.01f,214.93f), new(518.09f,215.98f), new(519.16f,217.03f),
        new(520.23f,218.07f), new(521.31f,219.12f), new(522.39f,220.16f), new(523.47f,221.20f), new(524.55f,222.24f), new(525.63f,223.28f), new(526.72f,224.31f), new(527.80f,225.35f),
        new(528.89f,226.39f), new(529.97f,227.42f), new(531.06f,228.46f), new(532.14f,229.50f), new(533.23f,230.53f), new(534.31f,231.57f), new(535.39f,232.60f), new(536.48f,233.64f),
        new(537.56f,234.68f), new(538.65f,235.71f), new(539.73f,236.75f), new(540.82f,237.78f), new(541.90f,238.82f), new(542.99f,239.86f), new(544.07f,240.89f), new(545.16f,241.93f),
        new(546.24f,242.97f), new(547.32f,244.00f), new(548.41f,245.04f), new(549.49f,246.07f), new(550.58f,247.11f), new(551.66f,248.15f), new(552.75f,249.18f), new(553.83f,250.22f),
        new(554.92f,251.25f), new(556.00f,252.29f), new(557.09f,253.33f), new(558.17f,254.36f), new(559.26f,255.40f), new(560.34f,256.44f), new(561.42f,257.47f), new(562.51f,258.51f),
        new(563.59f,259.54f), new(564.68f,260.58f), new(565.76f,261.62f), new(566.85f,262.65f), new(567.93f,263.69f), new(569.02f,264.72f), new(570.10f,265.76f), new(571.19f,266.80f),
        new(572.27f,267.83f), new(573.36f,268.87f), new(574.44f,269.91f), new(575.52f,270.94f), new(576.61f,271.98f), new(577.69f,273.01f), new(578.78f,274.05f), new(579.86f,275.09f),
        new(580.95f,276.12f), new(582.03f,277.16f), new(583.12f,278.19f), new(584.20f,279.23f), new(585.29f,280.27f), new(586.38f,281.29f), new(587.48f,282.31f), new(588.60f,283.31f),
        new(589.72f,284.30f), new(590.86f,285.28f), new(592.00f,286.25f), new(593.14f,287.23f), new(594.28f,288.20f), new(595.43f,289.18f), new(596.57f,290.15f), new(597.71f,291.12f),
        new(598.85f,292.10f), new(599.99f,293.07f), new(601.13f,294.04f), new(602.27f,295.02f), new(603.41f,295.99f), new(604.55f,296.97f), new(605.69f,297.94f), new(606.83f,298.91f),
        new(607.98f,299.89f), new(609.12f,300.86f), new(610.26f,301.83f), new(611.40f,302.81f), new(612.54f,303.78f), new(613.68f,304.76f), new(614.82f,305.73f), new(615.96f,306.70f),
        new(617.10f,307.68f), new(618.24f,308.65f), new(619.39f,309.62f), new(620.53f,310.60f), new(621.67f,311.57f), new(622.81f,312.55f), new(623.95f,313.52f), new(625.09f,314.49f),
        new(626.23f,315.47f), new(627.37f,316.44f), new(628.51f,317.41f), new(629.65f,318.39f), new(630.79f,319.36f), new(631.94f,320.34f), new(633.08f,321.31f), new(634.22f,322.28f),
        new(635.36f,323.26f), new(636.50f,324.23f), new(637.64f,325.21f), new(638.78f,326.18f), new(639.92f,327.15f), new(641.06f,328.13f), new(642.20f,329.10f), new(643.34f,330.07f),
        new(644.49f,331.05f), new(645.63f,332.02f), new(646.77f,333.00f), new(647.91f,333.97f), new(649.05f,334.94f), new(650.19f,335.91f), new(651.36f,336.85f), new(652.55f,337.76f),
        new(653.77f,338.63f), new(655.01f,339.47f), new(656.28f,340.28f), new(657.54f,341.08f), new(658.81f,341.88f), new(660.07f,342.69f), new(661.34f,343.49f), new(662.60f,344.30f),
        new(663.87f,345.10f), new(665.14f,345.91f), new(666.40f,346.71f), new(667.67f,347.52f), new(668.93f,348.32f), new(670.20f,349.13f), new(671.46f,349.93f), new(672.73f,350.74f),
        new(674.00f,351.54f), new(675.27f,352.34f), new(676.54f,353.13f), new(677.81f,353.93f), new(679.09f,354.72f), new(680.36f,355.51f), new(681.64f,356.29f), new(682.90f,356.99f),
        new(684.09f,357.29f), new(685.21f,357.19f), new(686.26f,356.71f), new(687.24f,355.83f), new(688.17f,354.65f), new(689.10f,353.47f), new(690.03f,352.30f), new(690.96f,351.12f),
        new(691.89f,349.94f), new(692.74f,348.72f), new(693.52f,347.46f), new(694.22f,346.15f), new(694.84f,344.79f), new(695.38f,343.39f), new(695.94f,342.00f), new(696.50f,340.61f),
        new(697.07f,339.22f), new(697.65f,337.84f), new(698.23f,336.46f), new(698.82f,335.08f), new(699.40f,333.70f), new(699.99f,332.32f), new(700.58f,330.93f), new(701.16f,329.55f),
        new(701.75f,328.17f), new(702.33f,326.79f), new(702.92f,325.41f), new(703.51f,324.03f), new(704.09f,322.65f), new(704.68f,321.27f), new(705.26f,319.89f), new(705.85f,318.51f),
        new(706.44f,317.13f), new(707.02f,315.75f), new(707.61f,314.37f), new(708.20f,312.99f), new(708.80f,311.61f), new(709.40f,310.24f), new(710.02f,308.87f), new(710.63f,307.50f),
        new(711.24f,306.13f), new(711.86f,304.76f), new(712.47f,303.40f), new(713.09f,302.03f), new(713.70f,300.66f), new(714.31f,299.29f), new(714.93f,297.92f), new(715.54f,296.55f),
        new(716.15f,295.18f), new(716.77f,293.81f), new(717.38f,292.45f), new(717.99f,291.08f), new(718.61f,289.71f), new(719.22f,288.34f), new(719.83f,286.97f), new(720.45f,285.60f),
        new(721.06f,284.23f), new(721.67f,282.86f), new(722.29f,281.49f), new(722.90f,280.13f), new(723.51f,278.76f), new(724.13f,277.39f), new(724.74f,276.02f), new(725.35f,274.65f),
        new(725.97f,273.28f), new(726.58f,271.91f), new(727.19f,270.54f), new(727.81f,269.17f), new(728.42f,267.81f), new(729.04f,266.44f), new(729.65f,265.07f), new(730.27f,263.70f),
        new(730.88f,262.33f), new(731.50f,260.96f), new(732.11f,259.60f), new(732.73f,258.23f), new(733.20f,256.85f), new(733.46f,255.47f), new(733.49f,254.07f), new(733.31f,252.66f),
        new(732.89f,251.26f), new(732.27f,249.94f), new(731.50f,248.71f), new(730.60f,247.58f), new(729.57f,246.54f), new(728.42f,245.58f), new(727.26f,244.62f), new(726.09f,243.68f),
        new(724.92f,242.75f), new(723.74f,241.82f), new(722.56f,240.89f), new(721.38f,239.97f), new(720.20f,239.04f), new(719.02f,238.12f), new(717.84f,237.19f), new(716.66f,236.27f),
        new(715.48f,235.34f), new(714.30f,234.41f), new(713.11f,233.49f), new(711.93f,232.56f), new(710.75f,231.64f), new(709.57f,230.71f), new(708.39f,229.79f), new(707.21f,228.86f),
        new(706.03f,227.94f), new(704.85f,227.01f), new(703.67f,226.08f), new(702.49f,225.16f), new(701.31f,224.23f), new(700.13f,223.31f), new(698.95f,222.38f), new(697.77f,221.46f),
        new(696.59f,220.53f), new(695.41f,219.61f), new(694.23f,218.68f), new(693.05f,217.75f), new(691.87f,216.83f), new(690.69f,215.90f), new(689.51f,214.97f), new(688.34f,214.04f),
        new(687.16f,213.11f), new(685.98f,212.18f), new(684.80f,211.25f), new(683.62f,210.33f), new(682.45f,209.40f), new(681.27f,208.47f), new(680.09f,207.54f), new(678.91f,206.61f),
        new(677.74f,205.68f), new(676.57f,204.74f), new(675.39f,203.81f), new(674.22f,202.87f), new(673.05f,201.93f), new(671.88f,201.00f), new(670.71f,200.06f), new(669.53f,199.12f),
        new(668.36f,198.19f), new(667.19f,197.25f), new(666.02f,196.31f), new(664.85f,195.38f), new(663.68f,194.44f), new(662.50f,193.50f), new(661.33f,192.57f), new(660.16f,191.63f),
        new(658.98f,190.70f), new(657.80f,189.78f), new(656.62f,188.85f), new(655.44f,187.93f), new(654.25f,187.01f), new(653.07f,186.09f), new(651.89f,185.17f), new(650.70f,184.24f),
        new(649.52f,183.32f), new(648.34f,182.39f), new(647.16f,181.47f), new(645.98f,180.54f), new(644.80f,179.62f), new(643.62f,178.70f), new(642.43f,177.78f), new(641.24f,176.87f),
        new(640.03f,175.98f), new(638.82f,175.09f), new(637.61f,174.22f), new(636.41f,173.32f), new(635.23f,172.39f), new(634.07f,171.44f), new(632.93f,170.47f), new(631.80f,169.48f),
        new(630.68f,168.48f), new(629.55f,167.49f), new(628.43f,166.50f), new(627.30f,165.51f), new(626.18f,164.51f), new(625.05f,163.52f), new(623.93f,162.53f), new(622.80f,161.54f),
        new(621.68f,160.54f), new(620.56f,159.55f), new(619.43f,158.56f), new(618.31f,157.57f), new(617.18f,156.58f), new(616.06f,155.58f), new(614.93f,154.59f), new(613.81f,153.60f),
        new(612.68f,152.61f), new(611.56f,151.61f), new(610.43f,150.62f), new(609.30f,149.63f), new(608.18f,148.64f), new(607.05f,147.65f), new(605.92f,146.67f), new(604.79f,145.68f),
        new(603.66f,144.70f), new(602.53f,143.71f), new(601.40f,142.72f), new(600.27f,141.74f), new(599.13f,140.75f), new(598.00f,139.77f), new(596.87f,138.78f), new(595.74f,137.80f),
        new(594.61f,136.81f), new(593.48f,135.83f), new(592.35f,134.84f), new(591.22f,133.85f), new(590.09f,132.87f), new(588.96f,131.88f), new(587.83f,130.90f), new(586.70f,129.91f),
        new(585.57f,128.92f), new(584.44f,127.93f), new(583.32f,126.94f), new(582.19f,125.95f), new(581.06f,124.96f), new(579.94f,123.97f), new(578.81f,122.98f), new(577.68f,121.99f),
        new(576.56f,121.00f), new(575.43f,120.01f), new(574.30f,119.02f), new(573.18f,118.03f), new(572.05f,117.04f), new(570.93f,116.04f), new(569.80f,115.05f), new(568.67f,114.06f),
        new(567.55f,113.07f), new(566.42f,112.08f), new(565.29f,111.09f), new(564.17f,110.10f), new(563.04f,109.11f), new(561.92f,108.12f), new(560.79f,107.13f), new(559.66f,106.14f),
        new(558.53f,105.15f), new(557.40f,104.16f), new(556.27f,103.18f), new(555.14f,102.20f), new(554.06f,101.26f), new(553.20f,100.51f),
    };

    int nearIdx;

    void Awake() { if (car == null) car = FindFirstObjectByType<CarController>(); }

    void Start() { if (snapToStartOnPlay) SnapToStart(); }

    static Vector2 Pt(int i) => route[((i % route.Length) + route.Length) % route.Length];

    // Place the car on route[0] AND face it along the route, so it starts cleanly.
    public void SnapToStart()
    {
        if (car == null) return;
        Vector2 p0 = Pt(0), p1 = Pt(1);
        Vector3 pos = new(p0.x, car.transform.position.y, p0.y);
        Vector3 dir = new(p1.x - p0.x, 0f, p1.y - p0.y);
        Quaternion rot = dir.sqrMagnitude > 0.001f ? Quaternion.LookRotation(dir, Vector3.up) : car.transform.rotation;

        car.transform.SetPositionAndRotation(pos, rot);
        var rb = car.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.position = pos; rb.rotation = rot;
            rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero;
        }
        nearIdx = 0;
    }

    public void ResetRoute() { nearIdx = 0; laps = 0; }

    void Update()
    {
        if (car == null) return;

        if (recordMode)
        {
            Vector2 xz = new(car.transform.position.x, car.transform.position.z);
            if ((xz - _lastRec).magnitude >= recordSpacing)
            {
                _lastRec = xz;
                _rec ??= new System.Text.StringBuilder();
                _rec.Append($"new({xz.x:F1}f,{xz.y:F1}f), ");
                System.IO.File.WriteAllText(Application.dataPath + "/recorded_route.txt", _rec.ToString());
            }
            return;   // don't auto-drive while recording; CarController + keyboard drives
        }

        if (!engaged) return;

        Vector2 carXZ = new(car.transform.position.x, car.transform.position.z);

        // advance the tracked index to the closest point ahead (local window, keeps progress monotonic)
        int bestI = nearIdx; float bestD = float.MaxValue;
        for (int k = -2; k <= 10; k++)
        {
            int i = nearIdx + k;
            float d = (Pt(i) - carXZ).sqrMagnitude;
            if (d < bestD) { bestD = d; bestI = i; }
        }
        if (bestI / route.Length > nearIdx / route.Length) { laps++; onLapComplete?.Invoke(); }
        nearIdx = ((bestI % route.Length) + route.Length) % route.Length;

        // look-ahead target: walk forward from the closest point until lookAhead metres
        Vector2 target = Pt(bestI);
        float acc = (target - carXZ).magnitude;
        int j = bestI;
        while (acc < lookAhead)
        {
            Vector2 a = Pt(j), b = Pt(j + 1);
            acc += (b - a).magnitude; j++;
            target = b;
            if (!loop && (j % route.Length) == route.Length - 1) break;
        }

        // steer toward target (XZ)
        Vector3 fwd = car.transform.forward; fwd.y = 0f;
        Vector3 to = new(target.x - carXZ.x, 0f, target.y - carXZ.y);
        float ang = Vector3.SignedAngle(fwd.normalized, to.normalized, Vector3.up);
        float steer = Mathf.Clamp(ang / Mathf.Max(1f, fullSteerAngle), -1f, 1f);

        // anticipate the upcoming bend and brake INTO the corner (human-like: slow before turning)
        float bendAhead = 0f, scan = 0f; int b0 = bestI;
        while (scan < cornerLookAhead)
        {
            Vector2 pa = Pt(b0), pb = Pt(b0 + 1), pc = Pt(b0 + 2);
            bendAhead += Vector2.Angle(pb - pa, pc - pb);
            scan += (pb - pa).magnitude; b0++;
            if (!loop && (b0 % route.Length) == route.Length - 1) break;
        }
        float cornerCap = Mathf.Lerp(targetSpeedKmh, cornerSpeedKmh, Mathf.Clamp01(bendAhead / Mathf.Max(1f, cornerBendDeg)));

        // throttle: hold target speed, ease off for heading error, and never exceed the corner cap
        float tgt = targetSpeedKmh * Mathf.Clamp01(1f - Mathf.Abs(ang) / 90f * turnSlowdown);
        tgt = Mathf.Min(tgt, cornerCap);
        float sp = car.currentSpeed;
        float throttle = sp < tgt - 1f ? 1f : (sp > tgt + 1f ? -1f : 0f);

        car.steerInput = steer;
        car.throttleInput = throttle;
    }

    void OnDrawGizmos()
    {
        Gizmos.color = Color.cyan;
        float y = car != null ? car.transform.position.y : 58f;
        for (int i = 0; i < route.Length; i++)
        {
            Vector3 a = new(route[i].x, y, route[i].y);
            Vector3 b = new(route[(i + 1) % route.Length].x, y, route[(i + 1) % route.Length].y);
            Gizmos.DrawLine(a, b);
        }
    }
}
