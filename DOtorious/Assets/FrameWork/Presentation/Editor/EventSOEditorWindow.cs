using JYW.Game.EventPlay;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

/// <summary>Compatibility entry point. All authoring opens the shared workspace.</summary>
public sealed class EventSOEditorWindow : EditorWindow
{
    [MenuItem("Window/FrameWork/Presentation", false, 2300)]
    public static void OpenWindow() => AllinOneEditor.OpenDocument(Selection.activeObject);
    public static void Open(EventSO asset) => AllinOneEditor.OpenDocument(asset);
    private void OnEnable() { EditorApplication.delayCall += Redirect; }
    private void Redirect() { if (this == null) return; OpenWindow(); Close(); }
    [OnOpenAsset]
#if UNITY_6000_5_OR_NEWER
    private static bool OpenAsset(EntityId id, int line)
    { var asset = EditorUtility.EntityIdToObject(id) as EventSO;
#else
    private static bool OpenAsset(int id, int line)
    { var asset = EditorUtility.InstanceIDToObject(id) as EventSO;
#endif
      if (asset == null) return false; Open(asset); return true; }
}
