using System.Collections;
using JYW.Game.EventPlay;
using JYW.Game.ObjectMaker;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Dotorious.Winter
{
    [DisallowMultipleComponent]
    public sealed class WinterStageRuntime : MonoBehaviour
    {
        public ObjectActor2D player;
        public GameObject playerPrefab;
        public Camera gameplayCamera;
        public GameObject titleUi;
        public GameObject hud;
        public Text healthText;
        public Text distanceText;
        public Text areaText;
        public Image healthFill;
        public Image progressFill;
        public GameObject finishPanel;
        public Text finishText;
        public Button restartButton;
        public Transform enemyRoot;
        public Vector3 checkpoint;
        public int defeated;
        [SerializeField] private MainMapArea2D currentMap;
        private bool started;
        private bool respawning;
        private bool finished;
        private Vector3 cameraOffset;
        private string checkpointName = "숲의 입구";

        public bool HasStarted => started;
        public bool IsFinished => finished;
        public string CheckpointName => checkpointName;
        public MainMapArea2D CurrentMap => currentMap;

        private void Awake()
        {
            if (player != null)
            {
                checkpoint = player.transform.position;
                player.Died += OnPlayerDied;
            }
            if (gameplayCamera != null && player != null)
                cameraOffset = gameplayCamera.transform.position - player.transform.position;
            if (hud != null) hud.SetActive(false);
            if (finishPanel != null) finishPanel.SetActive(false);
            if (restartButton != null) restartButton.onClick.AddListener(Restart);
            if (enemyRoot != null)
                foreach (var enemy in enemyRoot.GetComponentsInChildren<ObjectActor2D>(true))
                    enemy.Died += OnEnemyDied;
        }

        private void OnDestroy()
        {
            if (player != null) player.Died -= OnPlayerDied;
            if (restartButton != null) restartButton.onClick.RemoveListener(Restart);
            if (enemyRoot != null)
                foreach (var enemy in enemyRoot.GetComponentsInChildren<ObjectActor2D>(true))
                    enemy.Died -= OnEnemyDied;
        }

        private void Update()
        {
            if (!started)
            {
                if (titleUi != null && titleUi.activeInHierarchy) return;
                started = true;
                if (gameplayCamera != null && player != null)
                    cameraOffset = gameplayCamera.transform.position - player.transform.position;
                if (hud != null) hud.SetActive(true);
            }
            if (player == null || respawning) return;
            if (!finished && Time.timeScale > 0f && player.transform.position.y < (currentMap != null ? currentMap.FallY : -6f))
                StartCoroutine(Respawn(false));
            float x = Mathf.Clamp(player.transform.position.x, 0f, 300f);
            if (healthText != null) healthText.text = "HP  " + player.CurrentHealth + " / " + player.MaxHealth;
            if (distanceText != null) distanceText.text = Mathf.FloorToInt(x) + " / 300 m";
            if (healthFill != null) healthFill.fillAmount = player.CurrentHealthRatio;
            if (progressFill != null) progressFill.fillAmount = x / 300f;
            if (areaText != null) areaText.text = AreaName(x);
            if (currentMap != null && currentMap.gameObject != gameObject)
            {
                Bounds bounds = currentMap.GroundBounds;
                float length = Mathf.Max(1f, bounds.size.x);
                float distance = Mathf.Clamp(player.transform.position.x - bounds.min.x, 0f, length);
                if (distanceText != null) distanceText.text = Mathf.FloorToInt(distance) + " / " + Mathf.CeilToInt(length) + " m";
                if (progressFill != null) progressFill.fillAmount = distance / length;
                if (areaText != null) areaText.text = currentMap.displayName;
            }
        }

        public void EnterMap(MainMapArea2D map)
        {
            if (map == null || map.entryPoint == null) return;
            currentMap = map;
            SetCheckpoint(map.entryPoint.position, map.displayName);
        }

        public void SetCheckpoint(Vector3 position, string label)
        {
            if (!started || finished) return;
            checkpoint = position;
            checkpointName = label;
            if (player != null) player.RestoreFullHealth();
        }

        public void FinishStage()
        {
            if (finished) return;
            finished = true;
            if (finishText != null)
                finishText.text = "WINTER PATH\n겨울 숲을 통과했습니다\n\n300 m  ·  쓰러뜨린 적 " + defeated;
            if (finishPanel != null) finishPanel.SetActive(true);
        }

        public void Restart()
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene("Main", LoadSceneMode.Single);
        }

        private void OnEnemyDied(ObjectActor2D enemy) { defeated++; }

        private void OnPlayerDied(ObjectActor2D actor)
        {
            if (respawning || !started) return;
            if (gameplayCamera != null)
                gameplayCamera.transform.SetParent(transform, true);
            StartCoroutine(Respawn(true));
        }

        private IEnumerator Respawn(bool recreate)
        {
            respawning = true;
            yield return new WaitForSecondsRealtime(0.45f);
            if (recreate || player == null)
            {
                if (playerPrefab == null) { respawning = false; yield break; }
                GameObject instance = Instantiate(playerPrefab, checkpoint, Quaternion.identity);
                instance.name = "Player";
                player = instance.GetComponent<ObjectActor2D>();
                player.Died += OnPlayerDied;
            }
            else
            {
                player.Body.linearVelocity = Vector2.zero;
                player.transform.position = checkpoint;
                player.RestoreFullHealth();
            }
            if (gameplayCamera != null)
            {
                gameplayCamera.transform.SetParent(player.transform, true);
                gameplayCamera.transform.localPosition = cameraOffset;
            }
            Physics2D.SyncTransforms();
            respawning = false;
        }

        private static string AreaName(float x)
        {
            if (x < 48f) return "01  눈길의 시작";
            if (x < 100f) return "02  서리 정찰로";
            if (x < 150f) return "03  끊어진 다리";
            if (x < 200f) return "04  잊힌 문지기";
            if (x < 250f) return "05  얼음 능선";
            return "06  마지막 불빛";
        }
    }
}
