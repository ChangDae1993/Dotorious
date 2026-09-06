using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using JYW.Game.EventPlay;
using JYW.Game.ObjectMaker;
using JYW.Game.ObjectMaker.Editor;
using Dotorious.Winter;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public static class WinterStageBuilder
{
    const string Root = "Assets/Resources";
    const string Art = Root + "/Art/Main";
    const string Animations = Root + "/2DObjectMaker/Animations";
    const string Audio = Root + "/Audio/Main";
    const string Events = Root + "/EventSO/Main";
    const string Json = ObjectMakerPaths.GeneratedJson;
    const string UiPrefabs = Root + "/Prefabs/UI";
    static readonly Color Ice = new Color(.5f,.85f,1f);
    static Font font;
    static Transform terrain, decor, enemies;
    static WinterStageRuntime stage;
    static Sprite groundSprite;
    static Sprite[] props, trees;
    static AudioClip attackSound, hitSound, alertSound, jumpSound;
    struct Segment
    {
        public float a,b,y;
        public Segment(float a,float b,float y){this.a=a;this.b=b;this.y=y;}
    }
    static readonly Segment[] Route = {
        new Segment(48,65,3.4f),new Segment(68,78,3.7f),new Segment(81,95,4.4f),
        new Segment(97.5f,112,3.4f),new Segment(115,121,4.4f),new Segment(124,133,5.4f),
        new Segment(136,151,4.4f),new Segment(151,174,3.4f),new Segment(177,187,4.7f),
        new Segment(190,201,3.5f),new Segment(204,213,4.2f),new Segment(216,225,5.6f),
        new Segment(228,240,4.4f),new Segment(240,261,3.4f),new Segment(264,274,4.6f),
        new Segment(277,286,3.8f),new Segment(286,312,3.4f)
    };

    public static void Validate()
    {
        var scene=EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        foreach(var root in scene.GetRootGameObjects())
            foreach(var t in root.GetComponentsInChildren<Transform>(true))
                if(t.GetComponents<Component>().Any(c=>c==null))throw new Exception("Missing Script: "+t.name);
        var stage=UnityEngine.Object.FindFirstObjectByType<WinterStageRuntime>();
        if(stage==null || stage.enemyRoot.GetComponentsInChildren<ObjectActor2D>().Length!=26)
            throw new Exception("WinterStage enemy links are incomplete.");
        if(stage.playerPrefab.GetComponent<CapsuleCollider2D>().size!=((CapsuleCollider2D)stage.player.BodyCollider).size)
            throw new Exception("Respawn collider differs from the Main player.");
        ObjectMakerJson.ImportFile(Json+"/WinterGuardian.AllOptions.json",out var warnings);
        if(warnings.Length!=0)throw new Exception(string.Join("\n",warnings));
        foreach(var actor in stage.enemyRoot.GetComponentsInChildren<ObjectActor2D>())
        {
            if(actor.Definition==null || actor.Animator.runtimeAnimatorController==null || actor.SpriteRenderer.sprite==null)
                throw new Exception("Missing enemy reference: "+actor.name);
            foreach(var clip in actor.Animator.runtimeAnimatorController.animationClips)
                foreach(var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                    if(AnimationUtility.GetObjectReferenceCurve(clip,binding).Any(k=>k.value==null))
                        throw new Exception("Missing animation frame: "+clip.name);
        }
        Debug.Log("[WinterStage] Validation passed: Main, 26 enemies, respawn collider, animations and JSON asset references.");
    }

    [MenuItem("Tools/Dotorious/Build Winter Path (Main)")]
    public static void Build()
    {
        ObjectMakerPaths.EnsureGeneratedStructure();
        Directory.CreateDirectory(Animations);
        Directory.CreateDirectory(Audio);
        Directory.CreateDirectory(UiPrefabs);
        Directory.CreateDirectory(Events);
        Directory.CreateDirectory(Json);
        AssetDatabase.Refresh();
        attackSound=Tone("IceSlash",.16f,620,140,.2f);
        hitSound=Tone("MetalHit",.14f,170,45,.25f);
        alertSound=Tone("SentinelAlert",.32f,520,840,.17f);
        jumpSound=Tone("Jump",.14f,200,660,.15f);
        var crawler=ImportGrid("CrawlerSheet",170);
        var hare=ImportGrid("HareSheet",160);
        var lantern=ImportGrid("LanternSheet",170);
        trees=ImportTrees();
        props=ImportProps();
        var crawlerDef=MakeEnemy("WinterCrawler","서리 순찰병",crawler,0);
        var hareDef=MakeEnemy("WinterHare","설원 돌진병",hare,1);
        var lanternDef=MakeEnemy("WinterLantern","등불 감시자",lantern,2);
        var guardianDef=MakeEnemy("WinterGuardian","얼어붙은 문지기",crawler,3);
        var hero=MakeHero();

        var scene=EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        var roots=scene.GetRootGameObjects();
        var old=roots.FirstOrDefault(x=>x.name=="WinterStage");
        if(old!=null) UnityEngine.Object.DestroyImmediate(old);
        var all=roots.Where(x=>x!=null).SelectMany(x=>x.GetComponentsInChildren<Transform>(true)).ToArray();
        var player=all.Select(x=>x.GetComponent<ObjectActor2D>()).First(x=>x!=null && x.Kind==ObjectKind.Player);
        foreach(var t in all)
            if(t.name=="Stage1_Portal1" || (t.name=="SG-001" && t.position.y>0)) t.gameObject.SetActive(false);
        var originalGround=all.First(x=>x.name=="Ground" && x.position.y>0).GetComponent<SpriteRenderer>();
        groundSprite=originalGround.sprite;
        var background=all.First(x=>x.name=="Background_0").GetComponent<SpriteRenderer>();
        var camera=all.Select(x=>x.GetComponent<Camera>()).First(x=>x!=null && x.CompareTag("MainCamera"));
        var titleUi=all.First(x=>x.name=="TitleUI").gameObject;
        font=titleUi.GetComponentsInChildren<Text>(true).Select(x=>x.font).FirstOrDefault(x=>x!=null);
        if(font==null) font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        player.Configure(hero,player.Body,player.BodyCollider,player.SpriteRenderer.transform,player.SpriteRenderer,player.Animator);
        player.RefreshFromDefinition(true);
        PrefabUtility.RecordPrefabInstancePropertyModifications(player);
        player.GetComponent<ObjectPlayerController2D>().Configure(player,player.Body,player.BodyCollider,null);
        player.Body.gravityScale=1;
        PrefabUtility.RecordPrefabInstancePropertyModifications(player.Body);
        // The scene player has authored collider/visual overrides. Respawns must preserve those too.
        var respawnTemplate=UnityEngine.Object.Instantiate(player.gameObject);
        respawnTemplate.name="WinterPlayer";
        foreach(var attachedCamera in respawnTemplate.GetComponentsInChildren<Camera>(true))
            UnityEngine.Object.DestroyImmediate(attachedCamera.gameObject);
        PrefabUtility.SaveAsPrefabAsset(respawnTemplate,AssetDatabase.GetAssetPath(hero.GeneratedPrefab));
        UnityEngine.Object.DestroyImmediate(respawnTemplate);
        var root=new GameObject("WinterStage");
        stage=root.AddComponent<WinterStageRuntime>();
        stage.player=player;stage.playerPrefab=hero.GeneratedPrefab;stage.gameplayCamera=camera;stage.titleUi=titleUi;
        terrain=Child(root.transform,"01_SnowTerrain");
        decor=Child(root.transform,"02_ForestDetails");
        enemies=Child(root.transform,"03_Enemies");
        stage.enemyRoot=enemies;
        var distant=Child(root.transform,"00_DistantForest");
        var parallax=distant.gameObject.AddComponent<WinterParallax>();
        // The original intro tile stays in Stage1. Keep the continuation aligned with its fixed edge.
        parallax.cameraTransform=camera.transform;parallax.horizontalFactor=0;
        float backgroundWidth=background.bounds.size.x;
        for(int i=1;i<=8;i++)
        {
            var go=new GameObject("WinterForest_"+i);go.transform.SetParent(distant);
            go.transform.position=background.transform.position+Vector3.right*(backgroundWidth*i-.08f*i);
            go.transform.localScale=background.transform.lossyScale;
            var sr=go.AddComponent<SpriteRenderer>();sr.sprite=background.sprite;sr.sortingOrder=-100;
            sr.flipX=(i%2)==1;
        }
        foreach(var s in Route) Ground(s);
        Platform(57,6.2f,4,0);Platform(88,7.4f,5,0);Platform(106,6.1f,4,1);
        Platform(157,6.4f,4.5f,0);Platform(167,7.8f,4,0);Platform(196,6.5f,4,0);
        Platform(235,7.4f,5,1);Platform(253,6.2f,4,0);Platform(292,6.4f,4,0);
        for(int i=0;i<45;i++)
        {
            float x=12+i*6.65f;
            float y=Floor(x);
            var tree=Visual(decor,"SnowTree_"+i,trees[i%trees.Length],new Vector3(x,y-.15f,4),9f+(i%4)*1.4f,-25);
            tree.color=new Color(.38f+(i%3)*.07f,.53f+(i%3)*.07f,.65f+(i%3)*.07f,1);
            tree.flipX=i%2==0;
        }
        // Ground details are collision-free, so silhouettes never interfere with the route.
        for(int i=0;i<22;i++)
        {
            float x=40+i*11.8f;
            Visual(decor,"SnowRuin_"+i,props[0],new Vector3(x,Floor(x)-.15f,2),.7f+(i%3)*.25f,-3);
        }
        Visual(decor,"LastLightCabin",props[2],new Vector3(302,3.35f,3),5.5f,-8);
        Visual(decor,"FrozenGate",props[3],new Vector3(286,3.35f,3),6.5f,-9);
        var end=new GameObject("EndBoundary");end.transform.SetParent(terrain);end.transform.position=new Vector3(313,9,0);
        end.layer=15;end.AddComponent<BoxCollider2D>().size=new Vector2(1,16);

        float[] crawlerX={23,39,73,91,106,143,160,181,209,232,258,281};
        foreach(float x in crawlerX) Spawn(crawlerDef,x,Floor(x));
        float[] hareX={53,86,118,147,172,198,222,270};
        foreach(float x in hareX) Spawn(hareDef,x,Floor(x));
        foreach(float x in new[]{100f,153f,194f,243f,276f}) Spawn(lanternDef,x,Floor(x)+2.2f);
        Spawn(guardianDef,291,3.4f);

        BuildHud();
        Trigger(12,3.4f,"01_Controls","오른쪽으로 가 보자.\n← → 이동 · Shift 달리기 · Space 점프 · A 공격",false);
        Trigger(60,3.4f,"02_FirstLight","첫 번째 불빛이다. 여기서 다시 일어설 수 있겠어.",true);
        Trigger(128,5.4f,"03_BrokenBridge","끊어진 다리… 달리면서 건너자.\n낭떠러지에 떨어져도 마지막 불빛에서 다시 시작한다.",true);
        Trigger(164,3.4f,"04_Ruins","등불의 푸른 빛을 조심하자.\n공격 직후가 기회야.",true);
        Trigger(247,3.4f,"05_Ridge","저 너머에 마지막 불빛이 보여.\n조금만 더 가면 돼.",true);
        Trigger(300,3.4f,"06_Arrival","도착했어. 오늘 밤은 여기서 쉬자.",true,true);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        WriteExamples(guardianDef);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[WinterStage] Built: 300m, 26 enemies, 17 ground islands, 9 optional platforms, 6 Presentation events.");
    }

    static Transform Child(Transform parent,string name)
    { var t=new GameObject(name).transform;t.SetParent(parent,false);return t; }
    static float Floor(float x)
    {foreach(var s in Route)if(x>=s.a&&x<=s.b)return s.y;return 3.4f;}
    static SpriteRenderer Visual(Transform parent,string name,Sprite sprite,Vector3 position,float height,int order)
    {
        var t=Child(parent,name);t.position=position;
        t.localScale=Vector3.one*(height/sprite.bounds.size.y);
        var sr=t.gameObject.AddComponent<SpriteRenderer>();sr.sprite=sprite;sr.sortingOrder=order;return sr;
    }
    static void Ground(Segment s)
    {
        var t=Child(terrain,"Snow_"+s.a+"_"+s.b);t.position=new Vector3((s.a+s.b)/2,s.y-.86f,0);t.gameObject.layer=15;
        var sr=t.gameObject.AddComponent<SpriteRenderer>();sr.sprite=groundSprite;sr.drawMode=SpriteDrawMode.Tiled;
        sr.size=new Vector2(s.b-s.a,1.72f);sr.sortingOrder=1;
        // Sprite pivot is not centered in the source; collider top is specified in world coordinates.
        var box=t.gameObject.AddComponent<BoxCollider2D>();box.size=new Vector2(s.b-s.a,1.2f);box.offset=new Vector2(0,.26f);
        sr.transform.position=new Vector3((s.a+s.b)/2,s.y-.86f-groundSprite.bounds.center.y,0);
        box.offset=new Vector2(0,s.y-t.position.y-.6f);
    }
    static void Platform(float x,float top,float width,int index)
    {
        var sr=Visual(terrain,"JumpPlatform_"+x,props[index],new Vector3(x,top-1.1f,0),1.25f,2);
        sr.transform.localScale=new Vector3(width/props[index].bounds.size.x,1.25f/props[index].bounds.size.y,1);
        sr.gameObject.layer=15;
        var box=sr.gameObject.AddComponent<BoxCollider2D>();
        box.size=new Vector2(props[index].bounds.size.x,.35f/sr.transform.localScale.y);
        box.offset=new Vector2(0,(top-sr.transform.position.y-.175f)/sr.transform.localScale.y);
        box.usedByEffector=true;
        var effector=sr.gameObject.AddComponent<PlatformEffector2D>();effector.useOneWay=true;effector.useColliderMask=false;effector.surfaceArc=170;
    }
    static void Spawn(ObjectDefinitionSO definition,float x,float floor)
    {
        var go=(GameObject)PrefabUtility.InstantiatePrefab(definition.GeneratedPrefab);
        go.transform.SetParent(enemies,false);
        var col=go.GetComponent<CapsuleCollider2D>();
        go.transform.position=new Vector3(x,floor+col.size.y*.5f+.05f,0);
        go.name=definition.Data.displayName+"_"+x;
    }

    static ObjectDefinitionSO MakeEnemy(string id,string name,Sprite[] frames,int type)
    {
        var existing=AssetDatabase.LoadAssetAtPath<ObjectDefinitionSO>(ObjectMakerPaths.GeneratedDefinitions+"/"+id+".asset");
        var d=new ObjectDefinitionData{objectId=id,displayName=id,role=name,firstAppearance="Main · Winter Path"};
        d.kind=ObjectKind.Monster;d.core.maxHealth=type==3?10:type==1?3:2;
        d.core.attackPower=1;d.core.moveSpeed=type==1?1.35f:type==2?.7f:.65f;
        d.movement.groundWalker=type!=2;d.movement.groundMask=1<<15;
        d.destruction.destroyDelaySeconds=0;d.attack.contactDamage=1;d.hit.sound.audioClip=hitSound;
        d.destruction.sound.audioClip=hitSound;
        d.ai.useLegacyTuning=false;d.ai.rules.Clear();
        var death=Rule("HP 0 · 즉시 파괴",ObjectAICondition.HealthReached,ObjectAIAction.DestroySelf);
        death.condition.healthValue=0;death.effect=ObjectRuleEffect.IceShards;death.effectSettings.primaryColor=Ice;d.ai.rules.Add(death);
        var hit=Rule("피격 · 잠깐 멈춤",ObjectAICondition.Damaged,ObjectAIAction.Wait);
        hit.settings.durationSeconds=new Vector2(.16f,.16f);hit.settings.motion=ObjectMotionCondition.Hit;hit.effect=ObjectRuleEffect.SparkBurst;d.ai.rules.Add(hit);
        var attack=Rule(type==2?"사거리 · 서리 탄환":"근접 · 공격",ObjectAICondition.TargetWithinAttackRange,type==2?ObjectAIAction.Shoot:ObjectAIAction.Attack);
        attack.condition.distance=type==2?7:1.65f;attack.settings.attackRange=attack.condition.distance;
        attack.settings.attackPattern=type==2?ObjectAttackPattern.SingleProjectile:type==1?ObjectAttackPattern.Charge:type==3?ObjectAttackPattern.HeavySmash:ObjectAttackPattern.ShortMelee;
        attack.settings.damage=type==3?2:1;attack.settings.prepareSeconds=type==3?.75f:.55f;
        attack.settings.activeSeconds=.2f;attack.settings.recoverySeconds=.7f;attack.settings.cooldownSeconds=type==2?1.8f:1.1f;
        attack.settings.hitboxHeight=2.8f;attack.settings.knockback=.35f;attack.settings.projectileSpeed=4;
        attack.settings.projectileLifetime=2.4f;attack.settings.targetInvulnerabilitySeconds=1;
        attack.effect=type==2?ObjectRuleEffect.MuzzleFlash:ObjectRuleEffect.SlashArc;
        attack.actionSound.audioClip=attackSound;attack.effectSettings.primaryColor=Ice;d.ai.rules.Add(attack);
        var alert=Rule("처음 발견 · 경계",ObjectAICondition.TargetEnteredDetection,ObjectAIAction.FaceTarget);
        alert.settings.durationSeconds=new Vector2(.3f,.3f);alert.effect=ObjectRuleEffect.AlertMark;
        alert.conditionSound.audioClip=alertSound;d.ai.rules.Add(alert);
        var follow=Rule(type==2?"발견 · 거리 유지":"발견 · 추격",ObjectAICondition.TargetDetected,type==2?ObjectAIAction.KeepDistance:ObjectAIAction.FollowTarget);
        follow.settings.followPattern=type==1?ObjectAIFollowPattern.BurstSprint:ObjectAIFollowPattern.Direct;
        follow.settings.speedMultiplier=type==1?1.9f:1.5f;follow.settings.distance=type==2?4:1.5f;follow.settings.maxDistanceFromSpawn=9;
        follow.settings.giveUpTargetDistance=9;d.ai.rules.Add(follow);
        var lost=Rule("놓침 · 돌아가기",ObjectAICondition.TargetLost,ObjectAIAction.SearchLastSeenThenReturn);d.ai.rules.Add(lost);
        var patrol=Rule("평상시 · 순찰",ObjectAICondition.NoTarget,type==2?ObjectAIAction.Hover:ObjectAIAction.Patrol);
        patrol.settings.patrolPattern=type==1?ObjectAIPatrolPattern.BurstAndPause:ObjectAIPatrolPattern.ChanceTurn;
        patrol.settings.moveSeconds=new Vector2(1.5f,3);patrol.settings.durationSeconds=new Vector2(.8f,1.8f);
        patrol.settings.turnChance=.45f;d.ai.rules.Add(patrol);
        foreach(var r in d.ai.rules)
        {
            r.condition.frontDistance=type==2?8:6;r.condition.rearDistance=2;r.condition.verticalRange=type==2?5:2.7f;
            r.settings.groundMovement=type!=2;r.settings.groundMask=1<<15;
            r.settings.cliffResponse=ObjectAITerrainResponse.TurnAround;r.settings.obstacleResponse=ObjectAITerrainResponse.TurnAround;
        }
        var result=ObjectMakerPrefabBuilder.Make(d,existing);
        var clips=new Dictionary<string,AnimationClip>{
            {"Idle",Clip(id,"Idle",frames.Take(4).ToArray(),5,true)},
            {"Walk",Clip(id,"Walk",frames.Skip(4).Take(4).ToArray(),8,true)},
            {"AttackPrepare",Clip(id,"Prepare",new[]{frames[8]},3,false)},
            {"Attack",Clip(id,"Attack",frames.Skip(9).Take(3).ToArray(),12,false)},
            {"AttackRecover",Clip(id,"Recover",new[]{frames[11],frames[0]},5,false)},
            {"Hit",Clip(id,"Hit",frames.Skip(12).Take(2).ToArray(),10,false)},
            {"Dead",Clip(id,"Dead",frames.Skip(14).Take(2).ToArray(),7,false)}
        };
        foreach(var s in result.AnimatorController.layers[0].stateMachine.states)
        {
            var key=s.state.name.Contains("AttackPrepare")?"AttackPrepare":s.state.name.Contains("AttackRecover")?"AttackRecover":
                s.state.name.Contains("Attack")||s.state.name.Contains("Shoot")?"Attack":
                s.state.name=="Dead"?"Dead":s.state.name=="Hit"||s.state.name.Contains("_Wait")?"Hit":
                s.state.name.Contains("Patrol")||s.state.name.Contains("Follow")||s.state.name.Contains("Search")||s.state.name.Contains("Hover")?"Walk":"Idle";
            s.state.motion=clips[key];
        }
        EditorUtility.SetDirty(result.AnimatorController);
        var root=PrefabUtility.LoadPrefabContents(result.PrefabPath);
        var actor=root.GetComponent<ObjectActor2D>();
        var collider=root.GetComponent<CapsuleCollider2D>();
        float height=type==3?2.8f:type==1?1.65f:1.7f;
        collider.size=new Vector2(type==3?2.5f:1.5f,height);collider.offset=Vector2.zero;
        actor.SpriteRenderer.sprite=frames[0];actor.SpriteRenderer.sortingOrder=4;
        actor.SpriteRenderer.transform.localScale=Vector3.one*(type==3?1.55f:1);
        actor.SpriteRenderer.transform.localPosition=new Vector3(0,-height/2,0);
        PrefabUtility.SaveAsPrefabAsset(root,result.PrefabPath);PrefabUtility.UnloadPrefabContents(root);
        return result.Definition;
    }
    static ObjectAIRule Rule(string label,ObjectAICondition when,ObjectAIAction action)
    {return new ObjectAIRule{label=label,when=when,action=action};}

    static ObjectDefinitionSO MakeHero()
    {
        string path=ObjectMakerPaths.GeneratedDefinitions+"/WinterPlayer.asset";
        var source=AssetDatabase.LoadAssetAtPath<ObjectDefinitionSO>(ObjectMakerPaths.GeneratedDefinitions+"/Player.asset");
        var existing=AssetDatabase.LoadAssetAtPath<ObjectDefinitionSO>(path);
        if(existing==null)
        {
            existing=ScriptableObject.CreateInstance<ObjectDefinitionSO>();
            var data=source.Data.Clone();data.displayName="WinterPlayer";data.objectId="WinterPlayer";
            string prefab=ObjectMakerPaths.GeneratedPlayerPrefabs+"/WinterPlayer.prefab";
            string controller=ObjectMakerPaths.GeneratedAnimators+"/WinterPlayer.controller";
            AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(source.GeneratedPrefab),prefab);
            AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(source.Data.animatorController),controller);
            data.animatorController=AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(controller);
            existing.ReplaceData(data);existing.SetGeneratedPrefab(AssetDatabase.LoadAssetAtPath<GameObject>(prefab));
            AssetDatabase.CreateAsset(existing,path);
        }
        var d=existing.Data.Clone();d.core.maxHealth=12;d.core.moveSpeed=4;
        d.player.moveLeftKey=Key.LeftArrow;d.player.moveRightKey=Key.RightArrow;d.player.runKey=Key.LeftShift;d.player.jumpKey=Key.Space;
        d.player.jumpForce=7.5f;d.player.groundMask=1<<15;d.player.runSpeedMultiplier=1.5f;
        d.player.jumpSound.audioClip=jumpSound;d.player.jumpSound.volume=.3f;d.hit.sound.audioClip=hitSound;d.hit.sound.volume=.4f;
        d.destruction.destroyDelaySeconds=0;
        d.player.attacks[0].key=Key.A;d.player.attacks[0].damage=1;d.player.attacks[0].range=2.5f;d.player.attacks[0].hitboxHeight=3.4f;
        d.player.attacks[0].sound.audioClip=attackSound;d.player.attacks[0].sound.volume=.3f;
        d.player.attacks[0].cooldownSeconds=.35f;
        return ObjectMakerPrefabBuilder.Make(d,existing).Definition;
    }

    static AnimationClip Clip(string id,string name,Sprite[] frames,float fps,bool loop)
    {
        string path=Animations+"/"+id+"_"+name+".anim";
        var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if(clip==null){clip=new AnimationClip();AssetDatabase.CreateAsset(clip,path);}
        clip.frameRate=fps;
        var keys=frames.Select((s,i)=>new ObjectReferenceKeyframe{time=i/fps,value=s}).ToList();
        keys.Add(new ObjectReferenceKeyframe{time=frames.Length/fps,value=frames[frames.Length-1]});
        AnimationUtility.SetObjectReferenceCurve(clip,EditorCurveBinding.PPtrCurve("",typeof(SpriteRenderer),"m_Sprite"),keys.ToArray());
        var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=loop;AnimationUtility.SetAnimationClipSettings(clip,settings);
        EditorUtility.SetDirty(clip);return clip;
    }
    static Sprite[] ImportGrid(string name,float ppu)
    {
        string path=Art+"/"+name+".png";
        var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        var rects=new List<SpriteMetaData>();
        for(int row=0;row<4;row++)for(int col=0;col<4;col++)
        {
            int x=col*texture.width/4, right=(col+1)*texture.width/4;
            int bottom=texture.height-(row+1)*texture.height/4,top=texture.height-row*texture.height/4;
            rects.Add(new SpriteMetaData{name=name+"_"+(row*4+col).ToString("00"),rect=new Rect(x,bottom,right-x,top-bottom),alignment=9,pivot=new Vector2(.5f,.07f)});
        }
        return Import(path,rects.ToArray(),ppu);
    }
    static Sprite[] ImportTrees()
    {
        string path=Art+"/ForestTrees.png";
        var t=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        var list=new List<SpriteMetaData>();
        float[] edges={0,.3f,.747f,1};
        for(int i=0;i<3;i++)list.Add(new SpriteMetaData{name="Tree_"+i,rect=new Rect(Mathf.Floor(t.width*edges[i]),0,Mathf.Floor(t.width*(edges[i+1]-edges[i])),t.height),alignment=9,pivot=new Vector2(.5f,0)});
        return Import(path,list.ToArray(),100);
    }
    static Sprite[] ImportProps()
    {
        string path=Art+"/StageProps.png";
        // Atlas uses clear independent regions. Bottom-aligned pivots keep terrain placement predictable.
        return Import(path,new[]{
            Meta("0_Ledge",new Rect(0,790,626,464)),Meta("1_Bridge",new Rect(627,790,627,464)),
            Meta("2_Cabin",new Rect(0,40,627,720)),Meta("3_Gate",new Rect(627,40,627,720))},100);
    }
    static SpriteMetaData Meta(string name,Rect rect)
    {return new SpriteMetaData{name=name,rect=rect,alignment=9,pivot=new Vector2(.5f,.02f)};}
