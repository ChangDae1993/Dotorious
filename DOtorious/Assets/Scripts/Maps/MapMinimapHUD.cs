using System.Collections.Generic;
using JYW.Game.ObjectMaker;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Dotorious.Maps
{
    /// <summary>Shows real terrain/background artwork without changing the gameplay renderers.</summary>
    [DisallowMultipleComponent]
    public sealed class MapMinimapHUD : MonoBehaviour
    {
        public Button openButton;
        public Button closeButton;
        public GameObject panel;
        public RawImage mapImage;
        public Text statusText;
        public RectTransform playerMarker;
        public Key toggleKey = Key.M;
        public Material previewMaterial;

        private const int PreviewLayer = 16;
        private static readonly Vector3 PreviewOffset = new Vector3(0, 0, 10000);
        private readonly List<GameObject> copies = new List<GameObject>();
        private readonly Dictionary<Sprite, Sprite> tiledSpriteCopies = new Dictionary<Sprite, Sprite>();
        private ObjectActor2D trackedPlayer;
        private Camera mapCamera;
        private GameObject previewRoot;
        private RenderTexture texture;
        private float previousTimeScale;
        private float refreshAt;
        private int previousSignature;
        private Bounds mapBounds;
        public bool IsOpen { get; private set; }
        public Bounds MapBounds => mapBounds;
        public int VisibleRendererCount => copies.Count;
        public Camera PreviewCamera => mapCamera;

        private void Awake()
        {
            if (panel != null) panel.SetActive(false);
            if (openButton != null) openButton.onClick.AddListener(Open);
            if (closeButton != null) closeButton.onClick.AddListener(Close);
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && toggleKey != Key.None && keyboard[toggleKey].wasPressedThisFrame)
            {
                if (IsOpen) Close(); else Open();
            }
            if (!IsOpen) return;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) { Close(); return; }
            if (Time.unscaledTime >= refreshAt)
            {
                refreshAt = Time.unscaledTime + .5f;
                RefreshMap();
            }
            UpdatePlayerMarker();
        }

        public void Open()
        {
            if (IsOpen || panel == null || mapImage == null) return;
            IsOpen = true;
            previousTimeScale = Time.timeScale;
            var controller = FindAnyObjectByType<ObjectPlayerController2D>();
            trackedPlayer = controller != null ? controller.GetComponent<ObjectActor2D>() : null;
            Time.timeScale = 0;
            panel.SetActive(true);
            Canvas.ForceUpdateCanvases();
            CreatePreviewCamera();
            previousSignature = int.MinValue;
            RefreshMap();
            refreshAt = Time.unscaledTime + .5f;
            UpdatePlayerMarker();
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            if (panel != null) panel.SetActive(false);
            Time.timeScale = previousTimeScale;
            ReleasePreview();
        }

        private void OnDisable() { Close(); }

        private void OnDestroy()
        {
            Close();
            if (openButton != null) openButton.onClick.RemoveListener(Open);
            if (closeButton != null) closeButton.onClick.RemoveListener(Close);
        }

        public static bool IsMapRenderer(Renderer renderer)
        {
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy ||
                renderer.gameObject.layer == PreviewLayer || !(renderer is SpriteRenderer || renderer is MeshRenderer))
                return false;
            var ingredient = renderer.GetComponentInParent<MapIngredient2D>();
            if (ingredient != null) return ingredient.VisibleOnMinimap;
            for (Transform current = renderer.transform; current != null; current = current.parent)
            {
                string tag = current.gameObject.tag;
                if (tag == "Background1" || tag == "Background3") return false;
                if (tag == "Ground" || tag == "Ground_Grass" || tag == "Background2") return true;
            }
            if (renderer.gameObject.layer == LayerMask.NameToLayer("Ground")) return true;
            // Read old map tags too; do not rewrite existing scenes or EventSO object references.
            return renderer.gameObject.tag == "BG" && renderer.gameObject.layer != 6 &&
                   renderer.gameObject.layer != 7 && renderer.gameObject.layer != 8 &&
                   renderer.gameObject.layer != 12 && renderer.gameObject.layer != 13 &&
                   renderer.gameObject.layer != 14;
        }

        public void RefreshMap()
        {
            if (!IsOpen || mapCamera == null) return;
            var renderers = FindObjectsByType<Renderer>();
            int signature = 17;
            Vector2 imageSize = mapImage.rectTransform.rect.size;
            unchecked
            {
                signature = signature * 31 + imageSize.GetHashCode();
                foreach (var renderer in renderers)
                {
                    if (!IsMapRenderer(renderer)) continue;
                    int entry = renderer.GetEntityId().GetHashCode();
                    entry = entry * 31 + renderer.localToWorldMatrix.GetHashCode();
                    entry = entry * 31 + renderer.bounds.GetHashCode();
                    entry = entry * 31 + renderer.sortingOrder;
                    if (renderer is SpriteRenderer sprite)
                    {
                        entry = entry * 31 + (sprite.sprite != null ? sprite.sprite.GetEntityId().GetHashCode() : 0);
                        entry = entry * 31 + sprite.color.GetHashCode();
                        entry = entry * 31 + sprite.flipX.GetHashCode() + sprite.flipY.GetHashCode();
                    }
                    // The no-sort Unity 6 API can enumerate in any order.
                    signature ^= entry;
                }
            }
            if (signature == previousSignature) return;
            previousSignature = signature;
            foreach (var copy in copies) { copy.SetActive(false); Destroy(copy); }
            copies.Clear();
            foreach (var sprite in tiledSpriteCopies.Values) Destroy(sprite);
            tiledSpriteCopies.Clear();
            bool found = false;
            foreach (var source in renderers)
            {
                if (!IsMapRenderer(source)) continue;
                if (!found) { mapBounds = source.bounds; found = true; }
                else mapBounds.Encapsulate(source.bounds);
                CopyRenderer(source);
            }
            if (!found) mapBounds = new Bounds(Vector3.zero, new Vector3(8, 4, 0));
            mapCamera.aspect = Mathf.Max(.1f, imageSize.x / Mathf.Max(1, imageSize.y));
            mapCamera.orthographicSize = Mathf.Max(.5f,
                Mathf.Max(mapBounds.extents.y, mapBounds.extents.x / mapCamera.aspect) * 1.06f);
            mapCamera.transform.position = new Vector3(mapBounds.center.x, mapBounds.center.y, PreviewOffset.z - 100);
            mapCamera.enabled = found;
            if (statusText != null)
                statusText.text = found ? "전체 맵 · 땅 / 뒷배경 · 자동 맞춤" : "표시할 지형이 없습니다. Ground 또는 Background2를 배치하세요.";
        }

        private void CreatePreviewCamera()
        {
            previewRoot = new GameObject("MinimapPreview (runtime only)") { hideFlags = HideFlags.DontSave };
            var cameraObject = new GameObject("MapCamera") { hideFlags = HideFlags.DontSave };
            cameraObject.transform.SetParent(previewRoot.transform);
            mapCamera = cameraObject.AddComponent<Camera>();
            mapCamera.orthographic = true;
            mapCamera.cullingMask = 1 << PreviewLayer;
            mapCamera.clearFlags = CameraClearFlags.SolidColor;
            mapCamera.backgroundColor = new Color(.025f, .045f, .07f, 1);
            mapCamera.nearClipPlane = .1f;
            mapCamera.farClipPlane = 200;
            mapCamera.allowHDR = false;
            mapCamera.allowMSAA = false;
            texture = new RenderTexture(1600, 900, 24) { name = "MapMinimap", filterMode = FilterMode.Point };
            texture.Create();
            mapCamera.targetTexture = texture;
            mapImage.texture = texture;
        }

        private void CopyRenderer(Renderer source)
        {
            var copy = new GameObject(source.name) { layer = PreviewLayer, hideFlags = HideFlags.DontSave };
            copy.transform.SetParent(previewRoot.transform);
            copy.transform.SetPositionAndRotation(source.transform.position + PreviewOffset, source.transform.rotation);
            copy.transform.localScale = source.transform.lossyScale;
            Renderer target;
            if (source is SpriteRenderer sprite)
            {
                var targetSprite = copy.AddComponent<SpriteRenderer>();
                Sprite imageSprite = sprite.sprite;
                if (imageSprite != null && sprite.drawMode != SpriteDrawMode.Simple)
                {
                    // Legacy tiled sprites may use a tight mesh. Only the preview gets a full-rect sprite;
                    // the source import settings, pixels, border and gameplay renderer stay unchanged.
                    if (!tiledSpriteCopies.TryGetValue(imageSprite, out Sprite tiled))
                    {
                        tiled = Sprite.Create(imageSprite.texture, imageSprite.rect,
                            imageSprite.pivot / imageSprite.rect.size, imageSprite.pixelsPerUnit,
                            0, SpriteMeshType.FullRect, imageSprite.border);
                        tiledSpriteCopies.Add(imageSprite, tiled);
                    }
                    imageSprite = tiled;
                }
                targetSprite.sprite = imageSprite;
                targetSprite.color = sprite.color;
                targetSprite.flipX = sprite.flipX;
                targetSprite.flipY = sprite.flipY;
                targetSprite.drawMode = sprite.drawMode;
                targetSprite.size = sprite.size;
                targetSprite.tileMode = sprite.tileMode;
                target = targetSprite;
                target.sharedMaterial = previewMaterial != null ? previewMaterial : source.sharedMaterial;
            }
            else
            {
                var sourceFilter = source.GetComponent<MeshFilter>();
                if (sourceFilter == null || sourceFilter.sharedMesh == null) { Destroy(copy); return; }
                copy.AddComponent<MeshFilter>().sharedMesh = sourceFilter.sharedMesh;
                target = copy.AddComponent<MeshRenderer>();
                target.sharedMaterials = source.sharedMaterials;
            }
            target.sortingLayerID = source.sortingLayerID;
            target.sortingOrder = source.sortingOrder;
            copies.Add(copy);
        }

        private void UpdatePlayerMarker()
        {
            if (playerMarker == null || mapCamera == null) return;
            ObjectActor2D player = trackedPlayer;
            playerMarker.gameObject.SetActive(player != null && copies.Count > 0);
            if (player == null) return;
            Vector3 point = mapCamera.WorldToViewportPoint(player.transform.position + PreviewOffset);
            playerMarker.anchorMin = playerMarker.anchorMax = new Vector2(Mathf.Clamp01(point.x), Mathf.Clamp01(point.y));
            playerMarker.anchoredPosition = Vector2.zero;
        }

        private void ReleasePreview()
        {
            if (mapImage != null) mapImage.texture = null;
            if (mapCamera != null) mapCamera.targetTexture = null;
            if (texture != null) { texture.Release(); Destroy(texture); }
            if (previewRoot != null) { previewRoot.SetActive(false); Destroy(previewRoot); }
            texture = null;
            mapCamera = null;
            previewRoot = null;
            copies.Clear();
            foreach (var sprite in tiledSpriteCopies.Values) Destroy(sprite);
            tiledSpriteCopies.Clear();
        }
    }
}
