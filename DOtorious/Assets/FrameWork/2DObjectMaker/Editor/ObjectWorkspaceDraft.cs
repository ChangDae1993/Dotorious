using UnityEngine;

namespace JYW.Game.ObjectMaker.Editor
{
    /// <summary>Only editable content participates in Undo; source identity and saved baselines do not.</summary>
    public sealed class ObjectWorkspaceDraft : ScriptableObject
    {
        public ObjectDefinitionData Data;
    }
}
