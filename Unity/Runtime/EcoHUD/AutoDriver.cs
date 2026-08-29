using UnityEngine;

[DefaultExecutionOrder(100)]
public class AutoDriver : MonoBehaviour
{
    [Header("Source")]
    public CarController car;                  // auto-found if null

    [Header("Driving (tuned to hug bends, not clip the kerb)")]
    public bool engaged = true;                // off = keyboard drives
    public float targetSpeedKmh = 40f;
    public float lookAhead = 6f;
    public float fullSteerAngle = 24f;
    [Range(0f, 1f)] public float turnSlowdown = 0.5f;
    public float cornerSpeedKmh = 15f;
    public float cornerBendDeg = 75f;
    public float cornerLookAhead = 16f;
    public bool loop = true;
    public bool snapToStartOnPlay = true;

    [Header("Lane keeping (needed once two-way ambient traffic exists)")]
    [Tooltip("Metres the driven line is shifted to the LEFT of the road centreline (UK keep-left). 0 = drive the centreline. Negative = right.")]
    public float laneOffsetM = 1.75f;

    [Header("Traffic ahead (ease off instead of ramming slower ambient cars)")]
    public bool trafficBrake = true;
    public float trafficDetectM = 16f;
    public float trafficStopM = 5f;
    public LayerMask trafficMask = 1 << 9;

    [Header("Meeting oncoming traffic (narrow two-way streets)")]
    [Tooltip("Ease the pursuit target to the right and slow to meetSpeedKmh while an oncoming Gley vehicle is close ahead; smoothly recover ~1 s after it clears. Never reverses, never commands a full stop.")]
    public bool meetPull = true;
    public float meetDetectM = 18f;
    public float meetLateralM = 2.2f;
    public float meetOffsetM = 0.8f;
    public float meetSpeedKmh = 15f;           // speed cap while meeting
    float _meetBlend;                          // smoothed 0..1 activation
    float _meetLastSeen = -999f;
    readonly Collider[] _meetHits = new Collider[8];

    [Header("Walls (invisible road-boundary walls line every carriageway)")]
    [Tooltip("Whisker rays feel for the boundary walls; steering is biased toward the freer side. Narrow streets + the lane offset can otherwise wedge the car against a wall.")]
    public bool wallAvoid = true;
    public LayerMask wallMask = 1 | (1 << 29) | (1 << 30);
    public float whiskerLen = 5f;
    [Tooltip("Full throttle but standing still for this long = stuck. The autopilot NEVER reverses (real drivers don't reverse mid-road) — it holds, logs, and RoundController may end the round via stuckSeconds.")]
    public float wedgeSeconds = 4f;
    [Tooltip("Read-only: how long the autopilot has been pushing without moving. RoundController ends the round when this passes its own threshold.")]
    public float stuckSeconds;
    [Tooltip("Read-only: whether the current stall has a traffic vehicle ahead (a queue — be patient) or not (geometry wedge — abort sooner).")]
    public bool stuckTrafficAhead;
    float _wedgeTime;

    [Header("Record mode: tick this + untick 'engaged', drive the loop by keyboard; it writes Assets/recorded_route.txt")]
    public bool recordMode = false;
    public float recordSpacing = 2f;
    System.Text.StringBuilder _rec;
    Vector2 _lastRec = new(9e9f, 9e9f);

    public int laps { get; private set; }
    public event System.Action onLapComplete;

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

    Vector2[] _pts;
    Vector2[] _drive;
    bool _arrived;
    public event System.Action onRouteComplete;

    public void SetRoute(Vector2[] pts, bool loopRoute) => SetRoute(pts, null, loopRoute);

    public void SetRoute(Vector2[] pts, float[] halfw, bool loopRoute)
    {
        DensifyWith(pts, halfw != null && halfw.Length == pts.Length ? halfw : null,
                    2f, out _pts, out _halfwDense);
        loop = loopRoute;
        BuildDriveLine();
        nearIdx = 0;
        laps = 0;
        _arrived = false;
        _holdBrake = false;
    }

