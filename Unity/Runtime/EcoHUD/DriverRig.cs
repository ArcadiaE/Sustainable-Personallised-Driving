using UnityEngine;

public class DriverRig : MonoBehaviour
{
    [Header("Wired by DriverSetup")]
    public Transform character;
    public Transform wheel;

    [Header("Pose tuning")]
    [Range(0f, 40f)] public float spineRecline = 20f;
    [Range(0f, 30f)] public float pelvisRollBack = 4f;
    [Range(0f, 20f)] public float thighSplay = 7f;
    [Tooltip("Slide the whole body backwards so the back actually RESTS on the seatback; the head is shrunk anyway, so the old head-at-eye placement no longer binds the body position.")]
    public float sitBackM = 0.09f;   // m
    public bool shrinkHead = true;
    public bool shrinkArms = true;

    Transform _pelvis, _spine, _head;
    readonly System.Collections.Generic.List<Transform> _spineChain = new System.Collections.Generic.List<Transform>();
    Transform _thighL, _thighR, _calfL, _calfR;
    Transform _upArmL, _upArmR;

    void Start()
    {
        if (character == null)
        {
            Debug.LogWarning("[DriverRig] character not wired — idle.");
            return;
        }
        foreach (var an in character.GetComponentsInChildren<Animator>(true))
            an.enabled = false;

        character.position -= character.forward * sitBackM;

        FindBones();
        if (_pelvis == null)
        {
            Debug.LogWarning("[DriverRig] pelvis not found — pose skipped.");
            return;
        }

        SitPose();

        if (shrinkHead && _head != null)
            _head.localScale = Vector3.one * 0.001f;
        if (shrinkArms)
        {
            if (_upArmL != null) _upArmL.localScale = Vector3.one * 0.001f;
            if (_upArmR != null) _upArmR.localScale = Vector3.one * 0.001f;
            Debug.Log($"[DriverRig] arms removed (upperArm bones zero-scaled): L={_upArmL != null} R={_upArmR != null}");
        }
    }

    void SitPose()
    {
        Transform root = character;
        Vector3 fwd = root.forward, up = root.up, right = root.right;

        if (_pelvis != null)
            _pelvis.rotation = Quaternion.AngleAxis(-pelvisRollBack, right) * _pelvis.rotation;

        Vector3 thighDirL = (Quaternion.AngleAxis(-thighSplay, up) * (fwd + up * 0.08f)).normalized;
        Vector3 thighDirR = (Quaternion.AngleAxis(+thighSplay, up) * (fwd + up * 0.08f)).normalized;
        Aim(_thighL, _calfL, thighDirL);
        Aim(_thighR, _calfR, thighDirR);

        if (_calfL != null) Aim(_calfL, ChildOf(_calfL), (-up + fwd * 0.35f).normalized);
        if (_calfR != null) Aim(_calfR, ChildOf(_calfR), (-up + fwd * 0.35f).normalized);

        Transform chestRef = _head != null ? _head
                           : (_spineChain.Count > 0 ? _spineChain[_spineChain.Count - 1] : _spine);
        if (chestRef != null && _spineChain.Count > 0)
        {
            Vector3 torso = chestRef.position - _pelvis.position;
            float cur = Vector3.SignedAngle(up, Vector3.ProjectOnPlane(torso, right), right);
            float corr = (-spineRecline) - cur;
            float per = corr / _spineChain.Count;
            foreach (var sp in _spineChain)
                sp.rotation = Quaternion.AngleAxis(per, right) * sp.rotation;
            Debug.Log($"[DriverRig] torso closed-loop: measured {cur:F1} deg -> target {-spineRecline:F1} deg (corr {corr:F1})");
        }
    }

    // ---------------- rig discovery ----------------

    void FindBones()
    {
        var all = character.GetComponentsInChildren<Transform>(true);
        foreach (var t in all)
        {
            string n = t.name.ToLowerInvariant();
            if (_pelvis == null && (n.Contains("pelvis") || n.Contains("hips"))) _pelvis = t;
            if (n.Contains("spine")) { if (_spine == null) _spine = t; _spineChain.Add(t); }
            if (n.Contains("head") && !n.Contains("headg") && _head == null) _head = t;
        }
        if (_pelvis == null) return;
        foreach (var t in all)
        {
            string n = t.name.ToLowerInvariant();
            int side = SideOf(n);
            bool left = side != 0 ? side < 0
                : character.InverseTransformPoint(t.position).x < 0f;
            if (n.Contains("thigh") || n.Contains("upleg")) Assign(ref _thighL, ref _thighR, t, left);
            else if (n.Contains("calf") || n.Contains("lowerleg")
                     || (n.EndsWith("leg") && !n.Contains("upleg"))) Assign(ref _calfL, ref _calfR, t, left);
            else if (n.Contains("upperarm") || n.Contains("up_arm")
                     || (n.EndsWith("arm") && !n.Contains("forearm") && !n.Contains("lowerarm")))
                Assign(ref _upArmL, ref _upArmR, t, left);
        }
    }

    static int SideOf(string lower)
    {
        if (lower.Contains("left"))  return -1;
        if (lower.Contains("right")) return +1;
        if (System.Text.RegularExpressions.Regex.IsMatch(lower, @"(^|[ _\.])l([ _\.]|$)")) return -1;
        if (System.Text.RegularExpressions.Regex.IsMatch(lower, @"(^|[ _\.])r([ _\.]|$)")) return +1;
        return 0;
    }

    static void Assign(ref Transform l, ref Transform r, Transform t, bool isLeft)
    {
        if (isLeft) { if (l == null) l = t; }
        else        { if (r == null) r = t; }
    }

    static Transform ChildOf(Transform t) => t != null && t.childCount > 0 ? t.GetChild(0) : null;

    static void Aim(Transform bone, Transform child, Vector3 dir)
    {
        if (bone == null) return;
        Vector3 cur = child != null ? (child.position - bone.position).normalized : bone.forward;
        if (cur.sqrMagnitude < 1e-6f) return;
        bone.rotation = Quaternion.FromToRotation(cur, dir) * bone.rotation;
    }
}
