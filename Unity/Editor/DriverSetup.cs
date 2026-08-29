using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class DriverSetup
{
    const string PrefabPath = "Assets/StudyAssets/Driver/Remy.fbx";
    const string DriverName = "DriverModel";
    const string WheelName  = "RMCar05_SteeringWheel";

    static readonly Vector3 SeatOffset = new Vector3(0f, -0.62f, -0.38f);

    static DriverSetup()
    {
        EditorApplication.delayCall += () => Run(true);
    }

    [MenuItem("Tools/Sustainable Driving/Driver Setup")]
    static void RunMenu() => Run(false);

    static void Run(bool silent)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;

        var car = Object.FindFirstObjectByType<CarController>();
        if (car == null) { if (!silent) Debug.LogWarning("[DriverSetup] no CarController in scene."); return; }

        var wheel = FindDeep(car.transform, WheelName);
        if (wheel == null) { if (!silent) Debug.LogWarning($"[DriverSetup] '{WheelName}' not found under the car."); return; }

        EnsureTexturesExtracted();

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null) { Debug.LogWarning($"[DriverSetup] prefab missing: {PrefabPath}"); return; }

        var existing = FindDeep(car.transform, DriverName);
        GameObject go;
        if (existing != null
            && PrefabUtility.GetCorrespondingObjectFromSource(existing.gameObject) == prefab)
        {
            go = existing.gameObject;
        }
        else
        {
            if (existing != null) Object.DestroyImmediate(existing.gameObject);   // model swapped
            go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, car.transform);
            go.name = DriverName;
        }

        go.transform.localScale = Vector3.one;
        var r0 = go.GetComponentInChildren<Renderer>();
        if (r0 != null)
        {
            float h = r0.bounds.size.y;
            if (h > 0.1f && (h < 1.4f || h > 2.1f))
                go.transform.localScale = Vector3.one * (1.75f / h);
        }

        Camera cam = null; float best = float.MaxValue;
        foreach (var c in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
        {
            float d = Vector3.Distance(c.transform.position, wheel.position);
            if (d < 3f && d < best) { best = d; cam = c; }
        }

        Vector3 eye = cam != null ? cam.transform.position
                                  : wheel.position - car.transform.forward * 0.5f + Vector3.up * 0.55f;
        Vector3 fwd = Vector3.ProjectOnPlane(wheel.position - eye, Vector3.up);
        if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.ProjectOnPlane(car.transform.forward, Vector3.up);
        go.transform.rotation = Quaternion.LookRotation(fwd.normalized, Vector3.up);

        go.transform.position = wheel.position + wheel.TransformVector(SeatOffset);
        var head = FindBoneByKeyword(go.transform, "head");
        if (head != null)
        {
            Vector3 headTarget = eye - go.transform.forward * 0.08f - Vector3.up * 0.10f;
            go.transform.position += headTarget - head.position;
        }

        var rig = go.GetComponent<DriverRig>();
        if (rig == null) rig = go.AddComponent<DriverRig>();
        rig.character = go.transform;
        rig.wheel = wheel;

        EditorSceneManager.MarkSceneDirty(go.scene);
        EditorSceneManager.SaveScene(go.scene);
        var rend = go.GetComponentInChildren<Renderer>();
        Debug.Log($"[DriverSetup] driver model seated, DriverRig added, scene SAVED. pos={go.transform.position:F2} " +
                  $"scale={go.transform.lossyScale:F3} head={(head != null ? head.position.ToString("F2") : "null")} " +
                  $"bounds={(rend != null ? rend.bounds.center.ToString("F2") + " size " + rend.bounds.size.ToString("F2") : "no renderer")} " +
                  $"wheel={wheel.position:F2} eye={eye:F2}");
    }

    static void EnsureTexturesExtracted()
    {
        var imp = AssetImporter.GetAtPath(PrefabPath) as ModelImporter;
        if (imp == null) return;
        const string texDir = "Assets/StudyAssets/Driver/Textures";
        if (AssetDatabase.IsValidFolder(texDir)) return;   // done before
        AssetDatabase.CreateFolder("Assets/StudyAssets/Driver", "Textures");
        imp.ExtractTextures(texDir);
        AssetDatabase.ImportAsset(PrefabPath, ImportAssetOptions.ForceUpdate);
        AssetDatabase.Refresh();
        Debug.Log("[DriverSetup] embedded textures extracted to " + texDir);
    }

    static Transform FindDeep(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }

    static Transform FindBoneByKeyword(Transform root, string keyword)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name.ToLowerInvariant().Contains(keyword)) return t;
        return null;
    }
}