#pragma warning disable CS0618
    static Sprite[] Import(string path,SpriteMetaData[] rects,float ppu)
    {
        var importer=(TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Multiple;
        importer.spritePixelsPerUnit=ppu;importer.spritePivot=new Vector2(.5f,0);
        importer.alphaIsTransparency=true;importer.mipmapEnabled=false;importer.filterMode=FilterMode.Point;
        importer.textureCompression=TextureImporterCompression.Uncompressed;importer.maxTextureSize=2048;
        importer.isReadable=true;importer.SaveAndReimport();
        var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);var pixels=texture.GetPixels32();
        for(int i=0;i<rects.Length;i++)
        {
            Rect original=rects[i].rect;int left=(int)original.xMax,right=(int)original.xMin,bottom=(int)original.yMax,top=(int)original.yMin;
            for(int y=(int)original.yMin;y<(int)original.yMax;y++)for(int x=(int)original.xMin;x<(int)original.xMax;x++)
                if(pixels[y*texture.width+x].a>30){left=Math.Min(left,x);right=Math.Max(right,x);bottom=Math.Min(bottom,y);top=Math.Max(top,y);}
            if(right>left&&top>bottom)
            {
                rects[i].rect=new Rect(left,bottom,right-left+1,top-bottom+1);
                float center=path.Contains("Sheet")?original.center.x:(left+right)*.5f;
                rects[i].pivot=new Vector2((center-left)/(right-left+1),0);
            }
        }
        var factories=new SpriteDataProviderFactories();factories.Init();
        var provider=factories.GetSpriteEditorDataProviderFromObject(importer);provider.InitSpriteEditorDataProvider();
        var oldRects=provider.GetSpriteRects();
        var spriteRects=rects.Select(r=>new SpriteRect{name=r.name,rect=r.rect,pivot=r.pivot,alignment=SpriteAlignment.Custom,
            spriteID=oldRects.FirstOrDefault(o=>o.name==r.name)?.spriteID ?? GUID.Generate()}).ToArray();
        provider.SetSpriteRects(spriteRects);
        provider.GetDataProvider<ISpriteNameFileIdDataProvider>().SetNameFileIdPairs(spriteRects.Select(r=>new SpriteNameFileIdPair(r.name,r.spriteID)));
        provider.Apply();importer.SaveAndReimport();
        return AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().OrderBy(x=>x.name).ToArray();
    }
