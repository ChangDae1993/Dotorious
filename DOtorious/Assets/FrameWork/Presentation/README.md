# Presentation 자동 실행 구조

`Assets/FrameWork` 폴더를 `.meta` 파일과 함께 다른 Unity 프로젝트의 `Assets` 아래로 복사하면 됩니다.
`Tools` 메뉴 실행이나 수동 씬 등록은 필요하지 않습니다.

## EventSO 전용 편집 창

- 기존 Inspector 편집은 그대로 사용할 수 있습니다.
- `Window > FrameWork > Presentation`에서 전용 창을 엽니다.
- Project 창에서 EventSO를 더블클릭해도 전용 창이 열립니다.
- `Follow Selection`이 켜져 있으면 Project 창에서 선택한 EventSO를 자동으로 감지해 표시합니다.
- 창 위쪽의 EventSO 필드에 에셋을 드래그하거나 `Open File`로 직접 선택할 수도 있습니다.
- 전용 창은 기존 `EventSOEditor`를 그대로 사용하므로 Inspector와 기능 및 저장 형식이 동일하며, 별도의 데이터 복사본을 만들지 않습니다.

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
- 자동 `Condition` EventSO의 Caller는 Presentation 자동 감시 오브젝트입니다. `Empty = Caller` 또는 `Empty = Caller's Scene`인 대상형 기능을 자동 EventSO에서 사용할 때는 오브젝트 이름·`Root/Child` 경로·씬 이름을 직접 지정합니다.
- `Condition Checks = Nothing`: 별도 조건 검사 없이 해당 Condition의 Event Step을 즉시 실행합니다.
- `Check Value`: 기존 Global/PlayerPrefs 값을 같음, 다름, `>`, `≥`, `<`, `≤`, 홀수, 짝수로 비교합니다. 크기 비교는 Int/Float 전용이며 한 Condition 안의 여러 값은 모두 만족해야 합니다.
- `Time Count`: 게임 시작 후 설정한 초가 지나면 참이 됩니다.
- `A ↔ B Collision`: 지정한 두 씬 오브젝트가 3D/2D Collision 또는 Trigger로 접촉하면 참이 됩니다.
  오브젝트는 이름 또는 `Root/Child` 경로로 지정합니다.
  필요한 Collider가 없으면 Renderer 크기에 맞는 런타임 Trigger Collider를 대상에 추가하고, 충돌 감지용 kinematic Rigidbody는 숨김 자식 probe에만 둡니다.
  양쪽에 기존 static Collider만 있거나 기존 Rigidbody의 충돌 감지가 꺼진 경우에도 기존 구성요소는 바꾸지 않고, 기존 Collider별 보조 Trigger와 숨김 kinematic 감지 probe를 런타임에 한쪽만 추가합니다. 3D Box/Sphere/Capsule/convex Mesh와 2D Box/Circle/Capsule/Polygon/Edge는 원래 형태를 복제하고 그 밖의 Collider만 개별 bounds를 사용하므로 서로 떨어진 자식 Collider 사이의 빈 공간을 하나의 큰 충돌 영역으로 만들지 않습니다.
  기존 Collider와 Rigidbody는 변경하지 않으며, 자동 생성 구성요소는 씬이나 프리팹에 저장하지 않습니다.
- `Object State`: 이름 또는 `Root/Child` 경로의 오브젝트가 존재/없음/활성/비활성 상태인지 확인합니다.
- `Distance`: 두 오브젝트 사이 거리가 설정값 이하 또는 이상인지 확인합니다. `Use 2D`를 켜면 X/Y만, 끄면 X/Y/Z를 사용합니다.
- `Input Key`: 지정 키를 누른 프레임 또는 누르고 있는 동안 참이 됩니다. 키보드와 `Mouse0~Mouse4`를 새 Input System과 Legacy Input Manager에서 동일하게 지원합니다.
- `Animator State`: 지정 오브젝트 또는 자식 Animator가 특정 State를 현재 재생 중인지, 또는 설정한 normalizedTime 이상 재생을 마쳤는지 확인합니다.
- `Scene State`: 지정 씬이 로드됨/언로드됨/Active Scene 상태인지 확인합니다.
- `Camera View`: 지정 오브젝트가 현재 활성 Game Camera 화면 안/밖에 있는지 확인합니다. 대상 Layer를 렌더링하는 카메라 중 Depth가 가장 높은 카메라를 사용하고, Renderer가 있으면 Bounds와 Frustum을 검사합니다. 오브젝트나 카메라를 찾지 못한 상태는 `Not Visible`로 간주하지 않아 미완성 설정이 자동 발화하지 않습니다.
- Condition Checks를 여러 개 선택하면 선택한 조건을 모두 만족해야 합니다.
- 어떤 Condition도 일치하지 않으면 Condition 개수 다음 인덱스의 else Step을 실행합니다.
  단, else Step은 수동 `PlayEvent` 호출에서만 사용하며 자동 조건 감시로는 실행하지 않습니다.
