using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using JYW.Game.ObjectMaker;
using JYW.Game.ObjectMaker.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace JYW.Game.ObjectMaker.Tests
{
    /// <summary>Graphics-enabled EditMode integration tests. Recovery files are backed up before opening our own window.</summary>
    public sealed class MapEditorWorkflowTests
    {
        const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        readonly List<EditorWindow> windows = new List<EditorWindow>();
        readonly Dictionary<string, string> backups = new Dictionary<string, string>();
        string backupFolder;
        static Type Host => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("AllinOneEditor")).First(t => t != null);
        static string Recovery => (string)Host.GetProperty("MapRecoveryPath", BindingFlags.Public | BindingFlags.Static).GetValue(null);
        static object Call(object target, string name, params object[] args) => Host.GetMethod(name, Instance).Invoke(target, args);
        static object Field(EditorWindow window, string name) => Host.GetField(name, Instance).GetValue(window);
        static void Field(EditorWindow window, string name, object value) => Host.GetField(name, Instance).SetValue(window, value);
        static MapPaintDocument Document(EditorWindow window) => (MapPaintDocument)Host.GetProperty("MapDocument").GetValue(window);
        static void Area(EditorWindow window, string name) => Call(window, "SwitchArea", Enum.Parse(Host.GetNestedType("WorkspaceArea"), name));
        static IMGUIContainer Canvas(EditorWindow window) => window.rootVisualElement.Q<IMGUIContainer>("map-canvas");

        [SetUp] public void SetUp()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("Map workflow input tests require a graphics-enabled editor.");
            if (Resources.FindObjectsOfTypeAll<EditorWindow>().Any(w => w.GetType() == Host))
                Assert.Ignore("Close AllinOneEditor before running isolated workflow tests; existing user windows are never closed by this fixture.");
            backupFolder = Path.Combine(Path.GetTempPath(), "MapWorkflowBackup_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(backupFolder);
            foreach (string root in new[] { Recovery, Path.GetFullPath(ObjectWorkspaceRecovery.FilePath) })
                foreach (string suffix in new[] { "", ".bak", ".tmp" })
                {
                    string path = root + suffix;
                    string saved = File.Exists(path) ? Path.Combine(backupFolder, backups.Count + ".backup") : null;
                    if (saved != null) File.Copy(path, saved);
                    backups.Add(path, saved);
                    if (saved != null) File.Delete(path);
                }
        }
        [TearDown] public void TearDown()
        {
            try
            {
                foreach (var window in windows) if (window) window.Close();
                windows.Clear();
            }
            finally
            {
                foreach (var pair in backups)
                {
                    if (pair.Value != null) { Directory.CreateDirectory(Path.GetDirectoryName(pair.Key)); File.Copy(pair.Value, pair.Key, true); }
                    else if (File.Exists(pair.Key)) File.Delete(pair.Key);
                }
                backups.Clear();
                if (backupFolder != null && Directory.Exists(backupFolder))
                {
                    foreach (string file in Directory.GetFiles(backupFolder)) File.Delete(file);
                    Directory.Delete(backupFolder);
                }
                backupFolder = null;
            }
        }
        EditorWindow Open(bool fresh = true)
        {
            var window = (EditorWindow)ScriptableObject.CreateInstance(Host); windows.Add(window);
            window.position = new Rect(80, 80, 1200, 850); window.Show(); Call(window, "CreateGUI"); Area(window, "Map");
            if (fresh)
            {
                var doc = Document(window); doc.Resize(64, 48); doc.Clear(); doc.brushDiameter = 4; doc.pixelsPerUnit = 10;
                Call(window, "RebuildMapSettings"); Call(window, "MapChanged");
            }
            Field(window, "mapFitPending", false); Field(window, "mapZoom", 4f); Field(window, "mapPan", Vector2.zero);
            window.Focus(); Canvas(window).Focus(); window.Repaint(); return window;
        }
        static IEnumerator Layout(EditorWindow window)
        {
            for (int i = 0; i < 8; i++) { window.Repaint(); yield return null; }
            var rect = Canvas(window).contentRect;
            Assert.Greater(rect.width, 300, "Map canvas must have a usable width.");
            Assert.Greater(rect.height, 200, "Map canvas must have a usable height.");
        }
        static Vector2 Position(EditorWindow window, float x, float y)
        {
            var canvas = Canvas(window); var viewport = new Rect(0, 0, canvas.contentRect.width, canvas.contentRect.height);
            Rect image = (Rect)Call(window, "MapImageRect", viewport); float zoom = (float)Field(window, "mapZoom");
            return canvas.LocalToWorld(new Vector2(image.x + x * zoom, image.yMax - y * zoom));
        }
        static void Mouse(EditorWindow window, EventType type, Vector2 point)
        {
            window.SendEvent(new Event { type = type, mousePosition = point, button = 0, clickCount = 1 });
        }
        static string lastKeyDiagnostic;
        static void Key(EditorWindow window, KeyCode key)
        {
            Canvas(window).Focus(); string eventTarget = "none";
            EventCallback<KeyDownEvent> observe = evt => eventTarget = evt.target?.GetType().Name + "/" + (evt.target as VisualElement)?.name;
            window.rootVisualElement.RegisterCallback(observe, TrickleDown.TrickleDown);
            try
            {
                char character = key >= KeyCode.A && key <= KeyCode.Z ? (char)(int)key : '\0';
                window.SendEvent(new Event { type = EventType.KeyDown, keyCode = key, character = character });
                window.SendEvent(new Event { type = EventType.KeyUp, keyCode = key, character = character });
                var focused = window.rootVisualElement.focusController.focusedElement;
                lastKeyDiagnostic = "Focused=" + focused?.GetType().Name + "/" + (focused as VisualElement)?.name + "; KeyDown target=" + eventTarget;
            }
            finally { window.rootVisualElement.UnregisterCallback(observe, TrickleDown.TrickleDown); }
        }
        static void Stroke(EditorWindow window, Vector2 from, Vector2 to, bool drag = true)
        {
            Mouse(window, EventType.MouseDown, Position(window, from.x, from.y));
            if (drag) Mouse(window, EventType.MouseDrag, Position(window, to.x, to.y));
            Mouse(window, EventType.MouseUp, Position(window, to.x, to.y));
        }
        static void Click(EditorWindow window, VisualElement element)
        {
            Mouse(window, EventType.MouseDown, element.worldBound.center); Mouse(window, EventType.MouseUp, element.worldBound.center);
        }
        static byte Pixel(EditorWindow window, int x, int y) { var doc = Document(window); return doc.mask[y * doc.width + x]; }

        [UnityTest] public IEnumerator RealCanvasInputPaintsContinuousStrokeAndErasesIncludingMouseUpEndpoint()
        {
            var window = Open(); yield return Layout(window);
            Stroke(window, new Vector2(5, 20), new Vector2(55, 20));
            for (int x = 5; x < 55; x++) Assert.AreEqual(255, Pixel(window, x, 20), "Brush x=" + x);
            Key(window, KeyCode.E); Assert.AreEqual("Eraser", Field(window, "mapTool").ToString(), "E must select the eraser. " + lastKeyDiagnostic);
            yield return Layout(window); Stroke(window, new Vector2(20, 20), new Vector2(35, 20), false);
            for (int x = 20; x < 35; x++) Assert.AreEqual(0, Pixel(window, x, 20), "Eraser x=" + x);
            Assert.AreEqual(255, Pixel(window, 10, 20)); Assert.IsFalse((bool)Field(window, "mapStroke"));
            Key(window, KeyCode.B); Assert.AreEqual("Brush", Field(window, "mapTool").ToString());
            yield return Layout(window); Stroke(window, new Vector2(10, 30), new Vector2(45, 30), false);
            Assert.AreEqual(255, Pixel(window, 43, 30), "MouseUp must paint the final segment even when a drag event was coalesced.");
        }
        [UnityTest] public IEnumerator CanvasSizeFieldsApplyThroughButtonAndInvalidFloatCannotPoisonDraft()
        {
            var window = Open(); yield return Layout(window); Stroke(window, new Vector2(8, 8), new Vector2(8, 8));
            var width = window.rootVisualElement.Q<IntegerField>("map-width"); var height = window.rootVisualElement.Q<IntegerField>("map-height");
            width.value = 80; height.value = 60;
            var apply = width.parent.Q<Button>(); Assert.NotNull(apply); Click(window, apply); yield return Layout(window);
            Assert.AreEqual(80, Document(window).width); Assert.AreEqual(60, Document(window).height); Assert.AreEqual(255, Pixel(window, 8, 8));
            float scale = Document(window).tileScale;
            window.rootVisualElement.Q<FloatField>("map-tile-scale").value = float.NaN;
            Assert.AreEqual(scale, Document(window).tileScale); Assert.DoesNotThrow(() => Document(window).EnsureValid());
            Assert.IsTrue(window.rootVisualElement.Q<Label>(className: "map-notice").ClassListContains("is-error"));
            width = window.rootVisualElement.Q<IntegerField>("map-width"); width.value = 5000;
            Click(window, width.parent.Q<Button>()); yield return null;
            Assert.AreEqual(80, Document(window).width); Assert.AreEqual(255, Pixel(window, 8, 8));
        }
        [UnityTest] public IEnumerator EscapeAndToolSwitchReleaseInterruptedStroke()
        {
            var window = Open(); yield return Layout(window);
            Mouse(window, EventType.MouseDown, Position(window, 8, 8)); Assert.IsTrue((bool)Field(window, "mapStroke"));
            Key(window, KeyCode.Escape); Assert.IsFalse((bool)Field(window, "mapStroke")); Assert.AreEqual(0, (int)Field(window, "mapControl"));
            Mouse(window, EventType.MouseDrag, Position(window, 45, 8)); Mouse(window, EventType.MouseUp, Position(window, 45, 8));
            Assert.AreEqual(0, Pixel(window, 40, 8));
            Mouse(window, EventType.MouseDown, Position(window, 8, 25));
            Key(window, KeyCode.R); Assert.IsFalse((bool)Field(window, "mapStroke"));
            Mouse(window, EventType.MouseUp, Position(window, 40, 35)); Assert.AreEqual(0, Pixel(window, 30, 30));
        }
        [UnityTest] public IEnumerator SwitchingTabMidStrokeAndClosingWindowPreservesRecovery()
        {
            var window = Open(); yield return Layout(window);
            window.rootVisualElement.Q<TextField>("map-name").value = "Workflow recovery";
            Mouse(window, EventType.MouseDown, Position(window, 8, 12)); Mouse(window, EventType.MouseDrag, Position(window, 40, 12));
            Area(window, "Event"); Assert.IsFalse((bool)Field(window, "mapStroke")); Assert.IsTrue(File.Exists(Recovery));
            Area(window, "Map"); yield return Layout(window); Assert.AreEqual(255, Pixel(window, 30, 12));
            window.Close(); yield return null;
            var reopened = Open(false); yield return Layout(reopened);
            Assert.AreEqual("Workflow recovery", Document(reopened).mapName); Assert.AreEqual(64, Document(reopened).width);
            Assert.AreEqual(255, Pixel(reopened, 30, 12)); Assert.IsFalse((bool)Field(reopened, "mapStroke"));
        }
        [UnityTest] public IEnumerator CanvasUndoRedoDoesNotModifyMonsterDraft()
        {
            var window = Open(); Call(window, "NewObject", ObjectKind.Monster);
            var monster = (ObjectWorkspaceDocument)Host.GetProperty("ActiveObject", Instance).GetValue(window);
            monster.Data.core.maxHealth = 173; string before = ObjectMakerJson.Export(monster.Data);
            Area(window, "Map"); yield return Layout(window);
            Undo.IncrementCurrentGroup(); Stroke(window, new Vector2(5, 20), new Vector2(45, 20)); Undo.FlushUndoRecordObjects();
            Assert.AreEqual(255, Pixel(window, 30, 20)); Undo.PerformUndo(); yield return null;
            Assert.AreEqual(0, Pixel(window, 30, 20)); Assert.AreEqual(before, ObjectMakerJson.Export(monster.Data));
            Undo.PerformRedo(); yield return null;
            Assert.AreEqual(255, Pixel(window, 30, 20)); Assert.AreEqual(before, ObjectMakerJson.Export(monster.Data));
        }
        [UnityTest] public IEnumerator CorruptRecoveryIsPreservedInsteadOfSilentlyOverwritten()
        {
            byte[] corrupted = { 7, 1, 9, 3, 2 }; Directory.CreateDirectory(Path.GetDirectoryName(Recovery)); File.WriteAllBytes(Recovery, corrupted);
            var window = Open(); yield return Layout(window);
            Assert.IsTrue((bool)Field(window, "mapRecoveryBlocked")); Stroke(window, new Vector2(8, 8), new Vector2(20, 8));
            Call(window, "SaveMapRecovery"); CollectionAssert.AreEqual(corrupted, File.ReadAllBytes(Recovery));
            window.Close(); CollectionAssert.AreEqual(corrupted, File.ReadAllBytes(Recovery));
        }
    }
}


