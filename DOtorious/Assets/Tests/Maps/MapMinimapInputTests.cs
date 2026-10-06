using System.Collections;
using System.Linq;
using Dotorious.Maps;
using JYW.Game.ObjectMaker;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

public class MapMinimapInputTests : InputTestFixture
{
    GameObject hudObject;

    [UnitySetUp] public IEnumerator IsolateFromOtherMainTests()
    {
        Time.timeScale = 1;
        var main = SceneManager.GetSceneByName("Main");
        if (main.IsValid() && main.isLoaded)
        {
            var isolated = SceneManager.CreateScene("MinimapInputTestIsolation");
            SceneManager.SetActiveScene(isolated);
            yield return SceneManager.UnloadSceneAsync(main);
        }
    }

    [UnityTearDown] public IEnumerator CleanupMapHUD()
    {
        if (hudObject != null) Object.Destroy(hudObject);
        Time.timeScale = 1;
        yield return null;
    }

    [UnityTest] public IEnumerator MOpensAndEscapeClosesTheMinimap()
    {
        var keyboard = InputSystem.AddDevice<Keyboard>(); keyboard.MakeCurrent();
        hudObject = Object.Instantiate(Resources.Load<GameObject>("Prefabs/UI/MapMinimapHUD"));
        var hud = hudObject.GetComponent<MapMinimapHUD>();
        Press(keyboard.mKey); yield return null; Release(keyboard.mKey);
        Assert.IsTrue(hud.IsOpen); Assert.AreEqual(0, Time.timeScale);
        yield return null;
        Press(keyboard.escapeKey); yield return null; Release(keyboard.escapeKey);
        Assert.IsFalse(hud.IsOpen); Assert.AreEqual(1, Time.timeScale);
        Debug.Log("MAP_KEYBOARD_PASS: M opens; Escape closes; game time restored.");
    }

    [UnityTest] public IEnumerator UnassignedToggleKeyStillAllowsButtons()
    {
        var keyboard = InputSystem.AddDevice<Keyboard>(); keyboard.MakeCurrent();
        hudObject = Object.Instantiate(Resources.Load<GameObject>("Prefabs/UI/MapMinimapHUD"));
        var hud = hudObject.GetComponent<MapMinimapHUD>(); hud.toggleKey = Key.None;
        Press(keyboard.mKey); yield return null; Release(keyboard.mKey);
        Assert.IsFalse(hud.IsOpen);
        hud.openButton.onClick.Invoke(); Assert.IsTrue(hud.IsOpen);
        hud.closeButton.onClick.Invoke(); Assert.IsFalse(hud.IsOpen);
        Debug.Log("MAP_UNASSIGNED_PASS: no keyboard binding; buttons still work without exceptions.");
    }

    [UnityTest] public IEnumerator MainHUDButtonIsVisibleAndMapOpensAfterExistingStart()
    {
        Time.timeScale = 1;
        yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
        yield return null;
        var start = Object.FindObjectsByType<Button>(FindObjectsSortMode.None)
            .First(b => b.GetComponentInChildren<Text>()?.text.Trim().ToUpperInvariant() == "START");
        float deadline = Time.realtimeSinceStartup + 15;
        while (!start.interactable && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.IsTrue(start.interactable);
        start.onClick.Invoke();
        var controller = GameObject.Find("Player").GetComponent<ObjectPlayerController2D>();
        deadline = Time.realtimeSinceStartup + 45;
        while ((!controller.enabled || GameObject.Find("Title_UI") != null) && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.IsTrue(controller.enabled);
        yield return null;
        var hud = Object.FindAnyObjectByType<MapMinimapHUD>(); Assert.IsNotNull(hud);
        Canvas.ForceUpdateCanvases();
        var root = hud.GetComponent<RectTransform>();
        Assert.AreEqual(Vector2.one, root.anchorMax, "Nested canvas lost its stretch override.");
        Assert.Greater(root.rect.width, 300); Assert.Greater(root.rect.height, 300);
        var corners = new Vector3[4]; hud.openButton.GetComponent<RectTransform>().GetWorldCorners(corners);
        Assert.Greater(corners[2].x, corners[0].x);
        Assert.That(corners[0].x, Is.InRange(0, Screen.width));
        Assert.That(corners[0].y, Is.InRange(0, Screen.height));
        Assert.That(corners[2].x, Is.InRange(0, Screen.width));
        Assert.That(corners[2].y, Is.InRange(0, Screen.height));
        hud.openButton.onClick.Invoke(); yield return null;
        Assert.IsTrue(hud.IsOpen); Assert.Greater(hud.VisibleRendererCount, 0);
        Assert.Greater(hud.mapImage.rectTransform.rect.width, 300);
        hud.closeButton.onClick.Invoke(); Assert.IsFalse(hud.IsOpen);
        Debug.Log("MAP_MAIN_HUD_PASS: original Main Start completed; nested HUD button is onscreen; actual map renders.");
    }
}