- `Next Phase`는 현재 Step의 `EventExe.ConditionSteps`에 저장되며 현재 Phase 완료 후 순서대로 실행됩니다.
- `Wait Until Condition`: 한 Phase 안에서 동일한 Condition Checks를 계속 평가하고, 충족될 때까지 다음 Phase 진행을 막습니다.
  `Timeout = 0`은 제한 없이 기다리고, 양수는 해당 unscaled seconds가 지나면 경고 후 진행합니다.
- SoftSpeech, HardSpeech, Speech Bubble, Choice처럼 완료를 기다리는 연출은 완료된 뒤 다음 EventSO로 넘어갑니다.
- `Sound`: 기본값은 기존처럼 재생 시작 후 진행합니다. 비루프 Sound에서 `Wait For Completion`을 켜면 Start Delay와 클립 재생이 끝날 때까지 현재 Phase를 유지합니다.
  Loop Sound는 완료 시점이 없으므로 이 옵션을 적용하지 않습니다.
- `Tooltip`: `MemoCanvas`와 같은 Canvas/Image/Text/IEventUI 구조로 검은 배경과 텍스트를 표시합니다.
  - `Is Relative`가 꺼져 있으면 `Position`은 화면 중앙 `(0,0)` 기준의 Canvas X/Y 절대 좌표입니다.
  - `Is Relative`가 켜져 있으면 `Center Object` 위치에 `Right×X + Up×Y` 월드 오프셋을 더하고, 표시 중 매 프레임 실제 카메라의 UI 좌표로 변환합니다.
  - `Is Blocked`가 꺼져 있으면 `Duration` 후 자동으로 닫히고 다음 단계로 진행합니다.
  - `Is Blocked`가 켜져 있으면 키보드 키 또는 마우스 버튼 입력이 들어올 때 닫히고 다음 단계로 진행합니다.
- `Black Label`: `BlackLabelCanvas` 프리팹의 위·아래 검은 바를 `Duration` 동안 표시합니다.
  표시가 끝날 때까지 현재 Phase를 유지하며, 프리팹의 바 크기·색상·레이아웃은 프로젝트에서 직접 꾸밀 수 있습니다.
- `Portrait Speech`: Speeches 그룹에서 Line별 초상화, 화자 이름, 대사를 표시합니다.
  - `Input` 모드는 지정 키로 진행하며, 타이핑 중 첫 입력은 문장 전체 표시, 다음 입력은 다음 Line 진행입니다.
  - `Timed` 모드는 각 Line의 `Duration`이 끝나면 자동 진행합니다.
  - 모든 Line이 끝날 때까지 현재 Phase를 유지합니다.
  - `PortraitSpeechCanvas` 프리팹의 배경, 초상화 크기, 글꼴, 위치는 프로젝트에서 직접 꾸밀 수 있습니다.
  - 같은 이름의 UI가 이미 생성되어 있으면 기존 인스턴스를 재사용하므로 Line이나 Phase마다 중복 생성하지 않습니다.
- `Speech Bubble`: Speeches 그룹에서 `GameObject Name`으로 지정한 오브젝트 위에 Line별 대사를 표시합니다.
  - 이름을 비우면 EventSO를 호출한 오브젝트를 사용하며, 이름 또는 `Root/Child` 경로를 입력할 수 있습니다.
  - 각 Line은 SoftSpeech와 같은 `Duration`, `Text` 구조를 사용하고 `Is Typing`을 지원합니다.
  - 전체 문장 길이를 먼저 측정해 말풍선 폭과 높이를 정하므로 타이핑 중 크기가 흔들리지 않습니다. 긴 문장은 최대 폭에서 자동 줄바꿈되어 아래로 늘어납니다.
  - 대상의 Renderer 또는 Collider 윗부분을 따라 매 프레임 이동하며, 화면 가장자리에서는 본체를 화면 안에 두고 꼬리가 대상을 가리킵니다.
  - 모든 Line이 끝날 때까지 현재 Phase를 유지합니다. 본체는 9-slice, 꼬리는 별도 고정 이미지이므로 `SpeechBubbleCanvas` 프리팹에서 모양을 교체해도 늘어짐을 제어할 수 있습니다.
