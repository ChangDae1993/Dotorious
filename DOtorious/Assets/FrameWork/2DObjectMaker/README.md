# 2DObjectMaker

`Assets/FrameWork` 폴더를 `.meta` 파일과 함께 다른 Unity 프로젝트의 `Assets` 아래로 복사하면 됩니다.
별도 매니저 씬, Build Settings 등록, 프로젝트 전용 namespace 연결은 필요하지 않습니다.

## 전용 창

1. `Window > FrameWork > 2DObjectMaker`를 엽니다.
2. 맨 위에서 `Enemy` 또는 `Player`를 고르고 이름과 기본 스탯을 정합니다.
3. Enemy는 `AI 규칙 프로그램`에 필요한 만큼 `~할 때` 규칙 카드를 추가하고, Player는 이동/점프 키와 공격 카드를 추가합니다.
4. Player 공격마다 키, 데미지, 준비/판정/회수/재사용 시간, 범위와 콤보 수를 정합니다.
5. 필요하면 `멀티씬 자동 배치`에 씬 이름과 위치를 추가합니다.
6. `Make (Definition + Prefab + Animator)`를 누르면 프로젝트의 `Assets/Resources` 아래에 종류별 에셋이 함께 생성됩니다.

```text
Assets/Resources/2DObjectMaker/Definitions/<이름>.asset
Assets/Resources/Prefabs/Enemy/<이름>.prefab  (플레이어는 Player/)
Assets/Resources/2DObjectMaker/Animators/<이름>.controller
```

이미지와 Animator를 넣는 입력 칸은 없습니다. 생성된 Prefab을 열어 `Visual` 자식의
`Sprite Renderer > Sprite`에 이미지를 넣고, 생성된 Animator Controller의 State에 모션만 넣으면 됩니다.

같은 이름이 이미 있어도 덮어쓰지 않고 고유한 경로를 만듭니다. 생성된 Prefab은 자신을 만든
`ObjectDefinitionSO`를 `ObjectActor2D`에 가지고 있습니다. 창 위의 드롭 영역에 생성 Prefab 또는
Definition SO를 놓으면 설정 전체를 읽고 버튼이 `Modify`로 바뀝니다. Modify는 같은 SO, 같은 Prefab,
같은 Animator를 다시 정의하며 Prefab에서 바꾼 Sprite·자식 시각물과 State에 넣은 모션을 유지합니다.

## Player 제작

Player를 선택하면 다음 항목을 제작 창에서 정할 수 있습니다.

- 왼쪽/오른쪽 이동 키, 점프 키, 점프 힘, 지면 레이어와 GroundProbe 반경
- 공격 중 이동 허용 여부
- 제한 없이 추가하는 공격 카드와 공격별 입력 키
- 공격별 데미지, 공격 전 딜레이, 판정 지속시간, 공격 후 딜레이, 재사용 대기시간
- 공격 거리/높이, 넉백, 대상 피격 무적 시간
- 1~12단 콤보, 다음 입력 가능시간과 콤보 초기화 시간

생성 Prefab에는 `ObjectPlayerController2D`가 자동 연결됩니다. 새 Input System의 지정 키를 읽고,
Rigidbody2D 이동과 GroundProbe 점프를 실행하며 공격 판정도 생성합니다. 점프 State는 점프가 시작될 때
한 번만 재생되고 착지 전 매 프레임 다시 시작하지 않습니다.

`Key.None`이 아닌 공격 수와 각 공격의 콤보 수만큼 `Player_Attack_01_Combo_01` 형식의 State와
전이가 자동 생성됩니다. 예를 들어 키가 배정된 공격 2개가 각각 3단/2단 콤보라면 공격 State 5개가
만들어집니다. 이동 키가 모두 `None`이면 Run, 점프 키가 `None`이면 Jump 노드와 관련 변수를 만들지
않습니다.

## `~할 때 → 이 행동` 규칙 엔진

