using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Dotorious.Maps;
using JYW.Game.ObjectMaker;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public class MapIngredientsTests
{
    readonly List<GameObject> spawned = new List<GameObject>();
    readonly List<ScriptableObject> temporaryDefinitions = new List<ScriptableObject>();
    static readonly string[] themes = {"SpringMeadow","SpringBlossom","SummerForest","SummerCoast","AutumnAmber","AutumnCrimson","WinterSnow","WinterIce","SpringMossRuins","WinterStoneRuins"};
    static readonly string[] categories = {"Ground","Ramps","Stairs","Platforms","Bridges","Walls","Foreground","Background","FarBackground","Decorations"};

    [UnitySetUp] public IEnumerator IsolateFromTheMainRegressionScene()
    {
        Time.timeScale=1;
        var main=SceneManager.GetSceneByName("Main");
        if(main.IsValid() && main.isLoaded)
        {
            var isolated=SceneManager.CreateScene("IngredientTestIsolation"); SceneManager.SetActiveScene(isolated);
            yield return SceneManager.UnloadSceneAsync(main);
        }
        if(UnityEngine.Object.FindAnyObjectByType<AudioListener>()==null)
        {
            var listener=new GameObject("TestAudioListener",typeof(AudioListener)); spawned.Add(listener);
        }
    }

    string Path(MapIngredientKind kind,int version)
    {
        return "Prefabs/Maps/Ingeredients/" + categories[(int)kind] + "/" + kind + "_v" + version.ToString("00") + "_" + themes[version-1];
    }

    GameObject Spawn(MapIngredientKind kind,int version,Vector3 position)
    {
        var prefab=Resources.Load<GameObject>(Path(kind,version)); Assert.IsNotNull(prefab,Path(kind,version));
        var instance=UnityEngine.Object.Instantiate(prefab,position,Quaternion.identity); spawned.Add(instance); return instance;
    }

    ObjectActor2D Player(Vector3 position)
    {
        var go=UnityEngine.Object.Instantiate(Resources.Load<GameObject>("Prefabs/Player/AcornPlayer"),position,Quaternion.identity);
        spawned.Add(go); go.GetComponent<ObjectPlayerController2D>().enabled=false;
        return go.GetComponent<ObjectActor2D>();
    }

    void PutFeet(ObjectActor2D actor,float x,float y)
    {
        actor.Body.linearVelocity=Vector2.zero;
        actor.Body.position=new Vector2(x,actor.transform.position.y);
        Physics2D.SyncTransforms();
        actor.Body.position+=Vector2.up*(y+.012f-actor.BodyCollider.bounds.min.y);
        Physics2D.SyncTransforms();
    }

    [UnityTearDown] public IEnumerator Cleanup()
    {
        foreach(var go in spawned) if(go!=null) UnityEngine.Object.Destroy(go);
        foreach(var definition in temporaryDefinitions) if(definition!=null) UnityEngine.Object.Destroy(definition);
        temporaryDefinitions.Clear();
        spawned.Clear(); Time.timeScale=1; yield return null;
    }

    [Test] public void All100PrefabsHaveMatchingFamiliesAndBakedConnections()
    {
        int count=0;
        for(int version=1;version<=10;version++) foreach(MapIngredientKind kind in Enum.GetValues(typeof(MapIngredientKind)))
        {
            var prefab=Resources.Load<GameObject>(Path(kind,version)); Assert.IsNotNull(prefab,Path(kind,version));
            var info=prefab.GetComponent<MapIngredient2D>(); Assert.AreEqual(version,info.Version); Assert.AreEqual(kind,info.Kind);
            Assert.IsFalse(string.IsNullOrEmpty(info.Theme));
            foreach(var component in prefab.GetComponentsInChildren<Component>(true)) Assert.IsNotNull(component,"Missing script: "+prefab.name);
            var colliders=prefab.GetComponentsInChildren<Collider2D>();
            if(info.IsTerrain)
            {
                Assert.IsNotEmpty(colliders); Assert.AreEqual("Ground",prefab.tag);
                foreach(var collider in colliders)
                {
                    Assert.AreEqual(LayerMask.NameToLayer("Ground"),collider.gameObject.layer,prefab.name);
                    Assert.IsFalse(collider.isTrigger); Assert.IsNotNull(collider.sharedMaterial);
                }
                var mesh=prefab.GetComponentInChildren<MeshFilter>().sharedMesh; Assert.IsNotNull(mesh);
                foreach(var uv in mesh.uv)
                {
                    int i=version-1;
                    Assert.That(uv.x,Is.GreaterThan(i%5*.2f).And.LessThan((i%5+1)*.2f));
                    Assert.That(uv.y,Is.GreaterThan((1-i/5)*.5f).And.LessThan((2-i/5)*.5f));
                }
            }
            else Assert.IsEmpty(colliders,"Background must not block characters.");
            if(kind==MapIngredientKind.Background) Assert.AreEqual("Background2",prefab.tag);
            if(kind==MapIngredientKind.Foreground) Assert.AreEqual("Background1",prefab.tag);
            if(kind==MapIngredientKind.FarBackground) Assert.AreEqual("Background3",prefab.tag);
            count++;
        }
        Assert.AreEqual(100,count); Debug.Log("MAP_METADATA_PASS: 100 prefabs / ten matching seasonal UV families / baked terrain and depth tags.");
    }

    [UnityTest] public IEnumerator EveryTerrainFamilySupportsGroundedJump()
    {
        var actor=Player(new Vector3(1002,103,0)); var controller=actor.GetComponent<ObjectPlayerController2D>();
        for(int version=1;version<=10;version++)
            for(int index=0;index<6;index++)
            {
                var kind=(MapIngredientKind)index; var tile=Spawn(kind,version,new Vector3(1000,100,0));
                float height=kind==MapIngredientKind.Ramp?.5f:kind==MapIngredientKind.Stairs?.6f:kind==MapIngredientKind.Wall?4:0;
                PutFeet(actor,1002,100+height); yield return new WaitForFixedUpdate();
                Assert.IsTrue(controller.RefreshGrounded(),tile.name+" not recognized as ground.");
                Assert.IsTrue(controller.TryJump(),tile.name+" jump rejected.");
                Assert.Greater(actor.Body.linearVelocity.y,1,tile.name);
                float y=actor.transform.position.y; yield return new WaitForSeconds(.08f);
                Assert.Greater(actor.transform.position.y,y+.05f,tile.name);
                UnityEngine.Object.Destroy(tile); yield return null;
            }
        Debug.Log("MAP_JUMP_PASS: actual AcornPlayer grounded and jumped on all 60 terrain prefabs.");
    }

    [UnityTest] public IEnumerator OneWayPlatformAllowsAscentAndSupportsLanding()
    {
        Spawn(MapIngredientKind.Platform,7,new Vector3(1000,100,0));
        var actor=Player(new Vector3(1001.5f,98,0));
        float gravity=actor.Body.gravityScale; actor.Body.gravityScale=0;
        actor.Body.linearVelocity=Vector2.up*8;
        yield return new WaitForSeconds(.5f);
        Assert.Greater(actor.BodyCollider.bounds.min.y,100.1f,"One-way platform blocked ascent.");
        actor.Body.gravityScale=gravity; actor.Body.linearVelocity=Vector2.down*2;
        yield return new WaitForSeconds(.8f);
        Assert.That(actor.BodyCollider.bounds.min.y,Is.EqualTo(100).Within(.06f),"One-way platform failed landing.");
        Debug.Log("MAP_ONEWAY_PASS: passed upwards, landed on the top surface.");
    }

    [UnityTest] public IEnumerator EnemySeesEachFloorRampAndStairAsTraversableGround()
    {
        var prefab=Resources.Load<GameObject>("Prefabs/Enemy/WinterCrawler");
        var go=UnityEngine.Object.Instantiate(prefab,new Vector3(1000,105,0),Quaternion.identity); spawned.Add(go);
        var actor=go.GetComponent<ObjectActor2D>(); var brain=go.GetComponent<ObjectMonsterBrain2D>(); brain.enabled=false;
        actor.Body.gravityScale=0; actor.Body.interpolation=RigidbodyInterpolation2D.None;
        var parameters=new ObjectAIActionParameters {groundMovement=true,smallStepHeight=.35f,groundMask=1<<15,obstacleProbeDistance=.2f,cliffProbeDistance=.8f};
        var probe=typeof(ObjectMonsterBrain2D).GetMethod("ProbeMoveBlock",BindingFlags.Instance|BindingFlags.NonPublic);
        Assert.IsNotNull(probe);
        for(int version=1;version<=10;version++) foreach(var kind in new[]{MapIngredientKind.Ground,MapIngredientKind.Ramp,MapIngredientKind.Stairs})
        {
            var tile=Spawn(kind,version,new Vector3(1000,100,0));
            float y=kind==MapIngredientKind.Ramp?.5f:kind==MapIngredientKind.Stairs?.6f:0;
            PutFeet(actor,1002,100+y);
            Assert.AreEqual("None",probe.Invoke(brain,new object[]{1f,parameters,true}).ToString(),tile.name);
            UnityEngine.Object.Destroy(tile); yield return null;
        }
        // At a true edge the same existing AI still detects a cliff.
        Spawn(MapIngredientKind.Ground,7,new Vector3(1000,100,0)); PutFeet(actor,1003.9f,100);
        Assert.AreEqual("Cliff",probe.Invoke(brain,new object[]{1f,parameters,false}).ToString());
        Debug.Log("MAP_AI_PASS: existing enemy ground probes accept all seasonal floors/ramps/stairs and still detect cliffs.");
    }

    [UnityTest] public IEnumerator EnemyActuallyWalksOverConnectedRampAndStairs()
    {
        Spawn(MapIngredientKind.Ground,7,new Vector3(1000,100,0));
        Spawn(MapIngredientKind.Ramp,7,new Vector3(1004,100,0));
        Spawn(MapIngredientKind.Ground,7,new Vector3(1008,101,0));
        Spawn(MapIngredientKind.Stairs,7,new Vector3(1012,101,0));
        Spawn(MapIngredientKind.Ground,7,new Vector3(1016,102,0));
        var go=UnityEngine.Object.Instantiate(Resources.Load<GameObject>("Prefabs/Enemy/WinterCrawler"),new Vector3(1001,102,0),Quaternion.identity);
        spawned.Add(go); var actor=go.GetComponent<ObjectActor2D>();
        var definition=UnityEngine.Object.Instantiate(actor.Definition); temporaryDefinitions.Add(definition);
        var data=definition.Data.Clone(); data.facesRightByDefault=true; data.core.moveSpeed=2;
        data.ai.useLegacyTuning=false;
        data.ai.rules=new List<ObjectAIRule> {new ObjectAIRule {when=ObjectAICondition.NoTarget,action=ObjectAIAction.Patrol,
            settings=new ObjectAIActionParameters {patrolPattern=ObjectAIPatrolPattern.OneWay,moveSeconds=new Vector2(30,30),
                groundMask=1<<15,smallStepHeight=.35f,turnChance=0}}};
        definition.ReplaceData(data);
        actor.Configure(definition,actor.Body,actor.BodyCollider,actor.SpriteRenderer.transform,actor.SpriteRenderer,actor.Animator);
        actor.RefreshFromDefinition(true); PutFeet(actor,1001,100);
        float deadline=Time.realtimeSinceStartup+13;
        while(actor.transform.position.x<1017 && Time.realtimeSinceStartup<deadline) yield return null;
        var brain=actor.GetComponent<ObjectMonsterBrain2D>();
        Assert.GreaterOrEqual(actor.transform.position.x,1017,"Enemy blocked: "+brain.State+" at "+actor.transform.position+" velocity "+actor.Body.linearVelocity);
        Assert.That(actor.BodyCollider.bounds.min.y,Is.EqualTo(102).Within(.25f));
        Debug.Log("MAP_AI_WALK_PASS: real enemy Patrol crossed connected floor, rising ramp and five stairs without special terrain logic.");
    }

    [UnityTest] public IEnumerator PlayerActuallyWalksOverConnectedRampAndStairs()
    {
        Spawn(MapIngredientKind.Ground,7,new Vector3(1000,100,0));
        Spawn(MapIngredientKind.Ramp,7,new Vector3(1004,100,0));
        Spawn(MapIngredientKind.Ground,7,new Vector3(1008,101,0));
        Spawn(MapIngredientKind.Stairs,7,new Vector3(1012,101,0));
        Spawn(MapIngredientKind.Ground,7,new Vector3(1016,102,0));
        var actor=Player(new Vector3(1001,102,0)); PutFeet(actor,1001,100);
        var controller=actor.GetComponent<ObjectPlayerController2D>();
        float deadline=Time.realtimeSinceStartup+10;
        while(actor.transform.position.x<1017 && Time.realtimeSinceStartup<deadline)
        {
            controller.RefreshGrounded(); controller.SetMoveInput(1);
            yield return null;
        }
        Assert.GreaterOrEqual(actor.transform.position.x,1017,"Player blocked at "+actor.transform.position);
        Assert.That(actor.BodyCollider.bounds.min.y,Is.EqualTo(102).Within(.25f));
        Debug.Log("MAP_PLAYER_WALK_PASS: original player movement crossed rising ramp and stairs without jumping or player-code changes.");
    }

    [UnityTest] public IEnumerator MinimapAutoFitsPlacedMovedAndRemovedPartsAndExcludesOtherDepths()
    {
        var floor=Spawn(MapIngredientKind.Ground,7,new Vector3(1000,100,0));
        Spawn(MapIngredientKind.Background,7,new Vector3(1003,100,0));
        var near=Spawn(MapIngredientKind.Foreground,7,new Vector3(8000,8000,0));
        var far=Spawn(MapIngredientKind.FarBackground,7,new Vector3(-8000,-8000,0));
        Spawn(MapIngredientKind.Decoration,7,new Vector3(9000,9000,0));
        foreach(var r in near.GetComponentsInChildren<Renderer>()) Assert.IsFalse(MapMinimapHUD.IsMapRenderer(r));
        foreach(var r in far.GetComponentsInChildren<Renderer>()) Assert.IsFalse(MapMinimapHUD.IsMapRenderer(r));
        var go=UnityEngine.Object.Instantiate(Resources.Load<GameObject>("Prefabs/UI/MapMinimapHUD")); spawned.Add(go);
        var hud=go.GetComponent<MapMinimapHUD>(); Time.timeScale=.37f;
        hud.openButton.onClick.Invoke(); yield return null;
        Assert.IsTrue(hud.IsOpen); Assert.AreEqual(0,Time.timeScale); Assert.AreEqual(2,hud.VisibleRendererCount);
        Assert.Less(hud.MapBounds.size.x,20); Assert.Less(hud.MapBounds.size.y,20);
        float initial=hud.PreviewCamera.orthographicSize;
        floor.transform.position+=Vector3.right*100; Physics2D.SyncTransforms(); hud.RefreshMap();
        Assert.Greater(hud.MapBounds.size.x,95); Assert.Greater(hud.PreviewCamera.orthographicSize,initial);
        UnityEngine.Object.Destroy(floor); yield return null; hud.RefreshMap(); Assert.AreEqual(1,hud.VisibleRendererCount);
        hud.closeButton.onClick.Invoke(); Assert.IsFalse(hud.IsOpen); Assert.AreEqual(.37f,Time.timeScale); Assert.IsNull(hud.mapImage.texture);
        Debug.Log("MAP_MINIMAP_PASS: automatic fit/add/move/delete; Background1/3/decor excluded; pause restored.");
    }

    [UnityTest] public IEnumerator EmptyMinimapFailsGracefullyWithoutChangingPlayerEnabledState()
    {
        var player=Player(new Vector3(1000,100,0)); var controller=player.GetComponent<ObjectPlayerController2D>(); controller.enabled=true;
        var go=UnityEngine.Object.Instantiate(Resources.Load<GameObject>("Prefabs/UI/MapMinimapHUD")); spawned.Add(go);
        var hud=go.GetComponent<MapMinimapHUD>(); hud.Open(); yield return null;
        Assert.AreEqual(0,hud.VisibleRendererCount); Assert.IsTrue(controller.enabled);
        Assert.IsFalse(hud.PreviewCamera.enabled); Assert.IsNotEmpty(hud.statusText.text);
        hud.Close(); Assert.AreEqual(1,Time.timeScale); Assert.IsTrue(controller.enabled);
        Debug.Log("MAP_EMPTY_PASS: empty map handled, no controller disable/cancel side effects.");
    }
}