- `Animator`: Components 그룹에서 Caller 또는 지정 오브젝트의 Animator State 재생, Cross Fade, Trigger/Bool/Int/Float Parameter, Speed를 제어합니다.
  자식 Animator까지 찾으며, `Duration` 동안 다음 Phase 진행을 막습니다. `Duration`은 애니메이션 클립 길이를 자동 추정하지 않습니다.
- `Visual Fade`: Components 그룹에서 Caller 또는 이름/경로로 지정한 오브젝트와 자식을 목표 Alpha로 전환합니다.
  CanvasGroup, CanvasGroup 밖의 UI Graphic, SpriteRenderer, `_BaseColor`/`_Color`를 가진 3D Renderer를 지원하며 Entry별 `Duration` 동안 unscaled time으로 보간하고 Phase 진행을 막습니다. 여러 Entry는 동시에 실행됩니다. 3D Material은 실제 투명 표현이 가능한 Transparent 계열 설정이어야 합니다.
- `Light Tween`: Components 그룹에서 지정 오브젝트와 자식의 Light 색·밝기·범위 중 필요한 항목만 목표값으로 전환합니다.
  Entry별 `Duration` 동안 unscaled time으로 보간하고 Phase 진행을 막으며, 여러 Entry는 동시에 실행됩니다.
- `Particle Event`: Components 그룹에서 지정 오브젝트와 자식의 ParticleSystem에 Play/Pause/Stop Emitting/Stop And Clear/Clear 명령을 적용합니다.
  `Duration`은 파티클 수명을 자동 추정하지 않고, 명령 적용 후 설정한 unscaled seconds만큼 Phase 진행을 막습니다.
- `Attach Object`: Transforms 그룹에서 기존 오브젝트를 지정 부모나 `Root/Child` 경로에 연결하고, Parent를 비우면 분리합니다.
  World 좌표 유지 여부와 연결 직후 적용할 로컬 위치·회전·스케일을 선택할 수 있으며, 자기 자신이나 자기 자식으로 연결하는 순환은 거부합니다.
- `Screen Shake`: Cameras 그룹에서 `Duration`만 설정하면 기본 세기로 Main Camera 화면을 흔듭니다.
  `Duration` 동안 Phase를 유지하고, 종료나 이벤트 취소 시 이 기능이 더한 오프셋만 제거합니다. 카메라 대상, 세기, 빈도, Fade Out은 `Advanced Settings`에서 선택적으로 조절하며 기존 `CameraShake` 데이터와 호환됩니다.
- `Camera Lens`: Cameras 그룹에서 Main Camera 또는 Presentation EventCamera의 줌을 전환합니다.
  Perspective Camera는 Target FOV, Orthographic Camera는 Target Size를 자동 선택하고, `Duration` 동안 unscaled time으로 보간하며 Phase 진행을 막습니다. EventCamera는 기존 인스턴스를 재사용하고 설정한 렌즈 값을 다음 연출에도 유지합니다.
- `Time Scale`: 히트스톱이나 슬로모션을 위해 `Target Scale`을 즉시 적용하고 `Duration` 동안 Phase를 유지합니다.
  `Restore After Duration`을 켜면 종료 또는 이벤트 취소 시 이전 Time Scale로 복구하고, 끄면 설정값을 유지합니다.
- `Screen Flash`: `ScreenFlashCanvas`의 전체 화면 Image에 지정 색을 적용하고 Fade In → 유지 `Duration` → Fade Out 순서로 재생합니다.
  전체 재생이 끝날 때까지 Phase를 유지하며, 프리팹의 정렬 순서·Image 재질·레이아웃은 프로젝트에서 직접 꾸밀 수 있습니다.

## 빌드

Play를 먼저 누르지 않고 바로 빌드해도 동일한 AutoSetup 검사가 빌드 전에 자동 실행됩니다.
설정에 실패하면 잘못된 빌드를 만들지 않고 원인을 Console에 표시합니다.
