# 도토리 플레이어

Main의 기존 Player와 카메라/TitleMenu 참조는 유지하고, AcornPlayer Definition과 모션을 연결합니다. 기존 Player/WinterPlayer 에셋은 남겨둡니다.

| 키 | 행동 |
| --- | --- |
| ← / → | 이동 및 방향 전환 |
| Shift + 방향키 | 달리기 |
| C | 점프 (점프 모션은 반복하지 않음) |
| Z | 빠른 약공격 |
| X | 느린 강공격 |
| A | 손에서 앞으로 자라는 갈색 나무, 여러 적 관통 |
| S | 회전하는 도토리 투사체 |
| D | 전방 돌진, 경로의 적마다 한 번 피해 |

## 수정

Window > FrameWork > 2DObjectMaker에 Definitions/AcornPlayer.asset 또는 프리팹을 드래그합니다. 공격 목록에서 키, 피해, 준비/판정/회복/재사용 시간과 공격 방식을 수정하고 Modify를 누릅니다. 기존 애니메이션 클립 연결은 유지됩니다.

- Melee: 기존 근접 공격.
- GrowingThrust: 공격 거리까지 판정이 자라며 여러 적을 공격합니다.
- Projectile: 속도, 수명, 반경, 중력을 사용하며 지형에서 사라집니다. 근접 판정을 동시에 만들지 않습니다.
- Dash: 지속시간 동안 지정 속도로 돌진합니다. 지형을 통과하지 않습니다.
- 연출 프리팹: SpriteRenderer + ObjectSkillVisual2D. frames에 순서대로 스프라이트를 넣습니다. 나무의 모든 프레임은 같은 사각형 크기와 왼쪽 기준 피벗을 사용합니다. 연출이 없어도 피해 판정은 동작합니다.

JSON/AcornPlayer.json에서 동일 설정을 가져오거나 내보낼 수 있습니다. 이미지/프리팹 에셋도 함께 복사해야 GUID 또는 동일 경로로 복구됩니다.

## 모션

Resources/Art/AcornPlayer/AcornMotions.png: 6열 × 10행, 총 60 프레임. 행 순서는 Idle, Walk, Run, Jump, Z, X, A, S, D, Hit/Dead입니다. Unity Sprite Editor에서 투명 여백을 제외한 Rect 및 캐릭터 몸통 중심 피벗을 설정합니다. 원본 PNG 픽셀은 바꾸지 않았습니다.

애니메이터: Resources/2DObjectMaker/Animators/AcornPlayer.controller. 대기/이동/달리기/점프/피격/사망 및 다섯 공격 노드를 공용 2DObjectMaker 빌더로 생성합니다. 공격 노드 선택은 IsAttacking, PlayerAttack(1~5), PlayerCombo(1부터), 이동은 MoveSpeed, IsRunning, IsGrounded, Jump를 사용합니다. AnimationEvent나 별도 키 입력 스크립트가 필요하지 않습니다.

## 맵 이동 로딩

EventSO의 MoveObject에서 `맵 이동 로딩`을 켜고 `로딩 화면 시간 (초)`를 2로 설정합니다. Stage1_Portal1 예시에 적용합니다. 일반 MoveObject는 기존처럼 로딩 없이 실행됩니다.

LoadingScreen.prefab은 FrameWork/Presentation/Prefabs에 있습니다. 검은 배경, 금색 Tip, Spinner를 직접 꾸밀 수 있습니다. LoadingScreenCanvas의 tips 배열에서 도움말을 수정합니다. 표시 시간 동안 게임 시간이 정지하고, 실제 시간으로 2초를 기다린 뒤 복구합니다. 이동 Duration이 더 길면 이동이 끝날 때까지 화면과 다음 Phase를 유지합니다. 취소 시에도 화면과 정지를 해제합니다.
