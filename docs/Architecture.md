# GPR 애플리케이션 아키텍처

## 1. 기본 원칙

GPR 데스크톱 애플리케이션은 **MVVM + Composition Root 기반 싱글톤 수명 관리**를 사용한다.

- View는 화면 표시와 WPF 입력 이벤트만 처리한다.
- ViewModel은 화면 상태와 Command를 제공하며 구체적인 Window를 생성하지 않는다.
- Service는 분석, 파일 처리, 저장 및 외부 프로세스 실행을 담당한다.
- Contract는 ViewModel과 WPF 구현 사이의 인터페이스를 정의한다.
- Infrastructure는 구체적인 WPF 어댑터와 앱 전체 객체 생성을 담당한다.
- Model은 UI에 의존하지 않는 데이터 구조만 포함한다.

## 2. 패키지 구조

```text
GprPrediction.Wpf/
├─ Contracts/          ViewModel이 사용하는 서비스 인터페이스
├─ Infrastructure/     AppHost, WPF Window/Dialog 어댑터, Composition Root
├─ Models/             분석·지도·저장 결과 데이터 모델
├─ Services/           분석 실행, 변환, 읽기/쓰기 등 업무 로직
├─ ViewModels/         화면 상태, 바인딩 속성, ICommand
├─ Windows/            WPF View와 최소한의 화면 전용 code-behind
├─ Controls/           재사용 WPF 컨트롤
├─ Converters/         XAML 값 변환기
├─ Styles/             전역 ResourceDictionary와 테마
├─ Assets/             아이콘과 이미지
└─ App.xaml.cs         앱 시작/종료만 처리
```

`MainWindow`와 `LauncherWindow`는 Shell View이므로 현재 프로젝트 루트에 유지한다.
새로운 보조 화면은 반드시 `Windows`에 추가한다.

## 3. 싱글톤 정책

싱글톤 접근 지점은 `Infrastructure/AppHost` 하나로 제한한다.

- `AppHost`: `Lazy<T>`로 스레드 안전하게 한 번 생성한다.
- `MainViewModel`: 앱 세션 동안 하나만 유지한다.
- 상태를 공유하는 Service: `AppHost`에서 한 번 생성하여 생성자 주입한다.
- Window/Dialog: 싱글톤으로 만들지 않는다. 요청할 때 생성하고 닫으면 폐기한다.
- 실행별 상태: `CancellationTokenSource`, 프로세스 결과 등은 실행마다 새로 생성한다.

서비스 클래스 내부에 추가 `static Instance`를 만들지 않는다. 전역 서비스가 필요하면
`AppHost`에서 수명을 결정하고 ViewModel 생성자에 명시적으로 전달한다.

## 4. 의존 방향

```text
View ──binding──> ViewModel ──interface──> Contract
                                      └──> Service
Infrastructure ──creates───────────────^
Model <──────────── Service / ViewModel
```

금지 사항:

- ViewModel에서 `new Window()` 호출
- ViewModel에서 서비스 구현을 직접 `new`
- Model에서 WPF View 또는 ViewModel 참조
- 임의 클래스에 전역 mutable static 상태 추가
- View code-behind에서 분석·저장 업무 로직 구현

## 5. 수명과 종료

1. `App.OnStartup`이 `AppHost.Instance`를 초기화한다.
2. `AppHost`가 서비스들과 단일 `MainViewModel`을 생성한다.
3. `MainWindow`는 `AppHost.Instance.MainViewModel`을 DataContext로 사용한다.
4. 보조 Window도 같은 ViewModel을 전달받아 현재 분석 세션을 공유한다.
5. 개별 View는 공유 ViewModel을 Dispose하지 않는다.
6. `App.OnExit`만 세션 저장과 `AppHost.Dispose()`를 수행한다.

이 규칙으로 중복 ViewModel, 중복 타이머, 서로 다른 분석 상태 및 중복 Dispose 문제를 방지한다.
