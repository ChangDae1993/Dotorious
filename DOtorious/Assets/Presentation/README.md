# Presentation 자동 실행 구조

`Assets/Presentation` 폴더를 `.meta` 파일과 함께 다른 Unity 프로젝트의 `Assets` 아래로 복사하면 됩니다.
`Tools` 메뉴 실행이나 수동 씬 등록은 필요하지 않습니다.

## Play를 누르면 자동으로 하는 일

1. `AutoSetup`이 첫 Play 요청을 잠시 보류합니다.
2. 정확한 GUID의 `Presentation.unity`를 Build Settings에 활성 상태로 등록합니다.
3. Presentation 씬의 `EventPlayManager`를 확인하고, 비어 있는 프리팹 참조만 자동 연결합니다.
   이미 사용자가 연결한 참조는 덮어쓰지 않습니다.
4. 이전 형식 EventSO가 있으면 데이터를 백업한 뒤 직렬화 순환이 없는 형식으로 변환합니다.
5. 설정 성공 후 Unity가 자동으로 Play를 다시 시작합니다.
6. 런타임에서 `Presentation` 씬이 Additive로 로드됩니다.
7. 시작한 게임 씬 이름에 맞춰 모든 `Resources/Presentation/<씬 이름>` 폴더에서 `EventSO`를 읽습니다.
8. 해당 씬 폴더에서 실행 방식이 `Condition`인 EventSO만 자동 감시하고, 조건이 충족되면 한 번 실행합니다.
   `None`인 EventSO는 자동 실행하지 않으며 `EventPlayManager.PlayEvent(...)`로 호출해야 합니다.

## EventSO 위치와 실행 순서

씬 이름이 `Chapter1`이면 EventSO는 다음처럼 `Resources/Presentation/Chapter1` 아래에 둡니다.
프로젝트 안에 `Resources` 폴더가 여러 개여도 같은 상대 경로를 모두 읽습니다.

```text
Assets/Resources/Presentation/Chapter1/001_Opening.asset
Assets/Game/Resources/Presentation/Chapter1/002_Dialogue.asset
```

`Chapter1` 씬에서는 위 폴더만 읽고 다른 씬 폴더의 EventSO는 읽지 않습니다.
같은 프레임에 여러 Condition이 충족되면 씬 로드 순서와 이름 오름차순으로 실행하며,
앞 이벤트가 끝난 뒤 다음 이벤트를 실행합니다. 씬을 언로드하면 아직 시작하지 않은 해당 씬 이벤트는 제거하고,
같은 씬을 다시 로드하면 새 씬 실행으로 다시 준비합니다.

## EventSO 실행 규칙

- `None`: 자동 실행하지 않습니다. 수동 `PlayEvent` 호출 시 첫 번째 Event Step을 실행합니다.
- `Condition`: 위에서부터 처음 일치한 Condition과 같은 인덱스의 Event Step을 자동으로 한 번 실행합니다.
- `Condition Checks = Nothing`: 별도 조건 검사 없이 해당 Condition의 Event Step을 즉시 실행합니다.
- `Check Value`: 기존 Global/PlayerPrefs 값 비교 조건입니다. 한 Condition 안의 여러 값은 모두 만족해야 합니다.
- `Time Count`: 게임 시작 후 설정한 초가 지나면 참이 됩니다.
- `A ↔ B Collision`: 지정한 두 씬 오브젝트가 3D/2D Collision 또는 Trigger로 접촉하면 참이 됩니다.
  오브젝트는 이름 또는 `Root/Child` 경로로 지정합니다.
- `Check Value`, `Time Count`, `A ↔ B Collision`을 여러 개 선택하면 선택한 조건을 모두 만족해야 합니다.
- 어떤 Condition도 일치하지 않으면 Condition 개수 다음 인덱스의 else Step을 실행합니다.
  단, else Step은 수동 `PlayEvent` 호출에서만 사용하며 자동 조건 감시로는 실행하지 않습니다.
- `Next Phase`는 현재 Step의 `EventExe.ConditionSteps`에 저장되며 현재 Phase 완료 후 순서대로 실행됩니다.
- SoftSpeech, HardSpeech, Choice처럼 완료를 기다리는 연출은 완료된 뒤 다음 EventSO로 넘어갑니다.
- `Tooltip`: `MemoCanvas`와 같은 Canvas/Image/Text/IEventUI 구조로 검은 배경과 텍스트를 표시합니다.
  - `Is Relative`가 꺼져 있으면 `Position`은 화면 중앙 `(0,0)` 기준의 Canvas X/Y 절대 좌표입니다.
  - `Is Relative`가 켜져 있으면 `Center Object` 위치에 `Right×X + Up×Y` 월드 오프셋을 더하고, 표시 중 매 프레임 실제 카메라의 UI 좌표로 변환합니다.
  - `Is Blocked`가 꺼져 있으면 `Duration` 후 자동으로 닫히고 다음 단계로 진행합니다.
  - `Is Blocked`가 켜져 있으면 키보드 키 또는 마우스 버튼 입력이 들어올 때 닫히고 다음 단계로 진행합니다.

## 빌드

Play를 먼저 누르지 않고 바로 빌드해도 동일한 AutoSetup 검사가 빌드 전에 자동 실행됩니다.
설정에 실패하면 잘못된 빌드를 만들지 않고 원인을 Console에 표시합니다.
