# GPR 기존 프로그램 분석 요약

## 입력 자료

- `GPR(2024.10.18).zip`: 배포 실행 파일, ODA/DWG DLL, Python 알고리즘, 모델 파일 포함
- `update.zip`: 신규 모델 파일 2개, `main_2.py`, TDA Julia 스크립트, 학습 설명 문서 포함
- `지하매설물 제원 예측 소프트웨어_GPR_사용 매뉴얼.pdf`: 설치/사용 매뉴얼

## 기존 배포본 구조

- `bin/release/GPR.exe`: 기존 Windows 실행 파일
- `bin/release/*.dll`: DWG/도면 처리와 화면 표시용으로 보이는 ODA/Teigha 계열 DLL 포함
- `data/algorithm`, `data/algorithm2`: YOLOv5 기반 Python 추론 코드와 모델 파일
- `install`: VC++ 재배포 패키지, Python 설치 파일, 나눔고딕 폰트
- `update.zip`: `main_2.py`, `model_info.txt`, `tda.jl`, `Best_V1.260522.pt`, `Best_Tda_V1.260522.pt`로 구성된 최신 모델 업데이트 묶음

## 기능 흐름

1. GPR 스캔 파일을 선택한다. 매뉴얼상 권장 파일은 `.DZT`이며 `.SGY`, `.CSV`도 가능하다.
2. 스캔 범위 X/Z, 시작점, 측정 방향, X/Y 이미지 스케일, 신뢰도 threshold를 입력한다.
3. 프로그램이 외부 알고리즘을 호출한다.
4. 알고리즘은 스캔 데이터를 이미지화하고 1차 Python 필터링, TDA Julia 필터링, 2차 Python 분석을 수행한다.
5. 결과 CSV와 이미지 결과를 읽어 Top View, Front View, 제원 표로 표시한다.
6. 부가 기능으로 도면 위 결과 표시, 여러 분석 결과 병합, 결과 리포트 출력/PDF 저장이 있다.

## WPF 재구성 방향

- `GprPrediction.Wpf`: 새 WPF UI 프로젝트
- 입력 화면: 스캔 파일, 알고리즘 폴더, Python 실행 파일, 스캔 범위, 스케일, threshold
- 실행 서비스: 알고리즘 폴더의 `data/input_info.txt` 생성 후 Python 스크립트 호출
- 결과 화면: `results/prediction_results.csv` 로드 후 표, Top View, Front View 표시
- 다음 단계: 기존 `data/algorithm2`와 `update.zip`의 최신 `main_2.py`, 모델 파일, `tda.jl` 연계 방식 확정

## 실행 방식 결정

- 현재 방향은 Python/Juliа 알고리즘을 유지하되, 사용자가 Python을 따로 설치하지 않도록 배포 폴더에 런타임을 포함하는 방식이다.
- WPF 앱은 `AppContext.BaseDirectory/runtime/python/python.exe`를 먼저 찾는다.
- 내장 Python이 없으면 화면에서 지정한 Python 경로나 `python` 명령을 사용한다.
- 배포 시에는 `runtime/python` 폴더, `data/algorithm2`, `C:/TDA` 또는 설정 가능한 TDA 폴더, 모델 파일을 함께 구성해야 한다.

## 업데이트 파일 메모

- `model_info.txt` 기준 기본 모드는 `tda`이며, TDA threshold는 `0.35`이다.
- 일반 모델은 `Best_V1.260522.pt`, TDA 모델은 `Best_Tda_V1.260522.pt`를 사용한다.
- 최신 `main_2.py`는 `threshold`를 `data/input_info.txt`에서 읽고, `model_info.txt` 설정에 따라 일반/TDA 모델을 선택한다.
- TDA 모드일 때 `C:/TDA/data.png`가 없으면 TDA를 끄고 일반 처리로 내려간다.

## 2026-06-16 구현 반영 사항

