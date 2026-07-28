using System.Windows;
using GprPrediction.Wpf.Contracts;
using GprPrediction.Wpf.Windows;
using Microsoft.Win32;

namespace GprPrediction.Wpf.Infrastructure;

/// <summary>
/// 파일 선택과 사용자 메시지를 WPF 대화상자로 구현
/// 관련 책임을 한곳에 모아 구조와 수명 경계 명확화
/// </summary>
public sealed class WpfUserDialogService : IUserDialogService
{
    /// <summary>
    /// SelectScanFile 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    public string? SelectScanFile(string initialDirectory)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "GPR scan files (*.dzt;*.sgy;*.csv)|*.dzt;*.sgy;*.csv|All files (*.*)|*.*",
            InitialDirectory = initialDirectory,
            Title = "스캔 파일 선택"
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    /// <summary>
    /// SelectAlgorithmDirectory 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    public string? SelectAlgorithmDirectory()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "알고리즘 폴더 선택"
        };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    /// <summary>
    /// SelectPythonExecutable 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    public string? SelectPythonExecutable()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Python executable (python.exe)|python.exe|Executable files (*.exe)|*.exe|All files (*.*)|*.*",
            Title = "Python 실행 파일 선택"
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    /// <summary>
    /// SelectMapFile 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    public string? SelectMapFile()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "DWG files (*.dwg)|*.dwg|All files (*.*)|*.*",
            Title = "배경 지도 DWG 추가"
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    /// <summary>
    /// SelectResultFiles 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    public IReadOnlyList<string> SelectResultFiles(string initialDirectory)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "결과 파일 (*.sen;*.csv)|*.sen;*.csv|저장 결과 (*.sen)|*.sen|결과 CSV (*.csv)|*.csv|All files (*.*)|*.*",
            Title = "결과 파일 선택",
            InitialDirectory = initialDirectory,
            Multiselect = true
        };
        return dialog.ShowDialog() == true ? dialog.FileNames : [];
    }

    /// <summary>
    /// ShowMessage 화면 표시
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    public void ShowMessage(string message, string caption, UserMessageKind kind)
    {
        var image = kind switch
        {
            UserMessageKind.Error => MessageBoxImage.Error,
            UserMessageKind.Warning => MessageBoxImage.Warning,
            _ => MessageBoxImage.Information
        };
        CustomMessageBox.Show(message, caption, MessageBoxButton.OK, image);
    }
}
