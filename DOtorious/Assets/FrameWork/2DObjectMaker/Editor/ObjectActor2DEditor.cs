using UnityEditor;
using UnityEngine;

namespace JYW.Game.ObjectMaker.Editor
{
    [CustomEditor(typeof(ObjectActor2D))]
    public sealed class ObjectActor2DEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            if (GUILayout.Button("Open AllinOneEditor", GUILayout.Height(27)))
                ObjectMakerWindow.Open(target);
            DrawDefaultInspector();
        }
    }
}
