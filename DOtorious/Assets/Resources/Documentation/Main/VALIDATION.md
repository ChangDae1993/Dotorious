# 검증 기록 — 2026-09-06

## 확인한 결과

- 복제 프로젝트에서 실제 Main PlayMode 실행.
- A 키 공격으로 적 HP가 0이 된 뒤 GameObject 제거 확인.
- Space + Left Shift 입력으로 필수 점프 13곳 모두 건너고 착지 확인.
- 낙사, HP 0 부활, 체크포인트 회복, 부활 후 카메라 부모 연결 확인.
- X=300 도착 시 Presentation duration 종료 후 도착 UI 표시 및 재시작 확인.
- Start / Option 실제 Input System 마우스 이벤트를 통한 UI 클릭과 기존 Start 연출 검사 통과.
- 말풍선의 한글 짧은/긴 문장, 타이핑, duration, 타겟 추적, 본체/꼬리 연결 PlayMode 검사 통과.
- Main의 X=12, 128, 232, 289에서 실제 런타임 렌더 캡처를 열어 시각 확인. 초반 배경 틈 수정.
- JSON EditMode 테스트 7개 통과: 값/에셋 참조/한글 왕복, 잘못된 파일 거부, 누락 에셋 경고.
- Unity 재실행 뒤 전체 선택지 JSON 다시 읽기, 에셋 참조 복원, 왕복 직렬화 동일성 확인.
- Condition/Action/Patrol/Follow/Detection/Attack/Terrain/Effect/Motion 모든 드롭다운 값 포함 확인.
- 실사용 Dotorious를 다시 Unity로 열어 Main, 적 26개, 부활 콜라이더, 애니메이션 프레임 및 JSON 참조 검사 통과.
- 최종 로그 전체에서 error CS / Serialization depth limit / Missing Script / Import Error / NullReferenceException / Mismatched LayoutGroup / GUILayout / YAML parse 오류 0건.

## 남은 수동 UI 검증

다른 프로그램이 화면을 사용 중이어서 Unity를 앞으로 가져오는 조작은 보류했습니다.
JSON 버튼의 네이티브 파일 창 클릭 → Import → Make/Modify → Inspector 닫기/재열기 → Domain Reload
전체 사용자 클릭 흐름은 아직 미검증입니다. JSON 처리 코드의 자동 테스트와 재실행 후 참조 복원은 위와 같이 검사했습니다.
따라서 이 기록은 해당 수동 UI 검증까지 '모두 완료'했다는 뜻이 아닙니다.

## 원본 보존 / 전체 비교

작업 전후 Presentation과 2DObjectMaker 폴더 전체를 .meta 포함 재귀 SHA-256 비교했습니다.

- 2DObjectMaker: 작업 전 61/61 파일 동일. 작업 후 65/65 파일 전부 동일.
- Presentation: 기준 72 / 실사용 77 파일. 작업 전후 같은 차이만 존재.
  - Tooltip.prefab: Unity 버전의 m_UseReflectionProbes 직렬화 차이.
  - Custom_Sprite_Line3.png.meta: Unity importer 버전 자동 변환 차이.
  - 실사용 전용 Resources/JYW 관련 폴더 메타 2개와 Tests 관련 폴더 메타 3개.
  - 새 코드/리소스 불일치 없음. 더 최신 importer 메타를 오래된 형식으로 덮어쓰지 않음.
- Start.asset은 작업 전 바이트와 SHA-256 동일.
- 실사용 Main.unity는 마지막으로 검증한 복제본과 SHA-256 동일.
- 기존에 사용자가 삭제한 MainMenu/TitleScene 및 관련 변경은 복원하거나 되돌리지 않음.
- TagManager.asset의 빈 레이어 2줄에 빠진 공백만 복원하여 기존 YAML 파싱 오류 해결. 레이어 이름/번호 변경 없음.

원본 바이트 백업 및 로그/렌더 캡처:
C:/Users/혜진/Documents/GitHub/_codex_backups/WinterStage_20260906

주요 최종 로그:
json-final.log / json-final.xml
visual-tests.log / visual-tests.xml
play-tests3.log / play-tests3.xml
live-validation.log

주의: 초기 실패 로그도 원인 추적용으로 함께 보존되어 있습니다.

