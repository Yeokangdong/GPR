namespace GprPrediction.Wpf.Contracts;

public enum UserMessageKind
{
    Information,
    Warning,
    Error
}

/// <summary>
/// ViewModel에서 파일 선택과 사용자 알림을 요청하기 위한 UI 독립 계약입니다.
/// </summary>
public interface IUserDialogService
{
    string? SelectScanFile(string initialDirectory);

    string? SelectAlgorithmDirectory();

    string? SelectPythonExecutable();

    string? SelectMapFile();

    IReadOnlyList<string> SelectResultFiles(string initialDirectory);

    void ShowMessage(string message, string caption, UserMessageKind kind);
}
