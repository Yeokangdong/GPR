# Portable Python 배포 메모

목표는 사용자가 Python을 별도로 설치하지 않고 WPF 프로그램 폴더만 받아 실행하게 하는 것이다.

## Python 버전 고정: 3.11.x 필수

`algorithm/detect.pyc`, `algorithm/data_agc.pyc`는 소스가 없는 컴파일 전용 모듈이며, 매직넘버를 확인한 결과 `a7 0d 0d 0a` = `0x0DA7` = `3495`로 **CPython 3.11** 바이트코드다. 3.12 이상 인터프리터에서는 `ImportError: bad magic number`로 가져오기 자체가 실패하므로, `tools/Prepare-PythonRuntime.ps1`로 준비하는 런타임은 반드시 3.11.x(원본 설치 패키지와 동일한 `python-3.11.1-amd64.exe` 계열)를 사용해야 한다.

## 배포 폴더 기준

WPF 실행 파일 옆에 다음 구조를 둔다.

```text
GprPrediction.Wpf.exe
runtime/
  python/
    python.exe
algorithm/
    main_1.py
    main_2.py
    model_info.txt
    Best_V1.260522.pt
    Best_Tda_V1.260522.pt
```

WPF 앱은 `runtime/python/python.exe`와 `algorithm` 폴더를 우선 사용한다. 없으면 화면에서 Python 실행 파일과 알고리즘 폴더를 직접 지정할 수 있다.

## 준비 절차

1. 배포용 Python 폴더를 준비한다.
2. `tools/Prepare-PythonRuntime.ps1`로 `GprPrediction.Wpf/runtime/python`에 복사한다.
3. 해당 Python에 알고리즘 의존성을 설치한다.

```powershell
.\tools\Prepare-PythonRuntime.ps1 -SourcePythonDirectory "C:\Python311"
.\GprPrediction.Wpf\runtime\python\python.exe -m pip install --upgrade pip
.\GprPrediction.Wpf\runtime\python\python.exe -m pip install -r ".\GprPrediction.Wpf\algorithm\requirements.txt"
```

기존 매뉴얼 기준 주요 패키지는 `pandas`, `numpy`, `torch`, `matplotlib`, `pykrige`, `scikit-learn`, `readgssi`, `obspy`, `ultralytics`이다.
