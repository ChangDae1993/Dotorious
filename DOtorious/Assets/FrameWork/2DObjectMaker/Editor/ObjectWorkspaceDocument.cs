using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace JYW.Game.ObjectMaker.Editor
{
    /// <summary>Undo belongs to a draft document, never to the selected window or source asset.</summary>
    public sealed class ObjectWorkspaceDocument : ScriptableObject
    {
        public string Key;
        public string SourceGuid;
        public ObjectDefinitionSO Source;
        public ObjectWorkspaceDraft Content;
        public ObjectDefinitionData Data { get => Content.Data; set => Content.Data = value; }
        public string Baseline;
        public string SourceBaseline;
        public string Section = "identity";
        public int Index;
        public string Notice;
        public string UnresolvedJson;
        public bool HasUnresolvedReferences => !string.IsNullOrEmpty(UnresolvedJson);

        public string Title => string.IsNullOrWhiteSpace(Data?.displayName) ? "이름 없는 오브젝트" : Data.displayName;
        public bool IsDirty => string.IsNullOrEmpty(SourceGuid) || ObjectMakerJson.Export(Data) != Baseline;
        public bool SourceMissing => !string.IsNullOrEmpty(SourceGuid) && Source == null;
        public bool HasConflict => Source != null && ObjectMakerJson.Export(Source.Data) != SourceBaseline;

        public static ObjectWorkspaceDocument Create(ObjectDefinitionSO source, ObjectKind kind = ObjectKind.Monster)
        {
            var doc = CreateInstance<ObjectWorkspaceDocument>();
            doc.hideFlags = HideFlags.HideAndDontSave;
            doc.Content = CreateInstance<ObjectWorkspaceDraft>();
            doc.Content.hideFlags = HideFlags.HideAndDontSave;
            doc.Source = source;
            doc.SourceGuid = source == null ? "" : AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source));
            doc.Key = source == null ? "new:" + Guid.NewGuid().ToString("N") : doc.SourceGuid;
            doc.Data = source == null ? ObjectDefinitionData.CreateSG001() : source.Data.Clone();
            if (source == null) { doc.Data.kind = kind; doc.Data.displayName = kind == ObjectKind.Player ? "New Player" : "New Monster"; }
            doc.Data.Sanitize();
            ObjectBehaviorCatalog.ImportLegacyTuning(doc.Data);
            doc.Baseline = ObjectMakerJson.Export(doc.Data);
            doc.SourceBaseline = source == null ? "" : ObjectMakerJson.Export(source.Data);
            return doc;
        }

        private void OnDestroy()
        {
            if (Content == null) return;
            Undo.ClearUndo(Content);
            DestroyImmediate(Content);
        }

        public ObjectMakerBuildResult Apply()
        {
            if (HasUnresolvedReferences) throw new InvalidOperationException("복구하지 못한 에셋 참조가 있습니다. 에셋 복구 후 창을 다시 여세요. 원래 초안 JSON은 보존되어 있습니다.");
            if (SourceMissing) throw new InvalidOperationException("원본 Definition을 찾을 수 없습니다. 초안을 JSON으로 내보내거나 원본 에셋을 복구하세요.");
            if (HasConflict) throw new InvalidOperationException("초안을 연 뒤 원본 Definition이 변경되었습니다. 초안을 JSON으로 보관하고 ‘현재 초안 되돌리기’로 원본을 다시 읽으세요.");
            string blocker = ObjectPrefabCatalog.ApplyBlocker(Source);
            if (!string.IsNullOrEmpty(blocker)) throw new InvalidOperationException(blocker);
            ObjectMakerBuildResult result = ObjectMakerPrefabBuilder.Make(Data, Source);
            Source = result.Definition;
            SourceGuid = AssetDatabase.AssetPathToGUID(result.DefinitionPath);
            // Keep Key stable: open tabs and Undo history identify this draft, not the generated asset.
            Data = Source.Data.Clone();
            Data.Sanitize();
            Baseline = ObjectMakerJson.Export(Data);
            SourceBaseline = ObjectMakerJson.Export(Source.Data);
            Notice = "적용 완료 · " + result.PrefabPath;
            return result;
        }
    }

    /// <summary>Project-local recovery, including GUID/local-ID references across Editor restarts.</summary>
    public static class ObjectWorkspaceRecovery
    {
        [Serializable] public sealed class Snapshot
        {
            public int version = 1;
            public string activeObjectKey;
            public string activeEventGuid;
            public bool objectActive;
            public int workspaceArea;
            public string monsterKey, playerKey, eventSearch, monsterSearch, playerSearch;
            public List<string> eventGuids = new List<string>();
            public List<Record> objects = new List<Record>();
        }

        [Serializable] public sealed class Record
        {
            public string key, sourceGuid, json, baseline, sourceBaseline, section;
            public int index;
        }

        public static string FilePath => Path.GetFullPath("UserSettings/AllinOneEditorSession.json");

        public static Record Capture(ObjectWorkspaceDocument doc) => new Record
        {
            key = doc.Key, sourceGuid = doc.SourceGuid, json = doc.HasUnresolvedReferences ? doc.UnresolvedJson : ObjectMakerJson.Export(doc.Data),
            baseline = doc.Baseline, sourceBaseline = doc.SourceBaseline, section = doc.Section, index = doc.Index
        };

        public static ObjectWorkspaceDocument Restore(Record record)
        {
            // Resolve everything before creating a live document. Invalid data leaves the recovery file intact.
            var data = ObjectMakerJson.RestoreDraft(record.json, out string[] warnings);
            var doc = ScriptableObject.CreateInstance<ObjectWorkspaceDocument>();
            doc.hideFlags = HideFlags.HideAndDontSave;
            doc.Content = ScriptableObject.CreateInstance<ObjectWorkspaceDraft>();
            doc.Content.hideFlags = HideFlags.HideAndDontSave;
            doc.Key = record.key; doc.SourceGuid = record.sourceGuid;
            doc.Source = AssetDatabase.LoadAssetAtPath<ObjectDefinitionSO>(AssetDatabase.GUIDToAssetPath(record.sourceGuid ?? ""));
            doc.Data = data; doc.Baseline = record.baseline; doc.SourceBaseline = record.sourceBaseline;
            doc.Section = record.section ?? "identity"; doc.Index = record.index;
            doc.UnresolvedJson = warnings.Length > 0 ? record.json : null;
            doc.Notice = warnings.Length > 0 ? "복구 중 찾지 못한 참조: " + string.Join(", ", warnings) : "로컬 초안을 복구했습니다.";
            return doc;
        }

        public static Snapshot Read(string path = null)
        {
            path = path ?? FilePath;
            if (!File.Exists(path)) return new Snapshot();
            Snapshot snapshot = JsonUtility.FromJson<Snapshot>(File.ReadAllText(path));
            if (snapshot == null || snapshot.version != 1 || snapshot.objects == null || snapshot.eventGuids == null)
                throw new FormatException("초안 복구 파일을 읽을 수 없습니다: " + path);
            return snapshot;
        }

        public static void Write(Snapshot snapshot, string path = null)
        {
            path = path ?? FilePath;
            string json = JsonUtility.ToJson(snapshot, true);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, json, new System.Text.UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
            else File.Move(temporary, path);
        }
    }
}