- `algorithm/`에 실제 자산 반입 완료: `detect.pyc`, `data_agc.pyc`, `models/`, `utils/`(YOLOv5), `requirements.txt`(base zip `data/algorithm2`) + `main_2.py`, `tda.jl`, `model_info.txt`, `Best_V1.260522.pt`, `Best_Tda_V1.260522.pt`(update.zip 최신본).
- `main_1.py`는 자체 작성한 수정판을 사용한다. 원본 배포판의 `data/algorithm2/main_1.py`는 `input_info.txt`의 `file_name`을 읽지 않고 `GAP_SEC01_0001.DZT`로 하드코딩되어 있어(같은 폴더의 `_main_1.py` 및 구버전 `data/algorithm/main.py`와 비교해 확인한 회귀), 동적으로 읽도록 고쳐서 배치했다.
- `AlgorithmRunner`는 이제 `main_1.py`(AGC 전처리) -> `tda.jl`(Julia TDA, 선택) -> `main_2.py`(YOLO 탐지) 3단계를 순서대로 실행한다. 매 실행마다 `model_info.txt`를 요청 값(TDA 사용 여부/threshold)으로 다시 쓴다. `JuliaRuntimeLocator`로 Julia 실행 파일을 탐색하며, 못 찾거나 TDA 산출물이 없으면 자동으로 일반 모델로 폴백한다(원본 `main_2.py`의 폴백 로직과 동일한 정책).
- `PredictionResultReader`는 실제 컬럼명(`confidence, x1_m, x2_m, y1_m, y2_m` 등)을 헤더 기준으로 매칭해서 읽도록 재작성했다. 이전 버전은 위치 기반 추정이라 컬럼이 밀려서 X/Z/신뢰도가 잘못 표시되는 버그가 있었다.
- 자료입력 창의 측정 시작점/측선 방향 좌표를 ViewModel에 바인딩했고, `SurveyLineProjector`로 결과의 X(거리)를 그 측선에 투영해 Map View에 실제 탐지 마커 + 측선을 표시한다. "측정결과 열기"는 저장된 CSV를 다시 불러와 같은 방식으로 Map View에 그린다.
- Map View 화면 좌표 변환은 X/Y에 같은 배율(등방 스케일)을 쓴다. 처음엔 X span/Y span을 박스에 각각 맞춰 늘려서 실제 측선 각도가 왜곡되어 보이는 버그가 있었다(예: 실제 76도 경사가 화면에서 35도로 보임) — 등방 스케일로 고쳤다.

## 2026-06-16(2) DWG 배경 지도 렌더링 추가

- 원본은 ODA/Teigha(유료 SDK)로 `data/map/*.dwg`를 읽어 배경으로 그렸다. 동급 무료 .NET 라이브러리를 찾아본 결과 **ACadSharp**(MIT 라이선스, NuGet `ACadSharp`)가 실제 `data/map/01.dwg`(LwPolyline 16,736개, 좌표범위 X[220021,224478] Y[489036,491822] - 측정 시작점 좌표계와 일치)를 문제없이 읽어냈다. `Services/DwgMapLoader.cs`로 LINE/LWPOLYLINE/POLYLINE2D/CIRCLE 엔티티를 폴리라인 점 목록으로 추출한다(INSERT 블록삽입·TEXT는 제외 - 도로/필지/등고선의 대부분은 LWPOLYLINE이라 시각적으로 충분함을 확인).
- `MainViewModel`은 DWG 로드/배경 지오메트리 생성(무거운 작업, DWG 경로가 바뀔 때만)과 측선·탐지점 화면좌표 재계산(가벼운 작업, 좌표 입력칸 타이핑마다)을 분리했다 — DWG가 LwPolyline 1.6만 개 규모라 매 키 입력마다 다시 읽으면 입력이 느려지기 때문.
- Map View에 "배경 지도(DWG)..." 버튼을 추가해 사용자가 직접 DWG 파일을 지정한다. 기본값은 비워둠(특정 현장의 실측 도면 데이터를 앱에 미리 내장하지 않음).
- 실제 01.dwg로 렌더링 테스트 완료 - 도로/필지/등고선 배경 위에 측정 시작점이 올바른 위치(도로 변)에 표시됨을 스크린샷으로 확인.

## 2026-06-16(3) 버그 수정 + 모던 디자인 + 맵 확대/축소 + 맵 다중 관리

- 비활성 버튼 글자 안 보임: WPF 기본 테마가 `IsEnabled=False`일 때 배경/글자색을 강제로 테마 회색으로 덮어써서 커스텀 색 버튼(Analysis 등)의 글자가 안 보였다. `FlatButton`/`ModernButtonBase` 템플릿으로 교체해 비활성 시 투명도만 낮추도록 했다.
- Map View에 드래그 패닝(`TranslateTransform`)과 마우스 휠 확대/축소(`ScaleTransform`, `TransformGroup`으로 결합)를 추가했다. 확대/축소는 커서 위치를 중심으로 한다(휠 이벤트 시점의 캔버스좌표를 구해 새 스케일 기준으로 이동값을 재계산).
- 분석 버튼 클릭 성공 시 `algorithm/results/data.jpg`(YOLO 탐지박스 포함 결과 이미지로 추정)를 `GPR Data Labeling` 패널에 표시하도록 연결했다. Python 런타임이 없어 실제 분석 산출물로는 검증 못함 - 바인딩 코드만 확인.
- 맵을 단일 경로 텍스트 대신 `MapEntry`(이름+경로) 목록으로 관리한다. 추가된 DWG는 칩(chip) 버튼으로 나열되고, "+ 추가" 버튼으로 새 DWG를 추가하면 자동으로 목록에 들어가고 즉시 선택된다. 세션 동안만 유지(재시작 시 초기화, 디스크 저장은 안 함).
- `Styles/Theme.xaml`에 공통 다크 모던 테마(카드형 Border+그림자, 둥근 모서리 버튼/텍스트박스, 액센트 색상)를 만들어 `App.xaml`에 병합하고 5개 창(MainWindow/InputWindow/MapViewWindow/MergeWindow/PrintWindow) 전체에 적용했다. 네이티브 창 타이틀바는 그대로 둠(커스텀 창 크롬은 별도 작업 필요).

