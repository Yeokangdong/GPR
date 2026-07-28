namespace GprPrediction.Wpf.Models;

/// <summary>
/// 앱 재실행 시 복원할 사용자 작업 상태 묶음
/// 관련 책임을 한곳에 모아 구조와 수명 경계 명확화
/// </summary>
public sealed class AppSessionState
{
    /// <summary>
    /// 마지막으로 연 스캔 파일 경로
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public string ScanFilePath { get; set; } = string.Empty;

    /// <summary>
    /// 마지막 알고리즘 폴더 경로
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public string AlgorithmDirectory { get; set; } = string.Empty;

    /// <summary>
    /// 마지막 Python 실행 파일 경로
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public string PythonExecutable { get; set; } = string.Empty;

    /// <summary>
    /// 마지막 측정 범위 X 값
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public string ScanRangeX { get; set; } = string.Empty;

    /// <summary>
    /// 마지막 측정 범위 Z 값
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public string ScanRangeY { get; set; } = string.Empty;

    /// <summary>
    /// 마지막 X 스케일 값
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public string XScale { get; set; } = string.Empty;

    /// <summary>
    /// 마지막 Y 스케일 값
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public string YScale { get; set; } = string.Empty;

    /// <summary>
    /// 마지막 신뢰도 Threshold 값
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public string Threshold { get; set; } = string.Empty;

    /// <summary>
    /// 마지막 TDA 사용 여부
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public bool? UseTda { get; set; }

    /// <summary>
    /// 마지막 TDA Threshold 값
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public string TdaThreshold { get; set; } = string.Empty;

    /// <summary>
    /// 마지막 시작점 X 좌표
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public string StartPointX { get; set; } = string.Empty;

    /// <summary>
    /// 마지막 시작점 Y 좌표
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public string StartPointY { get; set; } = string.Empty;

    /// <summary>
    /// 마지막 방향점 X 좌표
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public string DirectionPointX { get; set; } = string.Empty;

    /// <summary>
    /// 마지막 방향점 Y 좌표
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public string DirectionPointY { get; set; } = string.Empty;

    /// <summary>
    /// 마지막으로 선택한 맵 경로
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public string SelectedMapPath { get; set; } = string.Empty;

    /// <summary>
    /// 사용자가 직접 추가한 외부 DWG 맵 경로 목록
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public List<string> AddedMapPaths { get; set; } = [];

    /// <summary>
    /// 마지막으로 연 결과 CSV 경로
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public string CurrentResultCsvPath { get; set; } = string.Empty;

    /// <summary>
    /// 마지막으로 연 SEN 결과 파일 목록
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public List<string> OpenedSavedResultFiles { get; set; } = [];

    /// <summary>
    /// 마지막으로 선택한 결과 번호
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public int SelectedResultIndex { get; set; } = 1;
}