규칙 카드는 위에서 아래 순서로 검사하며, 처음 조건이 맞은 카드가 실행됩니다. 조건과 행동은
서로 고정된 꾸러미가 아닙니다. 모든 카드에서 같은 공용 AI 목록을 사용하므로 예를 들어
`피격했을 때 → 정지`, `저체력일 때 → 정지`처럼 동일한 행동을 여러 상황에 재사용할 수 있습니다.

- 조건 23종: 생성, 항상, 타겟 유무, 감지/첫 발견/놓침, 거리, 공격 거리, 피격, 저체력,
  시간 반복, 스폰 거리, 장애물, 낭떠러지, 행동 종료, 파괴, 동료 경보, 접촉, 확률 등
- 공용 행동 28종: 정지, 대기, Patrol, 배회, 경계, Follow, 마지막 위치 수색, 거리 유지,
  바라보기, 도주, 귀환, 방향 전환, 점프, 부유, Attack, 돌진, 사격, 범위 공격,
  동료 경보, 순간이동, 자폭, 파괴, 모션 재생 등
- Patrol을 고르면 이동/정지 시간, 방향 결정 방식, 속도, 작은 턱과 낭떠러지 대응만 열립니다.
- Follow를 고르면 추적 방식, 속도, 기억 시간, 스폰 기준 최대 거리와 포기 거리만 열립니다.
- Attack을 고르면 근접/강공격/3연격/돌진/도약/투사체/충격파/자폭/저격과 해당 판정 값만 열립니다.
- 각 규칙에는 26종의 절차형 연출을 연결할 수 있습니다. 외부 이펙트 프리팹 없이 경보,
  스캔, 타겟 락, 베기, 충격파, 스파크, 불/얼음/어둠, 포털, 픽셀 분해 등을 생성합니다.

`+ ~할 때 규칙 추가`로 카드 수를 제한 없이 늘리고, ▲/▼로 우선순위를 바꿉니다.

## SG-001 기본 규칙 예시

SG-001은 전용 AI 꾸러미가 아니라 다음 공용 규칙 6개를 조합한 예시입니다.

- HP 0 도달 → 행동 없음 + 픽셀 분해 연출 (본체는 연출과 별개로 즉시 제거)
- 피격했을 때 → 피해를 준 대상 Follow
- 공격 거리 안일 때 → Attack
- 플레이어를 놓쳤을 때 → 마지막 위치 수색 후 귀환
- 플레이어를 감지했을 때 → Follow
- 타겟이 없을 때 → Patrol

기본 수치는 아래와 같습니다.

- 기본 근접형 적 / Act 1 초반
- 체력 1, 공격력 1, 방어력 0, 이동 속도 0.5
- 이동 1.5~3초, 정지 0.8~1.8초, 이동 시작 시 확률 방향 전환
- 전방 5m, 후방 1.5m, 수직 2m 감지
- 최소 추격 1초, 감지 기억 3초, 최초 위치 기준 최대 8m, 대상과 7m 이상이면 포기
- 추격 속도 1.4배, 작은 턱 자동 오르기, 높은 단차와 낭떠러지 회피
- 공격 거리 1.2m, 준비 0.5초, 판정 0.15초, 회수 0.7초, 재공격 대기 0.8초
- 접촉 피해 1, 접촉 넉백 0.2m, 대상 피격 무적 1초
- 피격 시 현재 행동 중단, 로컬 히트스톱 0.05초, 점멸
- 체력 0이면 충돌을 즉시 끄고 Death 모션 및 선택한 파괴 이펙트 실행

## 상황별 모션

2DObjectMaker가 현재 키 매핑과 활성 AI 규칙에 필요한 State/전이만 들어간 Animator Controller를
자동 생성하고 강제로 연결합니다. Modify 때 생성기 소유 노드와 변수를 다시 정리하므로 삭제한 공격이나
비활성 규칙의 노드가 남지 않습니다. 사용자는 State 이름이나 연결 문자열을 설정하지 않고 각 State의
`Motion`만 교체합니다.

