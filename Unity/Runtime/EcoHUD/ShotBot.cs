using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

public class ShotBot : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot() { new GameObject("ShotBot").AddComponent<ShotBot>(); }

    IEnumerator Start()
    {
        yield return new WaitForSeconds(8f);
        var car = FindFirstObjectByType<CarController>();
        if (car == null) yield break;
        Transform T = car.transform;
        Transform driver = Find(T, "DriverModel");
        Transform wheel  = Find(T, "RMCar05_SteeringWheel");
        var consoleGO = GameObject.Find("Console Plate");
        var clusterGO = GameObject.Find("ClusterPlate");
        Camera main = Camera.main;
        Vector3 eye = main != null ? main.transform.position : T.position + Vector3.up * 1.2f;

        string dir = Path.GetFullPath(Application.dataPath + "/../RouteShots");
        Directory.CreateDirectory(dir);

        if (consoleGO != null)
            Shot(eye + T.up * 0.05f, consoleGO.transform.position, 55f, Path.Combine(dir, "shot_console.png"));
        Vector3 clusterAim = clusterGO != null ? clusterGO.transform.position
                          : (wheel != null ? wheel.position : T.position);
        Shot(eye, clusterAim, 50f, Path.Combine(dir, "shot_cluster.png"));
        if (driver != null)
        {
            Vector3 chest = driver.position + Vector3.up * 1.0f;
            Shot(T.TransformPoint(new Vector3(0.55f, 1.45f, 1.6f)), chest, 60f,
                 Path.Combine(dir, "shot_driver_front.png"));
            Shot(T.TransformPoint(new Vector3(0.85f, 1.25f, 0.15f)), chest + Vector3.up * 0.15f, 65f,
                 Path.Combine(dir, "shot_driver_side.png"));
        }
        Debug.Log("[ShotBot] DONE -> " + dir);
    }

    static void Shot(Vector3 pos, Vector3 lookAt, float fov, string path)
    {
        var go = new GameObject("ShotCam");
        var cam = go.AddComponent<Camera>();
        cam.transform.position = pos;
        cam.transform.rotation = Quaternion.LookRotation(lookAt - pos, Vector3.up);
        cam.fieldOfView = fov;
        cam.nearClipPlane = 0.03f;
        cam.enabled = false;
        var rt = new RenderTexture(1600, 1000, 24);
        var req = new RenderPipeline.StandardRequest();
        if (RenderPipeline.SupportsRenderRequest(cam, req))
        {
            req.destination = rt;
            RenderPipeline.SubmitRenderRequest(cam, req);
        }
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        RenderTexture.active = rt;
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = null;
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Destroy(go); Destroy(rt); Destroy(tex);
    }

    static Transform Find(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }

    public static void SnapHud(string path)
    {
        var go = new GameObject("HudSnap");
        var s = go.AddComponent<HudSnap>();
        s.path = path;
    }

    class HudSnap : MonoBehaviour
    {
        public string path;
        IEnumerator Start()
        {
            yield return new WaitForSeconds(0.6f);
            var car = FindFirstObjectByType<CarController>();
            if (car != null)
            {
                Transform T = car.transform;
                Camera main = Camera.main;
                Vector3 eye = main != null ? main.transform.position
                                           : T.TransformPoint(new Vector3(-0.37f, 1.25f, 0.2f));
                Shot(eye, T.TransformPoint(new Vector3(0f, 1.15f, 6f)), 65f, path);
                Debug.Log("[ShotBot] hud snap -> " + path);
            }
            Destroy(gameObject);
        }
    }
}
