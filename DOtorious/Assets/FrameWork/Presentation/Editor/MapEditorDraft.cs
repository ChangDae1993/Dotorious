using JYW.Game.ObjectMaker.Editor;
using UnityEngine;

// Kept separate from the window so Undo never changes the active Event/Monster/Player document.
public sealed class MapEditorDraft : ScriptableObject
{
    public MapPaintDocument Data = new MapPaintDocument();
    public Object TerrainSource;
}
