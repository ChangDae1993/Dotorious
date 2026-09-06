using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace JYW.Game.ObjectMaker.Editor
{
    /// <summary>Editor-only JSON exchange. Runtime Definition and prefab serialization are unchanged.</summary>
    public static class ObjectMakerJson
    {
        [Serializable]
        private sealed class Document
        {
            public string format;
            public int version;
            public ObjectDefinitionData data;
            public List<AssetReference> assetReferences;
        }

        [Serializable]
        private sealed class AssetReference
        {
            public string field;
            public string guid;
            public long localId;
            public string assetPath;
            public string assetName;
            public string type;
        }

        public static string Export(ObjectDefinitionData source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            ObjectDefinitionData copy = source.Clone();
            var references = new List<AssetReference>();
            VisitObjects(copy, "", (owner, field, path) =>
            {
                Object asset = field.GetValue(owner) as Object;
                if (asset != null)
                {
                    string assetPath = AssetDatabase.GetAssetPath(asset);
                    if (string.IsNullOrEmpty(assetPath) ||
                        !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string guid, out long localId))
                        throw new InvalidOperationException("프로젝트에 저장된 에셋만 내보낼 수 있습니다: " + path);
                    references.Add(new AssetReference
                    {
                        field = path, guid = guid, localId = localId, assetPath = assetPath,
                        assetName = asset.name, type = asset.GetType().FullName
                    });
                }
                // Unity instance IDs only survive one Editor session; never put them in portable JSON.
                field.SetValue(owner, null);
            });
            return JsonUtility.ToJson(new Document
            {
                format = "2DObjectMaker", version = 1, data = copy, assetReferences = references
            }, true);
        }

        public static ObjectDefinitionData Import(string json, out string[] warnings)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new FormatException("JSON 파일이 비어 있습니다.");
            Document document;
            try { document = JsonUtility.FromJson<Document>(json); }
            catch (Exception exception) { throw new FormatException("올바른 JSON이 아닙니다.", exception); }
            if (document == null || document.format != "2DObjectMaker" || document.version != 1 ||
                document.data == null)
                throw new FormatException("지원하지 않는 2DObjectMaker JSON입니다. format, version=1, data를 확인하세요.");

            var fields = new Dictionary<string, Action<Object>>();
            var types = new Dictionary<string, Type>();
            VisitObjects(document.data, "", (owner, field, path) =>
            {
                field.SetValue(owner, null);
                fields[path] = value => field.SetValue(owner, value);
                types[path] = field.FieldType;
            });
            var messages = new List<string>();
            var seen = new HashSet<string>();
            foreach (AssetReference reference in document.assetReferences ?? new List<AssetReference>())
            {
                if (reference == null || string.IsNullOrEmpty(reference.field) ||
                    !seen.Add(reference.field) || !fields.ContainsKey(reference.field))
                    throw new FormatException("중복되거나 올바르지 않은 에셋 필드가 있습니다.");
                Object asset = Resolve(reference, types[reference.field]);
                fields[reference.field](asset);
                if (asset == null)
                    messages.Add(reference.field + ": 에셋을 찾지 못했습니다 (" + reference.assetPath + ")");
            }
            document.data.Sanitize();
            warnings = messages.ToArray();
            return document.data;
        }

        public static void ExportFile(string path, ObjectDefinitionData source)
        {
            string json = Export(source);
            File.WriteAllText(path, json, new System.Text.UTF8Encoding(false));
        }

        public static ObjectDefinitionData ImportFile(string path, out string[] warnings)
        {
            return Import(File.ReadAllText(path), out warnings);
        }

        private static Object Resolve(AssetReference reference, Type expectedType)
        {
            string path = AssetDatabase.GUIDToAssetPath(reference.guid);
            if (!string.IsNullOrEmpty(path))
            {
                foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (asset != null && expectedType.IsInstanceOfType(asset) &&
                        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string _, out long id) &&
                        id == reference.localId)
                        return asset;
            }
            // A copied project may regenerate GUIDs. Match the same path, name and type as a fallback.
            if (!string.IsNullOrEmpty(reference.assetPath) &&
                reference.assetPath.StartsWith("Assets/", StringComparison.Ordinal))
            {
                foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(reference.assetPath))
                    if (asset != null && expectedType.IsInstanceOfType(asset) &&
                        asset.name == reference.assetName && asset.GetType().FullName == reference.type)
                        return asset;
            }
            return null;
        }

        private static void VisitObjects(object value, string path, Action<object, FieldInfo, string> visit)
        {
            if (value == null) return;
            Type type = value.GetType();
            if (type.IsValueType || value is string) return;
            if (value is IList list)
            {
                for (int i = 0; i < list.Count; i++)
                    VisitObjects(list[i], path + "[" + i + "]", visit);
                return;
            }
            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (field.IsNotSerialized) continue;
                string childPath = string.IsNullOrEmpty(path) ? field.Name : path + "." + field.Name;
                if (typeof(Object).IsAssignableFrom(field.FieldType))
                    visit(value, field, childPath);
                else
                    VisitObjects(field.GetValue(value), childPath, visit);
            }
        }
    }
}