## 2026-06-16(4) 후속 피드백 수정

- **비활성 버튼 글자 대비 재수정**: (3)번에서 적용한 "Opacity로 비활성 표시"는 배경과 글자가 부모-자식 구조라 Opacity가 같이 곱해져서 여전히 잘 안 보였다. `ModernButtonBase` 템플릿을 Grid 기반으로 바꿔 배경(Chrome)과 글자(Content)를 형제 요소로 분리, 비활성 시 배경만 흐리고 글자는 `TextSecondaryBrush`로 색만 바꿔 또렷하게 유지한다.
- **칩 버튼(1~16, 맵 목록) 안 보임**: `ColorSurfaceAlt`(#1B2333)가 `ColorSurface`(#141A26, 카드 배경)와 너무 비슷해서 칩이 카드에 묻혔다. `ColorSurfaceAlt`를 `#2A3550`, `ColorBorder`를 `#3D4A6B`로 밝혀서 카드 위에서 도드라지게 했다.
- **메인 화면 우측 패널 1~16 그리드 잘림**: 창 높이(780)와 내부 행 비율(46\*/53\*)로는 제목+Analysis 버튼+X/Z/C 3행+선택그리드 4행이 다 들어갈 공간이 부족해서 그리드가 거의 0높이로 짤렸다. 창 높이를 860(MinHeight 820)으로, 내부 분할을 62\*/38\*로 올리고 버튼/입력칸 높이를 줄여 여유를 만들었다. (이 과정에서 X/Z/C 값 표시칸 높이를 너무 줄여 글자가 안 보이는 회귀가 잠깐 있었음 — Height 36 / Padding 8,4 / FontSize 16으로 재조정해서 해결)
- **맵 파일 내장 + 자동 버튼**: `data/map/00,01,02.dwg`를 `GprPrediction.Wpf/maps/`에 복사하고 csproj에 `CopyToOutputDirectory` 추가. `MainViewModel.LoadBundledMaps()`가 시작 시 `maps/*.dwg`를 스캔해 `MapEntries`에 자동 추가하고 첫 항목을 자동 선택한다. "+ 추가"는 기존과 동일하게 수동으로 더 넣을 수 있다.
- **DWG 좌표계가 다른 지도일 때 안 보이던 문제**: `00.dwg`는 로컬 좌표계(X[-2052,2049])라 현재 측선 좌표(22만대)와 전혀 안 겹친다. 기존엔 무조건 DWG 범위와 측선 범위를 합쳐서 화면에 맞췄기 때문에, 합친 범위가 거대해져 00.dwg 내용이 점처럼 작아져 안 보였다. `RebuildMapBackground()`에서 측선이 DWG 범위(여유분 50% 포함)와 겹칠 때만 같이 맞추고, 안 겹치면 DWG 범위만으로 화면을 맞추도록 고쳤다.
- **패닝/줌 속도**: `MapCanvas.CacheMode = BitmapCache`를 적용해, DWG 폴리라인(01.dwg 기준 1.6만개 이상)을 매 프레임 다시 그리지 않고 캐시된 비트맵을 변환만 하도록 했다.

## 확인된 리스크 / 남은 범위

- 원본 ZIP은 C#/WPF 소스가 아니라 배포본 중심이다.
- 기존 알고리즘은 Python, Julia, PyTorch, readgssi, ultralytics 등 런타임 의존성이 크다.
- DWG 배경은 LINE/LWPOLYLINE/POLYLINE2D/CIRCLE만 그린다. INSERT(블록 삽입 - 기호 등)와 TEXT(라벨)는 그리지 않는다. 필요해지면 추가해야 한다.
- GPR 병합(`MergeWindow`)과 자료입력 창의 알고리즘 폴더/Python 경로 설정 외 나머지는 여전히 시각적 뼈대 수준이다.
- `detect.pyc`/`data_agc.pyc`는 **CPython 3.11 전용 바이트코드**(magic number `3495`/`0x0DA7`)로 컴파일되어 있고 소스가 없다. 포터블 Python은 반드시 3.11.x를 써야 하며 3.12+에서는 import 자체가 실패한다.
