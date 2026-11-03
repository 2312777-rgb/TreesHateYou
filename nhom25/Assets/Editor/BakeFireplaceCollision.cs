#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class BakeFireplaceCollision
{
    static BakeFireplaceCollision() { EditorApplication.update += Apply; }
    private static void Apply()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (SceneManager.GetActiveScene().name != "Environment_Free") return;
        GameObject hearth = GameObject.Find("Lumberjack_Fireplace");
        if (hearth == null) return;
        EditorApplication.update -= Apply;
        int added = FireplaceCollisionSetup.Ensure(hearth);
        if (added > 0)
        {
            EditorSceneManager.MarkSceneDirty(hearth.scene);
            EditorSceneManager.SaveScene(hearth.scene);
            Debug.Log("Fireplace solid mesh colliders added: " + added);
        }
    }
}
#endif
