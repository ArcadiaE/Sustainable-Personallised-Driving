using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class OpenStudyScene
{
    const string ScenePath = "Assets/Scenes/Pimlico.unity";

    static OpenStudyScene()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (SessionState.GetBool("OpenStudyScene.done", false)) return;
            SessionState.SetBool("OpenStudyScene.done", true);

            if (GameObject.Find("TrafficManager") != null) return;   // already there
            if (!System.IO.File.Exists(ScenePath)) return;

            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(ScenePath);
            Debug.Log("[OpenStudyScene] Opened " + ScenePath + " (study scene was not loaded).");
        };
    }
}
