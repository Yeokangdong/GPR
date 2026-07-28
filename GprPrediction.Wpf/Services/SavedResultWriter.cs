using System.Globalization;
using System.IO;
using System.Text;
using GprPrediction.Wpf.Models;

namespace GprPrediction.Wpf.Services;

/// <summary>
/// 현재 분석 결과를 원본 프로그램과 호환되는 SEN 형식으로 저장
/// </summary>
public sealed class SavedResultWriter
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    /// <summary>
    /// 분석 결과 목록을 지도 좌표로 투영한 뒤 타임스탬프 기반 파일명으로 SEN 파일을 작성
    /// </summary>
    public string Write(
        string outputDirectory,
        IEnumerable<PredictionResult> results,
        double startX,
        double startY,
        double directionPointX,
        double directionPointY)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentNullException.ThrowIfNull(results);
        if (!new[] { startX, startY, directionPointX, directionPointY }.All(double.IsFinite))
        {
            throw new ArgumentOutOfRangeException(nameof(startX), "측선 좌표는 유한한 숫자여야 합니다.");
        }
        if (new[] { startX, startY, directionPointX, directionPointY }.Any(static value => Math.Abs(value) > 1_000_000_000_000d))
        {
            throw new ArgumentOutOfRangeException(nameof(startX), "측선 좌표가 지원 범위를 벗어났습니다.");
        }
        if (Math.Abs(directionPointX - startX) < 1e-9 &&
            Math.Abs(directionPointY - startY) < 1e-9)
        {
            throw new ArgumentException("측선 시작점과 방향점은 서로 달라야 합니다.", nameof(directionPointX));
        }

        Directory.CreateDirectory(outputDirectory);

        var timestamp = DateTime.Now.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);
        var senPath = GetUniquePath(outputDirectory, timestamp);
        var builder = new StringBuilder();

        // 결과를 순번 기준으로 고정 정렬해 저장 파일의 재현성을 유지
        foreach (var result in results.OrderBy(item => item.Index))
        {
            if (!double.IsFinite(result.DistanceMeters) ||
                !double.IsFinite(result.DepthMeters) ||
                Math.Abs(result.DistanceMeters) > 1_000_000_000d ||
                Math.Abs(result.DepthMeters) > 1_000_000d ||
                result.Index <= 0 ||
                result.SourceIndex <= 0)
            {
                continue;
            }
            var (x, y) = SurveyLineProjector.ProjectAlongLine(
                startX,
                startY,
                directionPointX,
                directionPointY,
                result.DistanceMeters);

            builder.Append("8|");
            builder.Append(result.Index.ToString("00", CultureInfo.InvariantCulture));
            builder.Append("(#");
            builder.Append(result.SourceIndex.ToString("00", CultureInfo.InvariantCulture));
            builder.Append(")|12|180|180|180|1|");
            builder.Append(result.DepthMeters.ToString("0.00", CultureInfo.InvariantCulture));
            builder.Append('|');
            builder.Append(x.ToString("0.00000000", CultureInfo.InvariantCulture));
            builder.Append(' ');
            builder.Append(y.ToString("0.00000000", CultureInfo.InvariantCulture));
            builder.AppendLine();
        }

        var tempPath = $"{senPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(tempPath, builder.ToString(), Utf8NoBom);
            File.Move(tempPath, senPath);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }

        return senPath;
    }

    private static string GetUniquePath(string outputDirectory, string timestamp)
    {
        for (var suffix = 0; suffix < 10_000; suffix++)
        {
            var fileName = suffix == 0 ? $"{timestamp}.sen" : $"{timestamp}-{suffix:000}.sen";
            var candidate = Path.Combine(outputDirectory, fileName);
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException("고유한 SEN 결과 파일 이름을 만들 수 없습니다.");
    }
}