- `Idle`: 대기·경계
- `Run`: 이동 키가 있는 Player 이동
- `Jump`: 점프 키가 있는 Player 점프
- `Hit`: 피격
- `Dead`: 파괴

Enemy는 `~할 때` 규칙 카드마다 `Enemy_Rule_01_Patrol` 형식의 State가 생성되고 Any State에서
`EnemyRule` 값으로 연결됩니다. Attack/Charge/Shoot 같은 공격 행동은 준비·판정·회수 State 3개가
`EnemyAttackPhase` 값으로 차례로 이어집니다. Player는 공격 카드/콤보 단계마다 별도 State가
`IsAttacking`, `PlayerAttack`, `PlayerCombo` 값으로 이어집니다.

생성 Controller가 필요에 따라 내보내는 공용 변수는 `MoveSpeed`, `IsGrounded`, `IsAttacking`,
`PlayerAttack`, `PlayerCombo`, `EnemyRule`, `EnemyAttackPhase`, `Jump`, `Hit`, `Dead`입니다.
런타임 제어용 공개 함수는 `ObjectActor2D.SetLocomotionAnimation`,
`SetPlayerAttackAnimation`, `SetEnemyRuleAnimation`이며 Player/Enemy 컨트롤러가 자동으로 호출합니다.
Animation Event를 따로 연결할 필요가 없습니다.

Animator Controller를 Prefab에서 임의로 바꿔도 런타임에는 Definition에 연결된 2DObjectMaker 전용
Controller가 다시 적용됩니다. 생성된 Controller 안의 Motion을 바꾸는 방식으로 사용합니다.

## 멀티씬 자동

런타임 부트스트랩이 현재 로드된 씬과 이후 Additive로 로드되는 씬을 모두 자동 등록합니다.
각 SO의 씬 배치 규칙과 씬 이름이 일치하면 연결된 프리팹을 해당 씬에 생성합니다.
씬을 언로드했다가 다시 로드하면 그 씬의 규칙도 새로 실행됩니다.

자동 배치를 사용하지 않는 프리팹은 일반 Unity 프리팹처럼 씬에 직접 배치하면 됩니다.
여러 씬의 생성 플레이어는 공용 Registry에 등록되므로 몬스터가 Additive 씬 구성에서도 가장 가까운
플레이어를 감지할 수 있습니다.

## JSON 가져오기 / 내보내기

창 오른쪽 위 `Export JSON`은 현재 편집 중인 설정을 UTF-8 JSON으로 저장합니다.
`Import JSON`은 설정을 편집 화면에 읽어오며, 파일을 읽는 것만으로 기존 SO나 프리팹을 변경하지 않습니다.
신규 설정은 `Make`, 기존 Definition을 연 상태는 `Modify`로 저장합니다. 가져오기 실패/취소 시 편집 내용은 유지됩니다.

JSON은 기존 ObjectDefinitionData 필드 구조를 `data`에 그대로 담습니다. 이미지·소리·Animator 등의
Unity 에셋 참조는 `assetReferences`의 GUID/localId와 경로로 복원합니다. JSON에 이미지·소리 파일 자체를
포함하지는 않으므로 다른 프로젝트로 옮길 때 해당 에셋과 `.meta`도 함께 복사하세요. 누락된 참조는 경고로 표시됩니다.

## 폴더 구조

```text
Assets/FrameWork/2DObjectMaker/  배포용 프레임워크
Assets/Resources/              현재 프로젝트에서 만든 결과물
  2DObjectMaker/
    Animators/                 공용 State 구조의 오브젝트별 Controller
    Definitions/               멀티씬 자동 배치용 Definition
    JSON/                      JSON 예제 저장 위치
  Prefabs/
    Enemy/                     적 Prefab
    Player/                    플레이어 Prefab
```

기존 Definition을 Modify하면 연결된 Prefab/Animator의 현재 경로를 유지합니다.
이전 Assets/2DObjectMaker 경로의 Animator도 계속 수정할 수 있습니다.
