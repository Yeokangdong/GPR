using System.IO;
using System.Text;
using System.Text.Json;
using GprPrediction.Wpf.Models;

namespace GprPrediction.Wpf.Services;

/// <summary>
/// 앱 작업 상태를 로컬 AppData에 JSON으로 저장하고 복원
/// </summary>
public sealed class AppSessionStateStore
{
    private const long MaximumStateFileBytes = 4 * 1024 * 1024;
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
    private readonly object saveGate = new();

    /// <summary>
    /// 상태 파일 JSON 직렬화 옵션
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>
    /// 사용자별 로컬 AppData에 저장하는 세션 상태 파일 경로
    /// </summary>
    private readonly string stateFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HBC",
        "GPR",
        "session-state.json");

    /// <summary>
    /// 저장된 작업 상태를 읽어 반환
    /// </summary>
    public AppSessionState? Load()
    {
        try
        {
            if (!File.Exists(stateFilePath))
            {
                return null;
            }

            var fileInfo = new FileInfo(stateFilePath);
            if (fileInfo.Length <= 0 || fileInfo.Length > MaximumStateFileBytes)
            {
                return null;
            }

            var json = File.ReadAllText(stateFilePath);
            var state = JsonSerializer.Deserialize<AppSessionState>(json, JsonOptions);
            if (state is null)
            {
                return null;
            }

            state.AddedMapPaths = NormalizePaths(state.AddedMapPaths);
            state.OpenedSavedResultFiles = NormalizePaths(state.OpenedSavedResultFiles);
            state.SelectedResultIndex = Math.Max(1, state.SelectedResultIndex);
            return state;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 현재 작업 상태를 JSON 파일로 저장
    /// </summary>
    public void Save(AppSessionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var directoryPath = Path.GetDirectoryName(stateFilePath);
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return;
        }

        lock (saveGate)
        {
            Directory.CreateDirectory(directoryPath);
            var tempFilePath = $"{stateFilePath}.{Guid.NewGuid():N}.tmp";
            try
            {
                var json = JsonSerializer.Serialize(state, JsonOptions);
                File.WriteAllText(tempFilePath, json, Utf8NoBom);
                File.Move(tempFilePath, stateFilePath, overwrite: true);
            }
            finally
            {
                if (File.Exists(tempFilePath))
                {
                    try { File.Delete(tempFilePath); } catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }
    }

    private static List<string> NormalizePaths(IEnumerable<string>? paths) =>
        paths?
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(static path => path.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(1_000)
            .ToList()
        ?? [];
}
