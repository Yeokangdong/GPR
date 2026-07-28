using System.Globalization;
using System.IO;
using GprPrediction.Wpf.Models;

namespace GprPrediction.Wpf.Services;

/// <summary>
/// 원본 프로그램의 SEN 저장 결과 파일을 읽어 지도 표시용 점 목록으로 변환
/// </summary>
public sealed class SavedResultReader
{
    /// <summary>
    /// SEN 파일을 한 줄씩 파싱해 저장 결과 점 목록을 반환
    /// </summary>
    public async Task<IReadOnlyList<SavedResultPoint>> ReadAsync(string senPath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(senPath);
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(senPath))
        {
            throw new FileNotFoundException("SEN 결과 파일을 찾을 수 없습니다.", senPath);
        }

        var lines = await File.ReadAllLinesAsync(senPath, cancellationToken);
        var sourceName = Path.GetFileNameWithoutExtension(senPath);
        var points = new List<SavedResultPoint>();

        // SEN 포맷은 "|" 구분자를 쓰므로 필요한 좌표 토큰만 골라서 읽기
        foreach (var rawLine in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(rawLine))
            {
                continue;
            }

            var tokens = rawLine.Split('|');
            if (tokens.Length < 9)
            {
                continue;
            }

            var coordinateTokens = tokens[8]
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (coordinateTokens.Length < 2)
            {
                continue;
            }

            if (!double.TryParse(coordinateTokens[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
                !double.TryParse(coordinateTokens[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) ||
                !double.IsFinite(x) ||
                !double.IsFinite(y))
            {
                continue;
            }

            _ = double.TryParse(tokens[7], NumberStyles.Float, CultureInfo.InvariantCulture, out var depthMeters);
            if (!double.IsFinite(depthMeters))
            {
                depthMeters = 0;
            }

            points.Add(new SavedResultPoint
            {
                SourceName = sourceName,
                Label = string.IsNullOrWhiteSpace(tokens[1]) ? $"{points.Count + 1:00}" : tokens[1].Trim(),
                X = x,
                Y = y,
                DepthMeters = depthMeters
            });
        }

        return points;
    }
}
