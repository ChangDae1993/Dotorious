using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace JYW.Game.EventPlay.Editor
{
    [InitializeOnLoad]
    public sealed class AutoSetup : AssetPostprocessor, IPreprocessBuildWithReport
    {
        private const string EventSOScriptGuid = "ae62fcccccff824489cb424ff73c83a1";
        private const string PresentationSceneGuid = "f40f6eb4eff654544a6074c65687655d";
        private const string MemoPrefabGuid = "9447052421f5fa34db48941fdea773ca";
        private const string TooltipPrefabPath = "Assets/Presentation/Prefabs/Tooltip.prefab";
        private const string SessionKey = "JYW.Presentation.AutoSetup.v2";
        private const long FirstRid = 1000000000000000000L;
        private const int MaxPasses = 10000;

        private static readonly string[] ManagerPrefabFields =
        {
            "softSpeechPrefab",
            "hardSpeechPrefab",
            "memoPrefab",
            "tooltipPrefab",
            "eventCamera",
            "choiceCanvasPrefab",
            "choiceContentsPrefab"
        };

        private static readonly string[] ManagerPrefabGuids =
        {
            "00c366de392e1424e871abf62b1dc3b0",
            "b6a66dd42ecd8f74587a206befa695f0",
            MemoPrefabGuid,
            "f95d06c07f91466d9a6407277d3eee04",
            "4401339c56720e24d816b7fe9f2ea2bb",
            "d8140f2b7552adb48890b2a1fe9357a4",
            "6bbda07d145aadc48bf53be54baa87e0"
        };

        private static bool isRunning;
        private static bool pendingRun;
        private static bool deferredUntilEditMode;
        private static bool playPreflightPending;
        private static bool playPreflightQueued;
        private static bool allowNextPlayRequest;

        private sealed class ReferenceEntry
        {
            public long Rid;
            public List<string> Data;
        }

        private struct InlineArray
        {
            public int Start;
            public int End;
            public int Indent;
        }

        static AutoSetup()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.delayCall += RunOnce;
        }

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (isRunning || importedAssets == null || !importedAssets.Any(IsAssetPath)) return;
            QueueRun();
        }

        private static void QueueRun()
        {
            if (pendingRun) return;
            pendingRun = true;
            EditorApplication.delayCall += RunQueued;
        }

        private static void RunQueued()
        {
            pendingRun = false;
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                deferredUntilEditMode = true;
                return;
            }

            if (EditorApplication.isCompiling || EditorApplication.isUpdating || isRunning)
            {
                QueueRun();
                return;
            }

            RunSetup(force: false);
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            switch (state)
            {
                case PlayModeStateChange.ExitingEditMode:
                    if (allowNextPlayRequest)
                    {
                        allowNextPlayRequest = false;
                        return;
                    }

                    // 씬/에셋을 Play 전환 중에 수정하지 않도록 첫 요청을 취소한 뒤
                    // 완전한 Edit Mode에서 자동 설정하고 Play를 다시 시작한다.
                    playPreflightPending = true;
                    EditorApplication.isPlaying = false;
                    QueuePlayPreflight();
                    break;

                case PlayModeStateChange.EnteredEditMode:
                    if (playPreflightPending)
                    {
                        QueuePlayPreflight();
                    }
                    else if (deferredUntilEditMode)
                    {
                        deferredUntilEditMode = false;
                        QueueRun();
                    }
                    break;

                case PlayModeStateChange.EnteredPlayMode:
                    playPreflightPending = false;
                    playPreflightQueued = false;
                    allowNextPlayRequest = false;
                    break;
            }
        }

        private static void QueuePlayPreflight()
        {
            if (playPreflightQueued) return;
            playPreflightQueued = true;
            EditorApplication.delayCall += RunPlayPreflight;
        }

        private static void RunPlayPreflight()
        {
            playPreflightQueued = false;
            if (!playPreflightPending) return;

            if (EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode || isRunning)
            {
                QueuePlayPreflight();
                return;
            }

            if (!RunSetup(force: false))
            {
                playPreflightPending = false;
                Debug.LogError("[Presentation AutoSetup] 자동 설정에 실패하여 Play를 시작하지 않았습니다. 위 오류를 확인해 주세요.");
                return;
            }

            playPreflightPending = false;
            allowNextPlayRequest = true;
            EditorApplication.isPlaying = true;
        }

        public static void RunFromCommandLine()
        {
            bool completed = RunSetup(force: true);
            if (Application.isBatchMode) EditorApplication.Exit(completed ? 0 : 1);
        }

        private static void RunOnce()
        {
            if (SessionState.GetBool(SessionKey, false)) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                deferredUntilEditMode = true;
                return;
            }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += RunOnce;
                return;
            }

            bool completed = RunSetup(force: false);
            if (completed) SessionState.SetBool(SessionKey, true);
        }

        private static bool RunSetup(bool force)
        {
            if (isRunning) return false;
            isRunning = true;
            bool completed = true;
            int migrated = 0;
            try
            {
                if (!RegisterPresentationScene()) completed = false;
                if (!EnsureTooltipPrefab()) completed = false;
                if (!RepairPresentationManagerWiring()) completed = false;
                migrated = ScanEventSOAssets(ref completed);

                // Refresh 중 발생하는 OnPostprocessAllAssets가 자기 자신을 다시 예약하지 않도록
                // isRunning 보호 범위 안에서 갱신한다.
                if (migrated > 0)
                    AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            }
            catch (Exception exception)
            {
                completed = false;
                Debug.LogError($"[Presentation AutoSetup] 자동 설정을 완료하지 못했습니다.\n{exception}");
            }
            finally
            {
                isRunning = false;
            }

            if (migrated > 0)
            {
                Debug.Log($"[Presentation AutoSetup] 기존 EventSO {migrated}개를 데이터 보존 형식으로 변환했습니다. 원본 백업은 Library/PresentationAutoSetupBackups에 있습니다.");
            }
            else if (force && completed)
            {
                Debug.Log("[Presentation AutoSetup] 설정이 완료되어 있습니다.");
            }

            return completed;
        }

        private static bool EnsureTooltipPrefab()
        {
            GameObject existingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(TooltipPrefabPath);
            if (existingPrefab != null)
            {
                if (existingPrefab.GetComponent<MemoCanvas>() != null &&
                    existingPrefab.GetComponentInChildren<Image>(true) != null &&
                    existingPrefab.GetComponentInChildren<Text>(true) != null)
                    return true;

                Debug.LogError("[Presentation AutoSetup] 기존 Tooltip 프리팹이 MemoCanvas 구조가 아닙니다. 사용자 프리팹을 덮어쓰지 않았습니다.");
                return false;
            }

            string memoPrefabPath = AssetDatabase.GUIDToAssetPath(MemoPrefabGuid);
            GameObject memoPrefab = string.IsNullOrEmpty(memoPrefabPath)
                ? null
                : AssetDatabase.LoadAssetAtPath<GameObject>(memoPrefabPath);
            if (memoPrefab == null)
            {
                Debug.LogError("[Presentation AutoSetup] Tooltip의 기준이 되는 MemoCanvas 프리팹을 찾지 못했습니다.");
                return false;
            }

            if (!AssetDatabase.CopyAsset(memoPrefabPath, TooltipPrefabPath))
            {
                Debug.LogError("[Presentation AutoSetup] MemoCanvas 구조의 Tooltip 프리팹을 만들지 못했습니다.");
                return false;
            }

            GameObject root = null;
            try
            {
                root = PrefabUtility.LoadPrefabContents(TooltipPrefabPath);
                MemoCanvas view = root.GetComponent<MemoCanvas>();
                Image background = root.GetComponentInChildren<Image>(true);
                Text text = root.GetComponentInChildren<Text>(true);
                if (view == null || background == null || text == null)
                    throw new InvalidOperationException("복사한 MemoCanvas 프리팹의 필수 Canvas/Image/Text/IEventUI 연결이 없습니다.");

                root.name = "Tooltip";
                background.gameObject.name = "TooltipBackground";
                background.color = Color.black;
                text.gameObject.name = "TooltipText";
                text.text = "Tooltip";
                text.color = Color.white;

                if (PrefabUtility.SaveAsPrefabAsset(root, TooltipPrefabPath) == null)
                    throw new InvalidOperationException("Tooltip 프리팹 저장에 실패했습니다.");

                AssetDatabase.ImportAsset(TooltipPrefabPath, ImportAssetOptions.ForceUpdate);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError($"[Presentation AutoSetup] Tooltip 프리팹 자동 생성에 실패했습니다.\n{exception}");
                AssetDatabase.DeleteAsset(TooltipPrefabPath);
                return false;
            }
            finally
            {
                if (root != null)
                    PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static bool RegisterPresentationScene()
        {
            string scenePath = AssetDatabase.GUIDToAssetPath(PresentationSceneGuid);
            if (string.IsNullOrEmpty(scenePath))
            {
                Debug.LogError("[Presentation AutoSetup] Presentation.unity 또는 해당 .meta 파일을 찾지 못했습니다.");
                return false;
            }

            var scenes = EditorBuildSettings.scenes.ToList();
            int index = scenes.FindIndex(scene => PathsEqual(scene.path, scenePath));
            bool changed = false;
            if (index < 0)
            {
                scenes.Add(new EditorBuildSettingsScene(scenePath, true));
                changed = true;
            }
            else if (!scenes[index].enabled)
            {
                scenes[index] = new EditorBuildSettingsScene(scenePath, true);
                changed = true;
            }

            int duplicateNames = scenes.Count(scene => scene.enabled &&
                string.Equals(Path.GetFileNameWithoutExtension(scene.path), "Presentation", StringComparison.OrdinalIgnoreCase));
            if (duplicateNames > 1)
                Debug.LogWarning("[Presentation AutoSetup] 활성화된 Presentation 이름의 씬이 둘 이상입니다. 기존 씬 목록은 삭제하거나 재정렬하지 않았습니다.");

            if (changed) EditorBuildSettings.scenes = scenes.ToArray();
            return true;
        }

        private static bool RepairPresentationManagerWiring()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return false;

            string scenePath = AssetDatabase.GUIDToAssetPath(PresentationSceneGuid);
            if (string.IsNullOrEmpty(scenePath)) return false;

            Scene scene = SceneManager.GetSceneByPath(scenePath);
            bool openedForSetup = !scene.IsValid() || !scene.isLoaded;
            bool wasDirty = scene.IsValid() && scene.isLoaded && scene.isDirty;
            try
            {
                if (openedForSetup)
                    scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);

                if (!scene.IsValid() || !scene.isLoaded)
                {
                    Debug.LogError("[Presentation AutoSetup] Presentation 씬을 열 수 없습니다.");
                    return false;
                }

                var managers = new List<EventPlayManager>();
                GameObject[] roots = scene.GetRootGameObjects();
                for (int i = 0; i < roots.Length; i++)
                    managers.AddRange(roots[i].GetComponentsInChildren<EventPlayManager>(true));

                EventPlayManager manager;
                bool sceneChanged = false;
                if (managers.Count == 0)
                {
                    var managerObject = new GameObject("EventPlayManager");
                    SceneManager.MoveGameObjectToScene(managerObject, scene);
                    manager = managerObject.AddComponent<EventPlayManager>();
                    sceneChanged = true;
                }
                else if (managers.Count == 1)
                {
                    manager = managers[0];
                }
                else
                {
                    Debug.LogError("[Presentation AutoSetup] Presentation 씬에 EventPlayManager가 둘 이상입니다. 사용자 오브젝트를 임의 삭제하지 않았습니다.");
                    return false;
                }

                var serializedManager = new SerializedObject(manager);
                serializedManager.Update();
                for (int i = 0; i < ManagerPrefabFields.Length; i++)
                {
                    SerializedProperty property = serializedManager.FindProperty(ManagerPrefabFields[i]);
                    string prefabPath = string.Equals(
                        ManagerPrefabFields[i],
                        "tooltipPrefab",
                        StringComparison.Ordinal)
                        ? TooltipPrefabPath
                        : AssetDatabase.GUIDToAssetPath(ManagerPrefabGuids[i]);
                    GameObject prefab = string.IsNullOrEmpty(prefabPath)
                        ? null
                        : AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

                    if (property == null || prefab == null)
                    {
                        Debug.LogError($"[Presentation AutoSetup] '{ManagerPrefabFields[i]}'에 연결할 Presentation 프리팹을 찾지 못했습니다.");
                        return false;
                    }

                    if (property.objectReferenceValue == null)
                    {
                        property.objectReferenceValue = prefab;
                        sceneChanged = true;
                    }
                }

                if (serializedManager.ApplyModifiedPropertiesWithoutUndo()) sceneChanged = true;
                if (sceneChanged)
                {
                    EditorUtility.SetDirty(manager);
                    EditorSceneManager.MarkSceneDirty(scene);
                    if ((openedForSetup || !wasDirty) && !EditorSceneManager.SaveScene(scene))
                    {
                        Debug.LogError("[Presentation AutoSetup] Presentation 씬의 자동 연결을 저장하지 못했습니다.");
                        return false;
                    }
                    if (!openedForSetup && wasDirty)
                        Debug.Log("[Presentation AutoSetup] 열려 있던 Presentation 씬의 빈 참조를 복구했습니다. 기존 저장되지 않은 변경은 그대로 유지됩니다.");
                }

                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError($"[Presentation AutoSetup] Presentation 프리팹 자동 연결에 실패했습니다.\n{exception}");
                return false;
            }
            finally
            {
                if (openedForSetup && scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        public int callbackOrder => -10000;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (!RunSetup(force: false))
                throw new BuildFailedException("Presentation 자동 설정에 실패하여 빌드를 중단했습니다.");
        }

        private static int ScanEventSOAssets(ref bool completed)
        {
            int count = 0;
            string assetsRoot = Path.GetFullPath(Application.dataPath);
            foreach (string absolutePath in Directory.EnumerateFiles(assetsRoot, "*.asset", SearchOption.AllDirectories))
            {
                string fullPath = Path.GetFullPath(absolutePath);
                if (!IsWithin(fullPath, assetsRoot)) continue;
                string suffix = fullPath.Substring(assetsRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string asset = "Assets/" + suffix.Replace('\\', '/');
                try
                {
                    if (TryMigrateFile(asset, out _)) count++;
                }
                catch (Exception exception)
                {
                    completed = false;
                    Debug.LogError($"[Presentation AutoSetup] '{asset}' 변환을 건너뛰고 나머지 에셋을 계속 확인합니다.\n{exception}");
                }
            }
            return count;
        }

        internal static bool TryMigrateFile(string assetPath, out string backupPath)
        {
            backupPath = null;
            if (!IsAssetPath(assetPath)) return false;

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string absolutePath = Path.GetFullPath(Path.Combine(projectRoot, assetPath));
            if (!IsWithin(absolutePath, projectRoot) || !File.Exists(absolutePath)) return false;

            byte[] originalBytes = File.ReadAllBytes(absolutePath);
            if (!ContainsAscii(originalBytes, EventSOScriptGuid)) return false;

            bool bom = originalBytes.Length >= 3 && originalBytes[0] == 0xEF && originalBytes[1] == 0xBB && originalBytes[2] == 0xBF;
            int offset = bom ? 3 : 0;
            string original = new UTF8Encoding(false, true).GetString(originalBytes, offset, originalBytes.Length - offset);
            if (!TryMigrateText(original, out string migrated, out int migratedSteps, out string error))
            {
                if (!string.IsNullOrEmpty(error)) throw new InvalidDataException(error);
                return false;
            }
            if (migratedSteps == 0 || string.Equals(original, migrated, StringComparison.Ordinal)) return false;

            backupPath = CreateBackup(projectRoot, assetPath, originalBytes);
            byte[] payload = new UTF8Encoding(false).GetBytes(migrated);
            byte[] output = payload;
            if (bom)
            {
                output = new byte[payload.Length + 3];
                output[0] = 0xEF;
                output[1] = 0xBB;
                output[2] = 0xBF;
                Buffer.BlockCopy(payload, 0, output, 3, payload.Length);
            }

            string temporary = absolutePath + ".presentation-autosetup.tmp";
            try
            {
                File.WriteAllBytes(temporary, output);
                File.Copy(temporary, absolutePath, true);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
            return true;
        }

        internal static bool TryMigrateText(string original, out string migrated, out int stepCount, out string error)
        {
            migrated = original;
            stepCount = 0;
            error = null;
            if (string.IsNullOrEmpty(original) || original.IndexOf(EventSOScriptGuid, StringComparison.OrdinalIgnoreCase) < 0)
                return false;

            string newline = original.IndexOf("\r\n", StringComparison.Ordinal) >= 0 ? "\r\n" : "\n";
            bool trailingNewline = original.EndsWith("\n", StringComparison.Ordinal) || original.EndsWith("\r", StringComparison.Ordinal);
            string normalized = original.Replace("\r\n", "\n").Replace('\r', '\n');
            var lines = new List<string>(normalized.Split(new[] { '\n' }, StringSplitOptions.None));
            if (trailingNewline && lines.Count > 0 && lines[lines.Count - 1].Length == 0) lines.RemoveAt(lines.Count - 1);

            var documents = FindDocuments(lines);
            for (int documentIndex = documents.Count - 1; documentIndex >= 0; documentIndex--)
            {
                int start = documents[documentIndex];
                int end = documentIndex + 1 < documents.Count ? documents[documentIndex + 1] : lines.Count;
                if (!DocumentContainsEventSO(lines, start, end)) continue;

                var document = lines.GetRange(start, end - start);
                if (!TryMigrateDocument(document, out int converted, out error))
                {
                    migrated = original;
                    stepCount = 0;
                    return false;
                }
                if (converted == 0) continue;

                lines.RemoveRange(start, end - start);
                lines.InsertRange(start, document);
                stepCount += converted;
            }

            if (stepCount == 0) return false;
            migrated = string.Join(newline, lines);
            if (trailingNewline) migrated += newline;
            return true;
        }

        private static bool TryMigrateDocument(List<string> lines, out int count, out string error)
        {
            count = 0;
            error = null;
            var usedRids = CollectRids(lines);
            long maximum = usedRids.Where(value => value > 0).DefaultIfEmpty(FirstRid - 1).Max();
            if (maximum == long.MaxValue)
            {
                error = "EventSO managed-reference RID 공간이 부족합니다. 원본은 변경하지 않았습니다.";
                return false;
            }
            long nextRid = Math.Max(FirstRid, maximum + 1);
            var entries = new List<ReferenceEntry>();

            for (int pass = 0; pass < MaxPasses; pass++)
            {
                if (!TryFindDeepestInlineArray(lines, out InlineArray array)) break;
                if (!ConvertArray(lines, array, entries, usedRids, ref nextRid, out int converted, out error)) return false;
                count += converted;
            }
            if (TryFindDeepestInlineArray(lines, out _))
            {
                error = $"EventSO 변환이 최대 {MaxPasses}단계를 초과했습니다. 원본은 변경하지 않았습니다.";
                return false;
            }
            if (entries.Count == 0) return true;
            return AppendReferenceEntries(lines, entries, out error);
        }

        private static List<int> FindDocuments(List<string> lines)
        {
            var starts = new List<int>();
            for (int i = 0; i < lines.Count; i++)
                if (lines[i].StartsWith("--- !u!", StringComparison.Ordinal)) starts.Add(i);
            if (starts.Count == 0) starts.Add(0);
            return starts;
        }

        private static bool DocumentContainsEventSO(List<string> lines, int start, int end)
        {
            for (int i = start; i < end; i++)
                if (lines[i].IndexOf("m_Script:", StringComparison.Ordinal) >= 0 &&
                    lines[i].IndexOf(EventSOScriptGuid, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private static bool TryFindDeepestInlineArray(List<string> lines, out InlineArray result)
        {
            result = default;
            bool found = false;
            for (int i = 0; i < lines.Count; i++)
            {
                if (!string.Equals(lines[i].Trim(), "EventExe:", StringComparison.Ordinal)) continue;
                int eventIndent = CountIndent(lines[i]);
                for (int j = i + 1; j < lines.Count; j++)
                {
                    if (string.IsNullOrWhiteSpace(lines[j])) continue;
                    int indent = CountIndent(lines[j]);
                    if (indent <= eventIndent) break;
                    if (indent != eventIndent + 2 || !string.Equals(lines[j].Trim(), "ConditionSteps:", StringComparison.Ordinal)) continue;

                    int start = j + 1;
                    int end = FindArrayEnd(lines, start, indent);
                    bool inline = false;
                    for (int k = start; k < end; k++)
                    {
                        if (CountIndent(lines[k]) == indent && lines[k].TrimStart().StartsWith("- Flags:", StringComparison.Ordinal))
                        {
                            inline = true;
                            break;
                        }
                    }
                    if (inline && (!found || indent > result.Indent))
                    {
                        result = new InlineArray { Start = start, End = end, Indent = indent };
                        found = true;
                    }
                    break;
                }
            }
            return found;
        }

        private static int FindArrayEnd(List<string> lines, int start, int listIndent)
        {
            int end = start;
            for (; end < lines.Count; end++)
            {
                if (string.IsNullOrWhiteSpace(lines[end])) continue;
                int indent = CountIndent(lines[end]);
                if (indent < listIndent) break;
                if (indent == listIndent && !lines[end].TrimStart().StartsWith("- ", StringComparison.Ordinal)) break;
            }
            return end;
        }

        private static bool ConvertArray(List<string> lines, InlineArray array, List<ReferenceEntry> entries,
            HashSet<long> usedRids, ref long nextRid, out int converted, out string error)
        {
            converted = 0;
            error = null;
            var replacement = new List<string>();
            int cursor = array.Start;
            while (cursor < array.End)
            {
                if (string.IsNullOrWhiteSpace(lines[cursor]))
                {
                    replacement.Add(lines[cursor++]);
                    continue;
                }
                if (CountIndent(lines[cursor]) != array.Indent || !lines[cursor].TrimStart().StartsWith("- ", StringComparison.Ordinal))
                {
                    error = $"EventExe.ConditionSteps YAML 형식을 해석할 수 없습니다(line {cursor + 1}). 원본은 변경하지 않았습니다.";
                    return false;
                }

                int itemEnd = cursor + 1;
                while (itemEnd < array.End)
                {
                    if (!string.IsNullOrWhiteSpace(lines[itemEnd]) && CountIndent(lines[itemEnd]) == array.Indent &&
                        lines[itemEnd].TrimStart().StartsWith("- ", StringComparison.Ordinal)) break;
                    itemEnd++;
                }

                string marker = lines[cursor].TrimStart();
                if (marker.StartsWith("- rid:", StringComparison.Ordinal))
                {
                    for (int i = cursor; i < itemEnd; i++) replacement.Add(lines[i]);
                }
                else if (marker.StartsWith("- Flags:", StringComparison.Ordinal))
                {
                    long rid = AllocateRid(usedRids, ref nextRid);
                    replacement.Add(new string(' ', array.Indent) + "- rid: " + rid);
                    entries.Add(new ReferenceEntry { Rid = rid, Data = ConvertItem(lines, cursor, itemEnd, array.Indent) });
                    converted++;
                }
                else
                {
                    error = $"지원하지 않는 EventExe.ConditionSteps 요소입니다(line {cursor + 1}). 원본은 변경하지 않았습니다.";
                    return false;
                }
                cursor = itemEnd;
            }

            lines.RemoveRange(array.Start, array.End - array.Start);
            lines.InsertRange(array.Start, replacement);
            return converted > 0;
        }

        private static List<string> ConvertItem(List<string> lines, int start, int end, int listIndent)
        {
            var data = new List<string>(end - start);
            string first = lines[start].TrimStart();
            data.Add("        " + first.Substring(2));
            int shift = 6 - listIndent;
            for (int i = start + 1; i < end; i++)
            {
                string line = lines[i];
                if (line.Length == 0) { data.Add(line); continue; }
                int indent = CountIndent(line);
                data.Add(new string(' ', Math.Max(0, indent + shift)) + line.Substring(indent));
            }
            return data;
        }

        private static bool AppendReferenceEntries(List<string> lines, List<ReferenceEntry> entries, out string error)
        {
            error = null;
            while (lines.Count > 0 && lines[lines.Count - 1].Length == 0) lines.RemoveAt(lines.Count - 1);
            int references = lines.FindIndex(line => string.Equals(line, "  references:", StringComparison.Ordinal));
            int insertion;
            if (references < 0)
            {
                lines.Add("  references:");
                lines.Add("    version: 2");
                lines.Add("    RefIds:");
                insertion = lines.Count;
            }
            else
            {
                int refIds = -1;
                for (int i = references + 1; i < lines.Count; i++)
                {
                    if (string.Equals(lines[i], "    RefIds:", StringComparison.Ordinal)) { refIds = i; break; }
                    if (string.Equals(lines[i], "    RefIds: []", StringComparison.Ordinal))
                    {
                        lines[i] = "    RefIds:";
                        refIds = i;
                        break;
                    }
                    if (CountIndent(lines[i]) <= 2 && !string.IsNullOrWhiteSpace(lines[i])) break;
                }
                if (refIds < 0)
                {
                    error = "기존 references 블록 형식을 해석할 수 없습니다. 원본은 변경하지 않았습니다.";
                    return false;
                }
                insertion = lines.Count;
            }

            var serialized = new List<string>();
            foreach (ReferenceEntry entry in entries.OrderBy(value => value.Rid))
            {
                serialized.Add("    - rid: " + entry.Rid);
                serialized.Add("      type: {class: EventSO/EventStep, ns: JYW.Game.EventPlay, asm: JYW.Framework}");
                serialized.Add("      data:");
                serialized.AddRange(entry.Data);
            }
            lines.InsertRange(insertion, serialized);
            return true;
        }

        private static HashSet<long> CollectRids(IEnumerable<string> lines)
        {
            var values = new HashSet<long>();
            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("- rid:", StringComparison.Ordinal) &&
                    long.TryParse(trimmed.Substring(6).Trim(), out long rid)) values.Add(rid);
            }
            return values;
        }

        private static long AllocateRid(HashSet<long> used, ref long nextRid)
        {
            while (nextRid <= 0 || used.Contains(nextRid))
            {
                if (nextRid == long.MaxValue) throw new InvalidDataException("사용 가능한 managed-reference RID가 없습니다.");
                nextRid++;
            }
            long value = nextRid;
            used.Add(value);
            if (nextRid < long.MaxValue) nextRid++;
            return value;
        }

        private static string CreateBackup(string projectRoot, string assetPath, byte[] bytes)
        {
            string hash;
            using (SHA256 sha = SHA256.Create())
                hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();

            string backupRoot = Path.GetFullPath(Path.Combine(projectRoot, "Library", "PresentationAutoSetupBackups"));
            string relative = assetPath.Replace('/', Path.DirectorySeparatorChar);
            string backup = Path.GetFullPath(Path.Combine(backupRoot, relative + "." + hash + ".bak"));
            if (!IsWithin(backup, backupRoot)) throw new InvalidDataException("백업 경로가 프로젝트 Library 밖으로 벗어났습니다.");
            Directory.CreateDirectory(Path.GetDirectoryName(backup));
            if (!File.Exists(backup)) File.WriteAllBytes(backup, bytes);
            return backup;
        }

        private static int CountIndent(string line)
        {
            int count = 0;
            while (count < line.Length && line[count] == ' ') count++;
            return count;
        }

        private static bool ContainsAscii(byte[] bytes, string value)
        {
            if (bytes == null || string.IsNullOrEmpty(value) || bytes.Length < value.Length) return false;
            for (int start = 0; start <= bytes.Length - value.Length; start++)
            {
                int offset = 0;
                while (offset < value.Length && bytes[start + offset] == (byte)value[offset]) offset++;
                if (offset == value.Length) return true;
            }
            return false;
        }

        private static bool IsAssetPath(string path)
        {
            return !string.IsNullOrEmpty(path) && path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) &&
                   path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsWithin(string path, string root)
        {
            string normalizedPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return normalizedPath.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
                   normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                   normalizedPath.StartsWith(normalizedRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private static bool PathsEqual(string left, string right)
        {
            return string.Equals(left?.Replace('\\', '/'), right?.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);
        }
    }
}
