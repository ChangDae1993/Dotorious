using System;
using System.IO;
using UnityEditor;

namespace JYW.Game.ObjectMaker.Editor
{
    public static class ObjectMakerPaths
    {
        private const string WindowSuffix = "/Editor/ObjectMakerWindow.cs";

        public static string Root
        {
            get
            {
                string[] guids = AssetDatabase.FindAssets("ObjectMakerWindow t:MonoScript");
                for (int i = 0; i < guids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]).Replace('\\', '/');
                    if (path.EndsWith(WindowSuffix, StringComparison.Ordinal))
                        return path.Substring(0, path.Length - WindowSuffix.Length);
                }

                return "Assets/FrameWork/2DObjectMaker";
            }
        }

        public static string Editor => Root + "/Editor";
        public static string Prefabs => Root + "/Prefabs";
        public static string Resources => Root + "/Resources";
        public static string ResourceObjectMaker => Resources + "/2DObjectMaker";
        public static string Definitions => ResourceObjectMaker + "/Definitions";
        public static string Scripts => Root + "/Scripts";
        public static string Tests => Root + "/Tests";
        public static string EditorTests => Tests + "/Editor";
        public static string PlayModeTests => Tests + "/PlayMode";

        public const string GeneratedRoot = "Assets/Resources";
        public const string GeneratedPrefabs = GeneratedRoot + "/Prefabs";
        public const string GeneratedEnemyPrefabs = GeneratedPrefabs + "/Enemy";
        public const string GeneratedPlayerPrefabs = GeneratedPrefabs + "/Player";
        public const string GeneratedResources = GeneratedRoot;
        public const string GeneratedResourceObjectMaker = GeneratedResources + "/2DObjectMaker";
        public const string GeneratedAnimators = GeneratedResourceObjectMaker + "/Animators";
        public const string GeneratedDefinitions = GeneratedResourceObjectMaker + "/Definitions";
        public const string GeneratedJson = GeneratedResourceObjectMaker + "/JSON";

        public static void EnsureStructure()
        {
            EnsureFolder(Root);
            EnsureFolder(Editor);
            EnsureFolder(Prefabs);
            EnsureFolder(Resources);
            EnsureFolder(ResourceObjectMaker);
            EnsureFolder(Definitions);
            EnsureFolder(Scripts);
            EnsureFolder(Tests);
            EnsureFolder(EditorTests);
            EnsureFolder(PlayModeTests);
        }

        public static void EnsureGeneratedStructure()
        {
            EnsureFolder(GeneratedRoot);
            EnsureFolder(GeneratedPrefabs);
            EnsureFolder(GeneratedEnemyPrefabs);
            EnsureFolder(GeneratedPlayerPrefabs);
            EnsureFolder(GeneratedAnimators);
            EnsureFolder(GeneratedResources);
            EnsureFolder(GeneratedResourceObjectMaker);
            EnsureFolder(GeneratedDefinitions);
            EnsureFolder(GeneratedJson);
        }

        public static string SafeFileName(string value)
        {
            string candidate = string.IsNullOrWhiteSpace(value) ? "Object" : value.Trim();
            char[] invalid = Path.GetInvalidFileNameChars();
            for (int i = 0; i < invalid.Length; i++)
                candidate = candidate.Replace(invalid[i], '_');
            return string.IsNullOrWhiteSpace(candidate) ? "Object" : candidate;
        }

        private static void EnsureFolder(string assetPath)
        {
            assetPath = assetPath.Replace('\\', '/').TrimEnd('/');
            if (AssetDatabase.IsValidFolder(assetPath))
                return;

            int slash = assetPath.LastIndexOf('/');
            if (slash <= 0)
                throw new InvalidOperationException("Assets 아래의 2DObjectMaker 폴더만 만들 수 있습니다: " + assetPath);

            string parent = assetPath.Substring(0, slash);
            string folder = assetPath.Substring(slash + 1);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, folder);
        }
    }
}
