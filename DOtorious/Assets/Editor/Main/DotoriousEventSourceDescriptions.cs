using Dotorious.UI;
using Dotorious.Winter;
using UnityEditor;
using UnityEngine;

// Descriptions mirror these game's verified entry points; this adapter never executes an event.
[InitializeOnLoad]
internal static class DotoriousEventSourceDescriptions
{
    static DotoriousEventSourceDescriptions()
    {
        PresentationEventSourceDescriptions.Register(typeof(TitleMunu).FullName, (source, path, asset) =>
            path == "startEvent" ? "START 버튼 클릭 → Presentation 준비 대기 → 시작 연출 실행. 연출 완료 후 타이틀을 닫고 플레이어 입력을 복구합니다." : null);
        PresentationEventSourceDescriptions.Register(typeof(WinterPresentationTrigger2D).FullName, (source, path, asset) =>
        {
            if (path != "presentation") return null;
            var trigger = (WinterPresentationTrigger2D)source;
            string condition = "게임 시작 후 플레이어가 2D 트리거에 진입하면 오브젝트당 1회 실행. 진행 중인 연출이 끝날 때까지 기다립니다.";
            if (trigger.stage == null) condition = "실행 불가: Stage가 연결되지 않았습니다. " + condition;
            if (trigger.checkpoint) condition += " 진입 시 체크포인트 ‘" + trigger.checkpointLabel + "’를 저장하고 체력을 회복합니다.";
            if (trigger.finish) condition += " 이 연출이 끝나면 완료 화면을 엽니다.";
            return condition + Region(trigger.GetComponent<BoxCollider2D>());
        });
        PresentationEventSourceDescriptions.Register(typeof(MainMapPortal2D).FullName, (source, path, asset) =>
        {
            if (path != "presentation") return null;
            var portal = (MainMapPortal2D)source;
            if (portal.nextMap == null)
                return "다음 맵이 없어 이 EventSO는 실행하지 않습니다." + (portal.finishAtLastMap ? " 플레이어 진입 시 스테이지를 완료합니다." : "");
            return "게임 시작 후 현재 맵이 ‘" + (portal.sourceMap != null ? portal.sourceMap.name : "미연결") +
                "’인 상태에서 살아 있는 현재 플레이어가 2D 포털에 진입/머무르면 실행합니다. 다른 연출 완료 후에도 영역 안에 있어야 합니다. " +
                "복제한 EventSO의 조건을 끄고 첫 경로를 실행하며, 이동 성공 시 ‘" + portal.nextMap.name + "’에 체크포인트를 등록합니다." +
                Region(portal.GetComponent<BoxCollider2D>());
        });
    }

    private static string Region(BoxCollider2D collider)
    {
        if (collider == null) return " 콜라이더가 없습니다.";
        Bounds b = collider.bounds;
        return $" 영역: X {b.min.x:0.##}~{b.max.x:0.##}, Y {b.min.y:0.##}~{b.max.y:0.##}.";
    }
}
