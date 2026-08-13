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
7. 프로젝트의 모든 `Resources` 폴더에서 `EventSO`를 읽어 이름 오름차순으로 하나씩 실행합니다.
   앞 EventSO가 끝나야 다음 EventSO가 시작됩니다.

## EventSO 위치와 실행 순서

EventSO는 다음처럼 프로젝트 안의 어느 `Resources` 폴더에 두어도 됩니다.

```text
Assets/Resources/001_Opening.asset
Assets/Game/Resources/002_Dialogue.asset
Assets/Chapter1/Resources/003_End.asset
```

권장 이름은 `001_`, `002_`, `003_`처럼 순서 접두사를 붙이는 방식입니다.
`Resources.LoadAll<EventSO>("")`로 전부 읽으며 문자열 이름 오름차순으로 실행합니다.

## EventSO 실행 규칙

- `UseCondition`이 꺼져 있으면 첫 번째 `ConditionSteps`를 실행합니다.
- `UseCondition`이 켜져 있으면 위에서부터 처음 일치한 Condition과 같은 인덱스의 Step을 실행합니다.
- 어떤 Condition도 일치하지 않으면 Condition 개수 다음 인덱스의 else Step을 실행합니다.
- `Next Phase`는 현재 Step의 `EventExe.ConditionSteps`에 저장되며 현재 Phase 완료 후 순서대로 실행됩니다.
- SoftSpeech, HardSpeech, Choice처럼 완료를 기다리는 연출은 완료된 뒤 다음 EventSO로 넘어갑니다.

## 빌드

Play를 먼저 누르지 않고 바로 빌드해도 동일한 AutoSetup 검사가 빌드 전에 자동 실행됩니다.
설정에 실패하면 잘못된 빌드를 만들지 않고 원인을 Console에 표시합니다.
