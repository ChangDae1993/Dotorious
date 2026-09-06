# Main · Winter Path

Main의 기존 TitleMenu → Start EventSO → 카메라 하강 연출을 유지하고 오른쪽 X=300까지 이어지는 겨울 구간입니다.

## 실행과 조작

Main 씬을 열고 Play → Start. ← / → 이동, Left Shift 달리기, Space 점프, A 공격.
불빛 체크포인트는 체력을 회복합니다. 낙사하거나 체력이 0이 되면 마지막 불빛에서 부활합니다.
X=300에 도착하면 Presentation 말풍선이 끝난 뒤 도착 화면과 '처음부터 다시' 버튼이 표시됩니다.

## 리소스와 설정

- Assets/Resources/Art/Main: 직접 생성한 나무 3종, 발판·다리·오두막·문, 적 3종의 16프레임 시트. 원본 PNG 보존.
- Assets/Resources/2DObjectMaker/Animations: Idle / Walk / Prepare / Attack / Recover / Hit / Dead 클립.
- Assets/Resources/Prefabs/Enemy: 2DObjectMaker로 만든 적 프리팹. 플레이어는 Prefabs/Player에 있습니다.
- Assets/Resources/2DObjectMaker/Animators: 설정된 규칙/키와 연결된 자동 상태 그래프. 모션 연결 포함.
- Assets/Resources/2DObjectMaker/Definitions: 윈도우로 다시 열고 Modify할 수 있는 Definition.
- Assets/Resources/EventSO/Main: 6개 Presentation EventSO. 기존 Start는 자동 로딩 경로인 Resources/Presentation/Main/Stage1에 그대로 보존합니다.
- Assets/Resources/Prefabs/UI/WinterHUD.prefab: 편집 가능한 실제 UI 리소스.
- Assets/Resources/Audio/Main: 짧은 합성 효과음. Definition의 공용 행동별 소리 칸으로 연결.

## 구간

| X | 구간 | 특징 |
| --- | --- | --- |
| 0–48 | 눈길의 시작 | 기존 도입부, 조작 안내, 순찰병 |
| 48–100 | 서리 정찰로 | 첫 체크포인트, 돌진병, 낮은 점프 |
| 100–150 | 끊어진 다리 | 계단형 섬, 원거리 감시자 |
| 150–200 | 잊힌 문지기 | 폐허, 선택형 상단 발판 |
| 200–250 | 얼음 능선 | 높낮이가 다른 눈 덮인 섬 |
| 250–300 | 마지막 불빛 | 마지막 체크포인트, 강화 문지기, 오두막 |

필수 점프 간격은 2.5–3m이며 새 플레이어의 점프력/달리기에 맞춰 배치했습니다.
기존 Stage1 포털과 데모 SG-001 인스턴스는 비활성화했습니다. 기존 EventSO, 원본 플레이어/SG-001 Definition과 원본 프리팹은 덮어쓰지 않았습니다.

## JSON 예제

Assets/Resources/2DObjectMaker/JSON/WinterGuardian.json은 실제 배치된 강화 문지기입니다.
같은 폴더의 WinterGuardian.AllOptions.json은 그 문지기를 토대로 모든 Condition / Action / Patrol / Follow /
Detection / Attack / Terrain / Effect / Motion 선택지가 최소 한 번씩 등장하도록 비활성 참고 규칙을 추가한 버전입니다.
플레이어 이동/달리기/점프, 다중 공격·콤보, 행동별 오디오, 씬 생성 설정도 들어 있습니다.
무한한 수치 조합이나 모든 키 조합을 생성한 것이 아니라 전체 설정 필드와 드롭다운 선택지의 참고용입니다.
Enemy를 Player로 바꾸지 않는 한 player 설정은 사용되지 않습니다.

Import JSON은 편집 화면에만 반영됩니다. 기존 대상에 저장하려면 Modify, 새 대상은 Make를 누릅니다.
에셋 파일 자체는 JSON에 포함되지 않으므로 다른 프로젝트에는 관련 리소스와 .meta도 복사하세요.

## 유지보수

게임 전용 코드는 Assets/Scripts/Main, 생성 코드는 Assets/Editor/Main에 있습니다.
프레임워크의 EventSO/Definition 스키마와 런타임 Resources 로딩 키는 바꾸지 않습니다.
Tools > Dotorious > Build Winter Path (Main)은 이 구간을 다시 생성하는 개발용 명령입니다.
직접 꾸민 WinterStage 하위 배치와 Winter* 생성 에셋을 다시 작성하므로, 수동 수정 후에는 백업 없이 재생성하지 마세요.
