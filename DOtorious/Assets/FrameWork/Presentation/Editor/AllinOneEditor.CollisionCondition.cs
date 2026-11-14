using System;
using JYW.Game.EventPlay;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

public sealed partial class AllinOneEditor
{
    [SerializeField] private BoxCollider2D conditionRegion;
    [SerializeField] private Collider2D conditionActor;
    [SerializeField] private int conditionBranch;

    private void DrawCollisionStartCondition()
    {
        var section = SimpleSection("3  Collider 시작 조건");
        section.name = "collision-condition-placement";
        section.Add(Text("직접 이벤트를 호출하지 않습니다. 선택한 분기의 Condition에 실제 A↔B 현재 2D 접촉 조건을 저장합니다.", "aio-simple-hint"));
        var region = new ObjectField("시작 BoxCollider2D") { objectType = typeof(BoxCollider2D), allowSceneObjects = true, value = conditionRegion, name = "condition-start-region" };
        var actor = new ObjectField("플레이어 Collider2D") { objectType = typeof(Collider2D), allowSceneObjects = true, value = conditionActor, name = "condition-start-actor" };
        var branch = new IntegerField("조건 분기 (1부터)") { value = conditionBranch + 1, name = "condition-start-branch" };
        var note = Text("", "aio-simple-notice"); note.name = "condition-start-notice";
        Action describe = () =>
        {
            var binding = conditionRegion != null ? conditionRegion.GetComponent<EventConditionRegion2D>() : null;
            note.text = binding == null ? "시작 구역과 플레이어를 선택하세요." : "구역의 연결: " + (binding.Event != null ? binding.Event.name : "없음") + " · 조건 " + (binding.ConditionIndex + 1);
        };
        region.RegisterValueChangedCallback(e =>
        {
            conditionRegion = e.newValue as BoxCollider2D ?? (e.newValue as GameObject)?.GetComponent<BoxCollider2D>();
            region.SetValueWithoutNotify(conditionRegion);
            var binding = conditionRegion != null ? conditionRegion.GetComponent<EventConditionRegion2D>() : null;
            if (binding != null) { conditionActor = binding.Actor; actor.SetValueWithoutNotify(conditionActor); conditionBranch = binding.ConditionIndex; branch.SetValueWithoutNotify(conditionBranch + 1); }
            describe();
        });
        actor.RegisterValueChangedCallback(e => { conditionActor = e.newValue as Collider2D ?? (e.newValue as GameObject)?.GetComponent<Collider2D>(); actor.SetValueWithoutNotify(conditionActor); });
        branch.RegisterValueChangedCallback(e => { conditionBranch = Mathf.Max(0, e.newValue - 1); branch.SetValueWithoutNotify(conditionBranch + 1); });
        section.Add(region);
        section.Add(ActionButton("Hierarchy 선택을 시작 구역으로", () => region.value = Selection.activeGameObject != null ? Selection.activeGameObject.GetComponent<BoxCollider2D>() : null));
        section.Add(actor);
        section.Add(ActionButton("Hierarchy 선택을 플레이어로", () => actor.value = Selection.activeGameObject != null ? Selection.activeGameObject.GetComponent<Collider2D>() : null));
        section.Add(branch);
        var buttons = new VisualElement(); buttons.AddToClassList("aio-simple-links"); section.Add(buttons);
        Action<bool> apply = create =>
        {
            try
            {
                if (!create && conditionRegion != null)
                {
                    var prior = conditionRegion.GetComponent<EventConditionRegion2D>();
                    if (prior != null && (prior.Event != currentEventSO || prior.Actor != conditionActor || prior.ConditionIndex != conditionBranch) &&
                        !EditorUtility.DisplayDialog("시작 조건 연결 변경", "선택한 구역의 EventSO/플레이어 연결과 선택 분기의 충돌 A/B 조건을 바꿉니다. 다른 조건과 동작은 유지합니다.", "연결 변경", "취소")) return;
                }
                EventConditionRegion2D result;
                if (create)
                {
                    Vector3 p = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero; p.z = 0;
                    result = EventConditionRegion2DPlacement.Create(currentEventSO, conditionActor, p, new Vector2(2, 3), conditionBranch);
                    conditionRegion = result.GetComponent<BoxCollider2D>(); region.SetValueWithoutNotify(conditionRegion);
                }
                else result = EventConditionRegion2DPlacement.Assign(currentEventSO, conditionRegion, conditionActor, conditionBranch);
                overview?.InvalidateSources();
                note.EnableInClassList("is-error", false);
                note.text = "Condition 저장: " + currentEventSO.Conditions[conditionBranch].CollisionObjectA + " ↔ " + currentEventSO.Conditions[conditionBranch].CollisionObjectB + " · 현재 2D 접촉. EventSO와 씬을 저장하세요.";
                if (!AssetDatabase.GetAssetPath(currentEventSO).Replace('\\', '/').Contains("/Resources/Presentation/" + conditionRegion.gameObject.scene.name + "/"))
                    note.text += " 자동 감시하려면 이 씬 이름의 Resources/Presentation 폴더에 EventSO가 있어야 합니다.";
                EditorGUIUtility.PingObject(result);
            }
            catch (Exception ex) { note.EnableInClassList("is-error", true); note.text = ex.Message; }
        };
        var connect = ActionButton("선택 분기에 접촉 조건 연결", () => apply(false)); connect.name = "condition-start-assign"; buttons.Add(connect);
        var make = ActionButton("새 시작 구역 + 접촉 조건", () => apply(true)); make.name = "condition-start-create"; buttons.Add(make);
        section.Add(note); describe();
    }
}