#pragma warning restore CS0618
    static AudioClip Tone(string name,float seconds,float from,float to,float volume)
    {
        string path=Audio+"/"+name+".wav";
        if(!File.Exists(path))
        {
            const int rate=22050;int count=(int)(rate*seconds);
            using(var writer=new BinaryWriter(File.Create(path)))
            {
                writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));writer.Write(36+count*2);writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
                writer.Write(16);writer.Write((short)1);writer.Write((short)1);writer.Write(rate);writer.Write(rate*2);writer.Write((short)2);writer.Write((short)16);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));writer.Write(count*2);
                double phase=0;
                for(int i=0;i<count;i++)
                {
                    float t=(float)i/count;phase+=2*Math.PI*Mathf.Lerp(from,to,t)/rate;
                    double wave=Math.Sin(phase)+.2*Math.Sin(phase*2.03);
                    writer.Write((short)(wave*volume*32767*Math.Sin(Math.PI*t)*(1-t)));
                }
            }
            AssetDatabase.ImportAsset(path);
        }
        return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
    }
    static void Trigger(float x,float floor,string name,string text,bool checkpoint,bool finish=false)
    {
        string path=Events+"/"+name+".asset";
        var so=AssetDatabase.LoadAssetAtPath<EventSO>(path);
        if(so==null){so=ScriptableObject.CreateInstance<EventSO>();AssetDatabase.CreateAsset(so,path);}
        so.UseCondition=false;
        so.ConditionSteps=new[]{new EventSO.EventStep{Flags=new EventSO.EventStepFlags{IsSpeeches=true},
            Speeches=new EventSO.SpeechesData{IsSpeechBubble=true,SpeechBubble=new EventSO.SpeechBubbleData{
                GameObjectName="Player",isTyping=true,SpeechBubbleTexts=new List<EventSO.SpeechData>{new EventSO.SpeechData{Text=text,Duration=3.5f}}}}}};
        EditorUtility.SetDirty(so);
        var go=Child(stage.transform,"Event_"+name).gameObject;go.transform.position=new Vector3(x,floor+3,0);
        var collider=go.AddComponent<BoxCollider2D>();collider.isTrigger=true;collider.size=new Vector2(2,9);
        var trigger=go.AddComponent<WinterPresentationTrigger2D>();trigger.presentation=so;trigger.stage=stage;
        trigger.checkpoint=checkpoint;trigger.checkpointLabel=name;trigger.respawnPosition=new Vector3(x,floor+2.35f,0);trigger.finish=finish;
        if(checkpoint)Visual(decor,"Checkpoint_"+name,props[3],new Vector3(x,floor,3),3.5f,-7);
    }

    static RectTransform Rect(Transform parent,string name,Vector2 min,Vector2 max,Vector2 offset,Vector2 size)
    {
        var go=new GameObject(name,typeof(RectTransform));go.transform.SetParent(parent,false);
        var rt=(RectTransform)go.transform;rt.anchorMin=min;rt.anchorMax=max;rt.pivot=new Vector2(.5f,.5f);rt.anchoredPosition=offset;rt.sizeDelta=size;return rt;
    }
    static Text Label(Transform parent,string name,string content,int size,Vector2 min,Vector2 max,Vector2 offset,Vector2 dimensions,TextAnchor anchor)
    {
        var rt=Rect(parent,name,min,max,offset,dimensions);var text=rt.gameObject.AddComponent<Text>();
        text.font=font;text.fontSize=size;text.text=content;text.color=new Color(.85f,.93f,1);text.alignment=anchor;text.raycastTarget=false;
        return text;
    }
    static Image Fill(Transform parent,string name,Vector2 anchor,Vector2 pos,Vector2 size,Color color)
    {
        var back=Rect(parent,name,anchor,anchor,pos,size);back.gameObject.AddComponent<Image>().color=new Color(.04f,.08f,.12f,.85f);
        var front=Rect(back,"Fill",Vector2.zero,Vector2.one,Vector2.zero,new Vector2(-4,-4));var im=front.gameObject.AddComponent<Image>();
        im.sprite=AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        im.color=color;im.type=Image.Type.Filled;im.fillMethod=Image.FillMethod.Horizontal;return im;
    }
    static void BuildHud()
    {
        var go=new GameObject("WinterHUD",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));go.transform.SetParent(stage.transform,false);
        var canvas=go.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=80;
        var scaler=go.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=.5f;
        stage.hud=go;
        stage.healthText=Label(go.transform,"Health","HP 12 / 12",24,Vector2.up,Vector2.up,new Vector2(154,-43),new Vector2(244,34),TextAnchor.MiddleLeft);
        stage.healthFill=Fill(go.transform,"HealthBar",Vector2.up,new Vector2(152,-75),new Vector2(240,14),new Color(.4f,.85f,.9f));
        stage.areaText=Label(go.transform,"Area","WINTER PATH",24,new Vector2(.5f,1),new Vector2(.5f,1),new Vector2(0,-40),new Vector2(550,36),TextAnchor.MiddleCenter);
        stage.distanceText=Label(go.transform,"Distance","0 / 300 m",22,Vector2.one,Vector2.one,new Vector2(-157,-42),new Vector2(240,34),TextAnchor.MiddleRight);
        stage.progressFill=Fill(go.transform,"Progress",Vector2.one,new Vector2(-154,-75),new Vector2(240,10),Ice);
        Label(go.transform,"Controls","← → 이동    Shift 달리기    Space 점프    A 공격",21,Vector2.zero,Vector2.zero,new Vector2(327,30),new Vector2(600,34),TextAnchor.MiddleLeft).color=new Color(.8f,.9f,1,.8f);
        var panel=Rect(go.transform,"FinishPanel",Vector2.zero,Vector2.one,Vector2.zero,Vector2.zero);
        panel.gameObject.AddComponent<Image>().color=new Color(.025f,.06f,.09f,.91f);stage.finishPanel=panel.gameObject;
        stage.finishText=Label(panel,"Message","WINTER PATH",40,new Vector2(.5f,.5f),new Vector2(.5f,.5f),new Vector2(0,60),new Vector2(950,270),TextAnchor.MiddleCenter);
        var button=Rect(panel,"Restart",new Vector2(.5f,.5f),new Vector2(.5f,.5f),new Vector2(0,-140),new Vector2(270,65));
        button.gameObject.AddComponent<Image>().color=new Color(.14f,.28f,.35f);stage.restartButton=button.gameObject.AddComponent<Button>();
        Label(button,"Label","처음부터 다시",25,Vector2.zero,Vector2.one,Vector2.zero,Vector2.zero,TextAnchor.MiddleCenter);
        stage.finishPanel.SetActive(false);
        PrefabUtility.SaveAsPrefabAsset(go,UiPrefabs+"/WinterHUD.prefab");
    }
    static void WriteExamples(ObjectDefinitionSO guardian)
    {
        ObjectMakerJson.ExportFile(Json+"/WinterGuardian.json",guardian.Data);
        var all=guardian.Data.Clone();
        // Disabled catalogue rows demonstrate every selectable option without changing the active enemy.
        var conditions=Enum.GetValues(typeof(ObjectAICondition)).Cast<ObjectAICondition>().Distinct().ToArray();
        var actions=Enum.GetValues(typeof(ObjectAIAction)).Cast<ObjectAIAction>().ToArray();
        var effects=Enum.GetValues(typeof(ObjectRuleEffect)).Cast<ObjectRuleEffect>().ToArray();
        var patrols=Enum.GetValues(typeof(ObjectAIPatrolPattern)).Cast<ObjectAIPatrolPattern>().ToArray();
        var follows=Enum.GetValues(typeof(ObjectAIFollowPattern)).Cast<ObjectAIFollowPattern>().ToArray();
        var shapes=Enum.GetValues(typeof(ObjectAIDetectionShape)).Cast<ObjectAIDetectionShape>().ToArray();
        var attacks=Enum.GetValues(typeof(ObjectAttackPattern)).Cast<ObjectAttackPattern>().ToArray();
        var terrains=Enum.GetValues(typeof(ObjectAITerrainResponse)).Cast<ObjectAITerrainResponse>().ToArray();
        var motions=Enum.GetValues(typeof(ObjectMotionCondition)).Cast<ObjectMotionCondition>().ToArray();
        for(int i=0;i<actions.Length;i++)
        {
            var r=Rule("EXAMPLE (disabled) "+conditions[i%conditions.Length]+" → "+actions[i],conditions[i%conditions.Length],actions[i]);
            r.enabled=false;r.effect=effects[i%effects.Length];r.settings.patrolPattern=patrols[i%patrols.Length];r.settings.followPattern=follows[i%follows.Length];
            r.condition.detectionShape=shapes[i%shapes.Length];r.settings.attackPattern=attacks[i%attacks.Length];r.settings.obstacleResponse=terrains[i%terrains.Length];
            r.settings.cliffResponse=terrains[(i+1)%terrains.Length];r.settings.motion=motions[i%motions.Length];
            r.conditionSound.audioClip=alertSound;r.actionSound.audioClip=attackSound;all.ai.rules.Add(r);
        }
        all.previewSprite=guardian.GeneratedPrefab.GetComponentInChildren<SpriteRenderer>().sprite;
        all.player.attacks=new List<ObjectPlayerAttack>{
            new ObjectPlayerAttack{displayName="A · 3단 콤보 예시",key=Key.A,comboSteps=3,sound=new ObjectSoundCue2D{audioClip=attackSound}},
            new ObjectPlayerAttack{displayName="S · 강공격 예시",key=Key.S,damage=3,inputDelaySeconds=.5f,activeSeconds=.3f,cooldownSeconds=1.2f}};
        all.player.walkSound.audioClip=hitSound;all.player.runSound.audioClip=hitSound;all.player.jumpSound.audioClip=jumpSound;
        all.sceneSpawns.Add(new ObjectSceneSpawnRule{enabled=false,sceneName="Main",count=2,spacing=new Vector3(3,0,0)});
        ObjectMakerJson.ExportFile(Json+"/WinterGuardian.AllOptions.json",all);
    }
}