    float[] _halfwDense;
    bool _holdBrake;

    static void DensifyWith(Vector2[] pts, float[] hw, float maxStep,
                            out Vector2[] outPts, out float[] outHw)
    {
        if (pts == null || pts.Length < 2) { outPts = pts; outHw = null; return; }
        var lp = new System.Collections.Generic.List<Vector2>(pts.Length * 4);
        var lh = hw != null ? new System.Collections.Generic.List<float>(pts.Length * 4) : null;
        for (int i = 0; i < pts.Length - 1; i++)
        {
            Vector2 a = pts[i], b = pts[i + 1];
            lp.Add(a); lh?.Add(hw[i]);
            int steps = Mathf.FloorToInt((b - a).magnitude / maxStep);
            for (int s = 1; s <= steps; s++)
            {
                float t = (float)s / (steps + 1);
                lp.Add(Vector2.Lerp(a, b, t));
                lh?.Add(Mathf.Lerp(hw[i], hw[i + 1], t));
            }
        }
        lp.Add(pts[pts.Length - 1]); lh?.Add(hw[hw.Length - 1]);
        outPts = lp.ToArray(); outHw = lh?.ToArray();
    }

    void BuildDriveLine()
    {
        int n = _pts.Length;
        _drive = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            Vector2 prev = loop ? _pts[(i - 1 + n) % n] : _pts[Mathf.Max(0, i - 1)];
            Vector2 next = loop ? _pts[(i + 1) % n] : _pts[Mathf.Min(n - 1, i + 1)];
            Vector2 dir = (next - prev).normalized;

            float off = laneOffsetM;
            if (_halfwDense != null && i < _halfwDense.Length)
            {
                float hw = _halfwDense[i];
                off = Mathf.Min(Mathf.Max(hw * 0.5f, 1.1f), laneOffsetM);
                off = Mathf.Min(off, Mathf.Max(0.5f, hw - 1.35f));
            }
            int i0 = loop ? (i - 2 + n) % n : Mathf.Max(0, i - 2);
            int i1 = loop ? (i + 2) % n : Mathf.Min(n - 1, i + 2);
            Vector2 v1 = _pts[i] - _pts[i0], v2 = _pts[i1] - _pts[i];
            if (v1.sqrMagnitude > 1e-3f && v2.sqrMagnitude > 1e-3f)
            {
                float bend = Vector2.Angle(v1, v2);
                off *= Mathf.Lerp(1f, 0.45f, Mathf.Clamp01(bend / 30f));
            }
            _drive[i] = _pts[i] + new Vector2(dir.y, -dir.x) * off; // RIGHT of travel
        }
    }

    void Awake()
    {
        if (car == null) car = FindFirstObjectByType<CarController>();
        if (_pts == null)
        {
            Debug.LogWarning("[AutoDriver] No designated route set — falling back to the BAKED LEGACY LOOP. " +
                             "If this is a study round, RouteSet failed to load its startNode.");
            SetRoute(route, true);
        }
    }

    void Start() { if (snapToStartOnPlay) SnapToStart(); }

    Vector2 Pt(int i)
    {
        if (_drive == null) { _pts = route; BuildDriveLine(); }
        int n = _drive.Length;
        return loop ? _drive[((i % n) + n) % n] : _drive[Mathf.Clamp(i, 0, n - 1)];
    }

    public void SnapToStart()
    {
        if (car == null) return;
        Vector2 p0 = Pt(0), p1 = Pt(1);
        float y = car.transform.position.y;
        int groundMask = (1 << 30) | (1 << 15);   // Highway | Landscape
        if (Physics.Raycast(new Vector3(p0.x, 300f, p0.y), Vector3.down,
                            out RaycastHit hit, 600f, groundMask))
            y = hit.point.y + 0.05f;
        Vector3 pos = new(p0.x, y, p0.y);
        Vector3 dir = new(p1.x - p0.x, 0f, p1.y - p0.y);
        Quaternion rot = dir.sqrMagnitude > 0.001f ? Quaternion.LookRotation(dir, Vector3.up) : car.transform.rotation;

#if GLEY_TRAFFIC_SYSTEM
        Gley.TrafficSystem.API.ClearTrafficOnArea(pos, 30f);
        StartCoroutine(ClearStartAgain(pos));
#endif

        var yawRig = FindFirstObjectByType<YawMotion>();
        if (yawRig != null) yawRig.SuppressForSeconds(1.0f);   // s

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

    public bool GetNextTurn(out float distanceM, out float signedAngleDeg)
    {
        distanceM = 0f; signedAngleDeg = 0f;
        if (_pts == null || _pts.Length < 3 || loop) return false;
        int n = _pts.Length;
        float dist = 0f;
        int i = Mathf.Clamp(nearIdx, 0, n - 3);
        while (i < n - 2 && dist < 200f)
        {
            float acc = 0f, w = 0f; int j = i;
            while (j < n - 2 && w < 10f)
            {
                Vector2 v1 = _pts[j + 1] - _pts[j];
                Vector2 v2 = _pts[j + 2] - _pts[j + 1];
                acc += Vector2.SignedAngle(v1, v2);   // Unity: + = counter-clockwise
                w += v1.magnitude;
                j++;
            }
            if (Mathf.Abs(acc) > 30f)
            {
                distanceM = dist;
                signedAngleDeg = -acc;                // flip: + = RIGHT turn
                return true;
            }
            dist += (_pts[i + 1] - _pts[i]).magnitude;
            i++;
        }
        return false;
    }

    public float GetRemainingM()
    {
        if (_pts == null || _pts.Length < 2 || loop) return 0f;
        float d = 0f;
        for (int i = Mathf.Clamp(nearIdx, 0, _pts.Length - 2); i < _pts.Length - 1; i++)
            d += (_pts[i + 1] - _pts[i]).magnitude;
        return d;
    }

#if GLEY_TRAFFIC_SYSTEM
    System.Collections.IEnumerator ClearStartAgain(Vector3 pos)
    {
        yield return new WaitForSeconds(0.15f);
        Gley.TrafficSystem.API.ClearTrafficOnArea(pos, 22f);
        yield return new WaitForSeconds(0.55f);
        Gley.TrafficSystem.API.ClearTrafficOnArea(pos, 22f);
    }
#endif

    bool TrackRouteProgress(out int bestI)
    {
        bestI = nearIdx;
        if (_pts == null || _pts.Length < 2) return false;

        Vector2 carXZ = new(car.transform.position.x, car.transform.position.z);
        int n = _pts.Length;

        float bestD = float.MaxValue;
        for (int k = -2; k <= 10; k++)
        {
            int i = nearIdx + k;
            float d = (Pt(i) - carXZ).sqrMagnitude;
            if (d < bestD) { bestD = d; bestI = i; }
        }
        if (loop)
        {
            if (bestI / n > nearIdx / n) { laps++; onLapComplete?.Invoke(); }
            nearIdx = ((bestI % n) + n) % n;
            return false;
        }

        nearIdx = Mathf.Clamp(bestI, 0, n - 1);
        Vector2 endPt = Pt(n - 1);
        Vector3 fwdA = car.transform.forward;
        bool passedEnd = nearIdx >= n - 2 &&
                         (endPt.x - carXZ.x) * fwdA.x + (endPt.y - carXZ.y) * fwdA.z < 0f;
        if (!_arrived && nearIdx >= n - 2 && ((endPt - carXZ).magnitude < 8f || passedEnd))
        {
            _arrived = true;
            bool wasAutopilot = engaged;
            engaged = false;
            if (wasAutopilot)
            {
                _holdBrake = true;
                car.throttleInput = 0f;
                car.steerInput = 0f;
            }
            onRouteComplete?.Invoke();
            return true;
        }
        return false;
    }

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
            return;
        }

        if (!engaged)
        {
            if (_holdBrake && car != null)
            {
                car.throttleInput = car.currentSpeed > 0.5f ? -1f : 0f;
                if (car.currentSpeed <= 0.5f) _holdBrake = false;
            }
            TrackRouteProgress(out _);
            return;
        }

        if (TrackRouteProgress(out int bestI)) return;   // arrived this frame
        Vector2 carXZ = new(car.transform.position.x, car.transform.position.z);
        int n = _pts.Length;

        Vector2 target = Pt(bestI);
        float acc = (target - carXZ).magnitude;
        int j = bestI;
        while (acc < lookAhead)
        {
            Vector2 a = Pt(j), b = Pt(j + 1);
            acc += (b - a).magnitude; j++;
            target = b;
            if (!loop && j >= n - 1) break;
        }

        if (meetPull)
        {
            bool oncoming = false;
            Vector3 fwdM = car.transform.forward; fwdM.y = 0f; fwdM.Normalize();
            Vector3 rightM = new(fwdM.z, 0f, -fwdM.x);
            Vector3 boxCentre = car.transform.position + fwdM * (meetDetectM * 0.5f) + Vector3.up * 0.7f;
            int nHits = Physics.OverlapBoxNonAlloc(boxCentre,
                new Vector3(meetLateralM + 1.2f, 1.2f, meetDetectM * 0.5f),
                _meetHits, Quaternion.LookRotation(fwdM, Vector3.up), trafficMask);
            for (int h = 0; h < nHits; h++)
            {
                Transform tr = _meetHits[h].attachedRigidbody != null
                    ? _meetHits[h].attachedRigidbody.transform : _meetHits[h].transform;
                Vector3 delta = tr.position - car.transform.position;
                float ahead = Vector3.Dot(delta, fwdM);
                if (ahead < 1f || ahead > meetDetectM) continue;
                if (Mathf.Abs(Vector3.Dot(delta, rightM)) > meetLateralM) continue;
                if (Vector3.Dot(tr.forward, fwdM) >= -0.6f) continue;   // not oncoming
                oncoming = true;
                break;
            }
            if (oncoming) _meetLastSeen = Time.time;
            float want = Time.time - _meetLastSeen < 1f ? 1f : 0f;
            _meetBlend = Mathf.MoveTowards(_meetBlend, want, Time.deltaTime * 2f);
            if (_meetBlend > 0.001f)
            {
                float hwM = _halfwDense != null && bestI < _halfwDense.Length ? _halfwDense[bestI] : 2.8f;
                float driveOff = Mathf.Min(Mathf.Max(hwM * 0.5f, 1.1f), laneOffsetM);
                driveOff = Mathf.Min(driveOff, Mathf.Max(0.5f, hwM - 1.35f));
                float extra = Mathf.Min(meetOffsetM, (hwM - 1.0f) - driveOff);
                if (extra > 0f)
                {
                    Vector2 dTo = target - carXZ;
                    if (dTo.sqrMagnitude > 1e-3f)
                    {
                        dTo.Normalize();
                        target += new Vector2(dTo.y, -dTo.x) * (extra * _meetBlend);   // right of travel
                    }
                }
            }
        }

        // steer toward target (XZ)
        Vector3 fwd = car.transform.forward; fwd.y = 0f;
        Vector3 to = new(target.x - carXZ.x, 0f, target.y - carXZ.y);
        float ang = Vector3.SignedAngle(fwd.normalized, to.normalized, Vector3.up);
        float steer = Mathf.Clamp(ang / Mathf.Max(1f, fullSteerAngle), -1f, 1f);

        float obstacleAheadM = float.MaxValue;
        if (wallAvoid)
        {
            Vector3 wOrigin = car.transform.position + Vector3.up * 1.0f;
            Vector3 fwdN = fwd.normalized;
            float Whisker(float deg)
            {
                Vector3 d = Quaternion.Euler(0f, deg, 0f) * fwdN;
                return Physics.Raycast(wOrigin, d, out RaycastHit h, whiskerLen, wallMask) ? h.distance : whiskerLen;
            }
            float dl20 = Whisker(-20f), dr20 = Whisker(20f);
            float dl8 = Whisker(-8f), dr8 = Whisker(8f);
            float dl = Mathf.Min(dl20, dl8), dr = Mathf.Min(dr20, dr8);
            steer = Mathf.Clamp(steer + (dl - dr) / whiskerLen * 0.6f, -1f, 1f);
            obstacleAheadM = Mathf.Min(dl8, dr8);
        }

        float bendAhead = 0f, scan = 0f; int b0 = bestI;
        while (scan < cornerLookAhead)
        {
            Vector2 pa = Pt(b0), pb = Pt(b0 + 1), pc = Pt(b0 + 2);
            bendAhead += Vector2.Angle(pb - pa, pc - pb);
            scan += (pb - pa).magnitude; b0++;
            if (!loop && b0 >= n - 2) break;
        }
        float cornerCap = Mathf.Lerp(targetSpeedKmh, cornerSpeedKmh, Mathf.Clamp01(bendAhead / Mathf.Max(1f, cornerBendDeg)));

        float tgt = targetSpeedKmh * Mathf.Clamp01(1f - Mathf.Abs(ang) / 90f * turnSlowdown);
        tgt = Mathf.Min(tgt, cornerCap);

        if (_meetBlend > 0.001f)
            tgt = Mathf.Min(tgt, Mathf.Lerp(targetSpeedKmh, meetSpeedKmh, _meetBlend));

        if (obstacleAheadM < 4.5f)
            tgt = Mathf.Min(tgt, Mathf.Lerp(0f, cornerSpeedKmh,
                Mathf.Clamp01((obstacleAheadM - 1.5f) / 3f)));

        if (trafficBrake)
        {
            Vector3 origin = car.transform.position + Vector3.up * 0.6f;
            if (Physics.SphereCast(origin, 1.0f, fwd.normalized, out RaycastHit hit, trafficDetectM, trafficMask))
            {
                Transform hitTr = hit.collider.attachedRigidbody != null
                    ? hit.collider.attachedRigidbody.transform : hit.collider.transform;
                if (hit.distance <= trafficStopM)
                    tgt = 0f;
                else if (Vector3.Dot(hitTr.forward, fwd.normalized) > 0.3f)
                    tgt = Mathf.Min(tgt, Mathf.Lerp(0f, targetSpeedKmh,
                        Mathf.Clamp01((hit.distance - trafficStopM) / Mathf.Max(1f, trafficDetectM - trafficStopM))));
            }
        }

        float sp = car.currentSpeed;
        float throttle = sp < tgt - 1f ? 1f : (sp > tgt + 3f ? -1f : 0f);

        if (throttle > 0.5f && sp < 1f)
        {
            _wedgeTime += Time.deltaTime;
            stuckSeconds = _wedgeTime;
            if (_wedgeTime > wedgeSeconds)
            {
                stuckTrafficAhead = Physics.CheckBox(
                    car.transform.position + car.transform.forward * 3.2f + Vector3.up * 0.7f,
                    new Vector3(1.8f, 0.8f, 2.6f), car.transform.rotation, trafficMask);
                if (Mathf.Repeat(_wedgeTime, 5f) < Time.deltaTime)
                    Debug.LogWarning($"[AutoDriver] stationary {_wedgeTime:F0}s at {car.transform.position:F1} — " +
                                     (stuckTrafficAhead ? "traffic ahead, waiting." : "no traffic detected (geometry wedge?), holding."));
            }
        }
        else
        {
            _wedgeTime = 0f;
            stuckSeconds = 0f;
            stuckTrafficAhead = false;
        }

        car.steerInput = steer;
        car.throttleInput = throttle;
    }

    void OnDrawGizmos()
    {
        var pts = _pts ?? route;
        Gizmos.color = Color.cyan;
        float y = car != null ? car.transform.position.y : 58f;
        int last = loop ? pts.Length : pts.Length - 1;
        for (int i = 0; i < last; i++)
        {
            Vector3 a = new(pts[i].x, y, pts[i].y);
            Vector3 b = new(pts[(i + 1) % pts.Length].x, y, pts[(i + 1) % pts.Length].y);
            Gizmos.DrawLine(a, b);
        }
    }
}
