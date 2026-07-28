namespace GprPrediction.Wpf.Contracts;

/// <summary>
/// UserMessageKind 상태 선택지 정의
/// 허용 상태를 제한해 분기 기준의 일관성 확보
/// </summary>
public enum UserMessageKind
{
    Information,
    Warning,
    Error
}

/// <summary>
/// ViewModel에서 파일 선택과 사용자 알림을 요청하기 위한 UI 독립 계약
/// 구현과 사용 계층의 결합을 낮춰 대체 가능성 확보
/// </summary>
public interface IUserDialogService
{
    /// <summary>
    /// SelectScanFile 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    string? SelectScanFile(string initialDirectory);

    /// <summary>
    /// SelectAlgorithmDirectory 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    string? SelectAlgorithmDirectory();

    /// <summary>
    /// SelectPythonExecutable 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    string? SelectPythonExecutable();

    /// <summary>
    /// SelectMapFile 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    string? SelectMapFile();

    /// <summary>
    /// SelectResultFiles 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    IReadOnlyList<string> SelectResultFiles(string initialDirectory);

    /// <summary>
    /// ShowMessage 화면 표시
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    void ShowMessage(string message, string caption, UserMessageKind kind);
}
