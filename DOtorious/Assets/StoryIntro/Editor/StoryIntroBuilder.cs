using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Dotorious.StoryIntro.Editor
{
    public static class StoryIntroBuilder
    {
        public const string ScenePath = "Assets/StoryIntro/Scenes/" + StoryIntroEvents.SceneName + ".unity";
        private const string PixelPath = "Assets/StoryIntro/Art/IntroPixel.png";
        private static Sprite pixel;
        private static Font font;
        private static readonly Color Snow = R(211, 230, 232);
        private static readonly Color Bark = R(99, 88, 84);
        private static readonly Color Ice = R(134, 168, 180);
        private static readonly Color Pine = R(57, 92, 101);

        [MenuItem("Dotorious/Storyboard/Open Native SB-002 to SB-006")]
        public static void Open()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                const string message = "Build the native storyboard first from Dotorious > Storyboard.";
                if (Application.isBatchMode) throw new InvalidOperationException(message);
                EditorUtility.DisplayDialog("Storyboard Not Built", message, "OK");
                return;
            }
            if (Application.isBatchMode)
            {
                if (Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).Any(x => x.isDirty))
                    throw new InvalidOperationException("The active scene setup has unsaved changes.");
            }
            else if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Debug.Log("Opened native storyboard: " + ScenePath);
        }

        [MenuItem("Dotorious/Storyboard/Build Native SB-002 to SB-006")]
        public static void Build()
        {
            bool exists = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null;
            if (exists)
            {
                if (Application.isBatchMode &&
                    !Environment.GetCommandLineArgs().Contains("-rebuildNativeStoryboard"))
                {
                    Debug.Log("Keeping existing scene and storyboard assets. Pass -rebuildNativeStoryboard to replace them.");
                    return;
                }
                if (!Application.isBatchMode && !EditorUtility.DisplayDialog("Rebuild Native Storyboard?",
                    "Rebuilding replaces the prototype scene and its ten editable EventSO assets. Other scenes are unchanged.",
                    "Replace Storyboard", "Keep Existing")) return;
            }

            var prior = SceneManager.GetActiveScene();
            var target = SceneManager.GetSceneByPath(ScenePath);
            bool wasOpen = target.IsValid() && target.isLoaded;
            if (wasOpen && target.isDirty) throw new InvalidOperationException("Save or close the storyboard before rebuilding.");
            if (!wasOpen)
            {
                bool untitled = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt)
                    .Any(s => s.isLoaded && string.IsNullOrEmpty(s.path));
                if (untitled && !(Application.isBatchMode && SceneManager.sceneCount == 1 && !prior.isDirty))
                {
                    const string message = "Save or close the untitled scene before building the storyboard.";
                    if (Application.isBatchMode) throw new InvalidOperationException(message);
                    EditorUtility.DisplayDialog("Untitled Scene Is Open", message, "OK");
                    return;
                }
                target = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
                    untitled ? NewSceneMode.Single : NewSceneMode.Additive);
            }

            SceneManager.SetActiveScene(target);
            try
            {
                if (wasOpen) foreach (GameObject old in target.GetRootGameObjects()) UnityEngine.Object.DestroyImmediate(old);
                Folder("Assets/StoryIntro/Scenes"); Folder(StoryIntroEvents.EventPath); Folder("Assets/StoryIntro/Art");
                pixel = Pixel();
                font = AssetDatabase.FindAssets("t:Font", new[] { "Assets/Resources/Fonts" })
                    .Select(guid => AssetDatabase.LoadAssetAtPath<Font>(AssetDatabase.GUIDToAssetPath(guid)))
                    .FirstOrDefault(face => face != null) ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

                var world = Node("IntroWorld");
                var actor = Node(StoryIntroEvents.Actor, world, 2.5f, 0);
                actor.tag = "Player";
                var body = actor.gameObject.AddComponent<Rigidbody2D>();
                body.bodyType = RigidbodyType2D.Kinematic;
                body.useFullKinematicContacts = true;
                body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
                var actorCollider = BoxCollider(actor, new Vector2(.62f, 1.12f), new Vector2(0, .61f));
                Transform visual = Person("IntroDotVisual", actor, 0, R(201, 153, 100), R(65, 130, 151));
                visual.gameObject.SetActive(false);
                actor.gameObject.AddComponent<AuthorablePlayerInput2D>()
                    .Configure(visual, StoryIntroEvents.ActualMove, StoryIntroEvents.ActualDash);
                BuildTerrain(world);

                var camera = Node(StoryIntroEvents.Camera);
                camera.position = new Vector3(2.5f, 13f, -10f);
                camera.tag = "MainCamera";
                var cameraComponent = camera.gameObject.AddComponent<Camera>();
                cameraComponent.orthographic = true;
                cameraComponent.orthographicSize = 5.4f;
                cameraComponent.clearFlags = CameraClearFlags.SolidColor;
                cameraComponent.backgroundColor = R(46, 73, 91);
                camera.gameObject.AddComponent<AudioListener>();
                camera.gameObject.AddComponent<AuthorableCameraFollow2D>().Configure(actor);

                BuildUi();
                StoryIntroEvents.Build(Node("IntroEventRegions"), actorCollider);
                if (!EditorSceneManager.SaveScene(target, ScenePath))
                    throw new InvalidOperationException("Could not save " + ScenePath);
                Debug.Log("Saved native storyboard scene and ten conditional EventSOs: " + ScenePath);
            }
            finally
            {
                if (prior.IsValid() && prior.isLoaded && prior != target) SceneManager.SetActiveScene(prior);
                if (!wasOpen && target.IsValid() && target.isLoaded && SceneManager.sceneCount > 1)
                    EditorSceneManager.CloseScene(target, true);
            }
        }

        private static void BuildTerrain(Transform world)
        {
            Box(world, "Ground", 38, -1.6f, 98f, 3.2f, R(57, 74, 84), 0);
            Box(world, "Snow", 38, -.12f, 98f, .24f, Snow, 1);
            Box(world, "Snow Edge", 38, -.35f, 98f, .22f, Ice, 1);

            foreach (float x in new[] { -2.4f, 78.2f })
                BoxCollider(Node("Route Boundary", world, x, 1), new Vector2(.3f, 3), Vector2.zero);
            var residentGate = Node("IntroResidentGate", world, 27.6f, .75f);
            BoxCollider(residentGate, new Vector2(.45f, 1.5f), Vector2.zero);
            Box(residentGate, "Barrier rail", 0, -.15f, .8f, .16f, Bark, 4, 14);
            Box(residentGate, "Barrier post", 0, -.22f, .2f, 1.08f, Bark, 4, -8);

            for (int n = 0; n < 31; n++)
            {
                float x = -13 + 3.45f * n;
                float h = 8.5f + n % 5 * 1.5f;
                Box(world, "Pine trunk", x, h * .44f, .34f, h * .89f, Bark, -3);
                bool destroyed = x > 31 && x < 45;
                if (destroyed)
                {
                    Box(world, "Broken pine", x, .28f, .6f, .67f, Bark, 2, -11);
                    Box(world, "Fallen branch", x + .6f, .18f, 1.1f, .21f, Bark, 2, 18);
                    Box(world, "Machine fragments", x + 1.23f, .14f, .44f, .17f, Ice, 3, -17);
                }
                for (int j = 0; j < 4; j++)
                {
                    float spread = 2.1f - .35f * j;
                    Box(world, "Pine crown", x, h * (.31f + .17f * j), spread, spread,
                        n % 2 == 0 ? Pine : R(62, 106, 111), -2, 45);
                }
                Box(world, "Snow-covered roots", x + .43f, .04f, 1.04f, .12f, Ice, 2, 8);
            }
            var house = Node("IntroNormalHouse", world);
            House(house, R(150, 111, 84), false);
            House(Hidden("IntroDamagedHouse", world), R(111, 87, 72), true);
            Tracks(Hidden("IntroHomeMachineTracks", world), .45f, 4, R(101, 85, 78));
            var outbound = Node("IntroOutboundTrail", world);
            Fence(outbound, 8, false); Fence(outbound, 15.2f, true);
            Tracks(outbound, 13f, 5, Ice);
            Tracks(Hidden("IntroFirstTrackContrast", world), 13f, 5, R(88, 122, 137));
            var changed = Hidden("IntroChangedTrail", world);
            Fence(changed, 8, true); Fence(changed, 15.2f, true);
            Tracks(changed, 5f, 9, R(92, 99, 111));
            Tracks(changed, 17f, 8, R(92, 99, 111));
            Tracks(changed, 38f, 7, R(92, 99, 111));
            Tracks(world, 33.8f, 7, Ice);
            Box(world, "Fallen Guide Sign", 40, .3f, .22f, 1.46f, Bark, 3, -67);
            Box(world, "Sign Board", 40.6f, .57f, .74f, .32f, R(174, 131, 85), 4, 15);
            for (int i = 0; i < 3; i++)
            {
                float x = 48f + i * 4.8f;
                House(Node("Deserted House", world, x), R(98, 127, 129), i == 2);
                Box(world, "Abandoned Bucket", x + 1.58f, .22f, .42f, .43f, Bark, 3, -22);
                Box(world, "Abandoned Basket", x - 1.63f, .15f, .61f, .3f, R(139, 110, 71), 3, 10);
            }
            for (int n = 0; n < 4; n++) Box(world, "Broken Well Tool", 64 + 1.42f * n, .16f, .99f, .12f, Bark, 3, 13);
            var well = Node("Stone Well", world, 72.55f);
            Box(well, "Stonework", 0, .52f, 1.87f, 1.04f, R(113, 143, 144), 3);
            Box(well, "Opening", 0, 1.05f, 1.9f, .31f, R(46, 70, 78), 4);
            Box(well, "Posts", -.81f, 1.82f, .18f, 1.56f, Bark, 3);
            Box(well, "Posts", .81f, 1.82f, .18f, 1.56f, Bark, 3);
            Box(well, "Snowy beam", 0, 2.55f, 2.1f, .28f, Snow, 4);
            Box(well, "Cracked bucket", 1.4f, .19f, .32f, .37f, Bark, 3, 38);
            var resident = Person(StoryIntroEvents.Resident, world, 29f, R(215, 167, 113), R(176, 109, 84));
            resident.localScale = new Vector3(-1, 1.17f, 1); resident.gameObject.SetActive(false);
            var keeper = Hidden(StoryIntroEvents.Keeper, world, 75f);
            Box(keeper, "Coat", 0, .24f, .96f, .36f, R(100, 113, 144), 6, 8);
            Box(keeper, "Face", .63f, .26f, .45f, .36f, R(205, 164, 119), 6, 16);
            Box(keeper, "Scarf", .41f, .23f, .2f, .39f, R(148, 72, 94), 7, 17);
            var machine = Hidden("IntroMachine", world, -1.4f);
            Box(machine, "Chassis", 0, .68f, .9f, 1f, R(55, 67, 78), 6);
            Box(machine, "Head", 0, 1.38f, .76f, .45f, R(110, 126, 138), 6);
            Box(machine, "Sensor", -.25f, 1.42f, .18f, .11f, R(234, 143, 90), 7);
            Box(machine, "Treads", 0, .16f, 1.07f, .33f, R(47, 63, 73), 7);
        }

        private static void BuildUi()
        {
            var root = new GameObject("IntroCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scale = root.GetComponent<CanvasScaler>();
            scale.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scale.referenceResolution = new Vector2(960, 540);
            var title = Ui("IntroTitle", root.transform, Vector2.zero, new Vector2(960, 540));
            TextBox("IntroLogo", title, "DOTORIOUS", 54, new Vector2(0, 103), new Vector2(600, 86));
            TextBox("IntroOtherMenu", title, "CONTINUE     ·     OPTIONS", 16, new Vector2(0, -105), new Vector2(440, 48));
            var buttonRect = Ui("IntroNewGame", title, new Vector2(0, -37), new Vector2(248, 56));
            var image = buttonRect.gameObject.AddComponent<Image>();
            image.color = R(50, 83, 105);
            var button = buttonRect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            TextBox("New Game Label", buttonRect, "NEW GAME", 25, Vector2.zero, buttonRect.sizeDelta);
            var signal = buttonRect.gameObject.AddComponent<EventBooleanButton>();
            signal.Configure(StoryIntroEvents.NewGame);
            UnityEventTools.AddPersistentListener(button.onClick, signal.Publish);
            Toast("IntroNewGameHighlight", title, "▶  NEW GAME  ◀", 25, -37);
            var eventSystem = new GameObject("IntroUIEventSystem", typeof(EventSystem), typeof(StandaloneInputModule))
                .GetComponent<EventSystem>();
            eventSystem.firstSelectedGameObject = button.gameObject;
            Toast("IntroMovePrompt", root.transform, "MOVE  [ ← / → ]", 25, -192);
            Toast("IntroResidentPrompt", root.transform, "INTERACT  [ E ]", 25, -192);
            Toast("IntroKeeperPrompt", root.transform, "INTERACT  [ E ]", 25, -192);
            Toast("IntroDashPrompt", root.transform, "DASH  [ D ]", 25, -192);
            Toast("IntroReturnObjective", root.transform, "집으로 돌아가기", 25, 200);
            Toast(StoryIntroEvents.Forest, root.transform, "파손된 숲길", 19, 165);
            Toast("IntroOutskirtsCue", root.transform, "마을 외곽", 19, 165);
            Toast(StoryIntroEvents.Well, root.transform, "우물로 가는 길", 19, 165);
            var left = TextBox("IntroLeftImpact", root.transform, "◀", 70, new Vector2(-380, 26), new Vector2(120, 95));
            left.gameObject.SetActive(false);
        }

        private static void Toast(string name, Transform parent, string words, int size, float y)
        {
            var rect = Ui(name, parent, new Vector2(0, y), new Vector2(306, 46));
            var image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(.055f, .107f, .152f, .9f);
            image.raycastTarget = false;
            TextBox(name + " Text", rect, words, size, Vector2.zero, rect.sizeDelta);
            rect.gameObject.SetActive(false);
        }

        private static Text TextBox(string name, Transform parent, string value, int size, Vector2 at, Vector2 dimensions)
        {
            var rect = Ui(name, parent, at, dimensions);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = font;
            text.text = value;
            text.fontSize = size;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Snow;
            text.raycastTarget = false;
            return text;
        }

        private static RectTransform Ui(string name, Transform parent, Vector2 at, Vector2 dimensions)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = at;
            rect.sizeDelta = dimensions;
            return rect;
        }

        private static void House(Transform root, Color wall, bool damaged)
        {
            Color roof = R(72, 89, 99);
            Box(root, "Wall", damaged ? -.31f : 0f, .94f, damaged ? 1.9f : 2.55f, 1.88f, wall, 2, damaged ? -7 : 0);
            Box(root, "Roof Left", -.68f, 2.08f, 1.91f, .34f, roof, 3, 29);
            Box(root, "Snowy Roof", -.71f, 2.22f, 1.82f, .12f, Snow, 4, 29);
            Box(root, "Roof Right", .67f, damaged ? .77f : 2.08f, 1.91f, .34f, roof, 3, -29);
            Box(root, "Door", .31f, .64f, .64f, 1.19f, R(67, 76, 79), 3, damaged ? 36 : 0);
            Box(root, "Window", -.66f, 1.23f, .45f, .45f, damaged ? roof : R(227, 196, 122), 4);
        }

        private static void Fence(Transform root, float x, bool broken)
        {
            for (int i = 0; i < 4; i++)
                Box(root, "Fence post", x + i * .56f, broken && i > 0 ? .13f : .39f, .15f, .75f, Bark, 3, broken && i > 0 ? 60 : 0);
            Box(root, "Fence rail", x + .85f, broken ? .31f : .53f, 1.94f, .14f, Bark, 3, broken ? -13 : 0);
        }

        private static void Tracks(Transform root, float x, int count, Color color)
        {
            for (int i = 0; i < count; i++)
                Box(root, "Footprint", x + i * .57f, .035f + i % 2 * .047f, .29f, .065f, color, 5, i % 2 == 0 ? 17 : -15);
        }

        private static Transform Person(string name, Transform parent, float x, Color face, Color coat)
        {
            var p = Node(name, parent, x);
            Box(p, "Body", 0, .39f, .53f, .66f, coat, 6);
            Box(p, "Head", .05f, .91f, .64f, .48f, face, 6);
            Box(p, "Eye", .22f, .96f, .07f, .08f, R(44, 52, 54), 7);
            Box(p, "Boots", .05f, .08f, .51f, .16f, R(56, 62, 70), 7);
            return p;
        }

        private static Transform Hidden(string name, Transform parent, float x = 0)
        {
            var child = Node(name, parent, x);
            child.gameObject.SetActive(false);
            return child;
        }

        private static Transform Node(string name, Transform parent = null, float x = 0, float y = 0)
        {
            var node = new GameObject(name).transform;
            node.SetParent(parent, false);
            node.localPosition = new Vector3(x, y, 0);
            return node;
        }

        private static BoxCollider2D BoxCollider(Transform target, Vector2 size, Vector2 offset)
        {
            var collider = target.gameObject.AddComponent<BoxCollider2D>();
            collider.size = size; collider.offset = offset;
            return collider;
        }

        private static void Box(Transform root, string name, float x, float y, float width, float height,
            Color color, int layer, float angle = 0)
        {
            var child = Node(name, root, x, y);
            child.localRotation = Quaternion.Euler(0, 0, angle);
            child.localScale = new Vector3(width, height, 1);
            var render = child.gameObject.AddComponent<SpriteRenderer>();
            render.sprite = pixel;
            render.color = color;
            render.sortingOrder = layer;
        }

        private static Color R(byte r, byte g, byte b) => new Color32(r, g, b, 255);

        private static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            Folder(path.Substring(0, slash));
            if (string.IsNullOrEmpty(AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1))))
                throw new InvalidOperationException("Could not create asset folder " + path);
        }

        private static Sprite Pixel()
        {
            var saved = AssetDatabase.LoadAssetAtPath<Sprite>(PixelPath);
            if (saved != null) return saved;
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.SetPixel(0, 0, Color.white); texture.Apply();
            File.WriteAllBytes(PixelPath, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(PixelPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(PixelPath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 1f;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Point;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(PixelPath);
        }
    }
}
