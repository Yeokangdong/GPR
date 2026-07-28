using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using GprPrediction.Wpf.Models;

namespace GprPrediction.Wpf.Services;

/// <summary>
/// prediction_results.csv를 읽어 거리(X), 심도(Z), 신뢰도 값을 추출
/// </summary>
public sealed class PredictionResultReader
{
    /// <summary>
    /// prediction_results.csv를 동기식으로 읽어 분석 결과 컬렉션으로 변환
    /// </summary>
    public ObservableCollection<PredictionResult> Read(string csvPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(csvPath);
        if (!File.Exists(csvPath))
        {
            throw new FileNotFoundException("결과 CSV 파일을 찾을 수 없습니다.", csvPath);
        }

        var lines = File.ReadAllLines(csvPath);
        return ParseLines(lines);
    }

    /// <summary>
    /// prediction_results.csv를 비동기식으로 읽어 분석 결과 컬렉션으로 변환
    /// </summary>
    public async Task<ObservableCollection<PredictionResult>> ReadAsync(string csvPath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(csvPath);
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(csvPath))
        {
            throw new FileNotFoundException("결과 CSV 파일을 찾을 수 없습니다.", csvPath);
        }

        var lines = await File.ReadAllLinesAsync(csvPath, cancellationToken);
        return ParseLines(lines);
    }

    /// <summary>
    /// 서로 다른 CSV 스키마를 공통 PredictionResult 형식으로 정규화
    /// </summary>
    private static ObservableCollection<PredictionResult> ParseLines(string[] lines)
    {
        var parsedResults = new List<PredictionResult>();

        if (lines.Length == 0)
        {
            return new ObservableCollection<PredictionResult>();
        }

        var header = ParseCsvLine(lines[0])
            .Select(static h => h.Trim().TrimStart('\uFEFF'))
            .ToArray();
        var columnIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < header.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(header[i]))
            {
                columnIndex[header[i]] = i;
            }
        }

        // 알고리즘 원본 CSV와 앱이 다시 내보낸 CSV 두 형식을 모두 허용
        var hasExpectedSchema = columnIndex.ContainsKey("confidence") &&
                                columnIndex.ContainsKey("x1_m") &&
                                columnIndex.ContainsKey("x2_m") &&
                                columnIndex.ContainsKey("y1_m") &&
                                columnIndex.ContainsKey("y2_m");
        var hasExportSchema = columnIndex.ContainsKey("distance_m") &&
                              columnIndex.ContainsKey("depth_m") &&
                              (columnIndex.ContainsKey("confidence_pct") || columnIndex.ContainsKey("confidence_ratio"));

        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var values = ParseCsvLine(line);

            double distanceMeters;
            double depthMeters;
            double confidenceRatio;
            int sourceIndex;

            try
            {
                if (hasExpectedSchema)
                {
                    var x1 = ParseRequiredDouble(values, columnIndex["x1_m"], i + 1);
                    var x2 = ParseRequiredDouble(values, columnIndex["x2_m"], i + 1);
                    var y1 = ParseRequiredDouble(values, columnIndex["y1_m"], i + 1);
                    var y2 = ParseRequiredDouble(values, columnIndex["y2_m"], i + 1);
                    distanceMeters = (x1 + x2) / 2.0;
                    depthMeters = (y1 + y2) / 2.0;
                    confidenceRatio = ParseRequiredDouble(values, columnIndex["confidence"], i + 1);
                    sourceIndex = parsedResults.Count + 1;
                }
                else if (hasExportSchema)
                {
                    distanceMeters = ParseRequiredDouble(values, columnIndex["distance_m"], i + 1);
                    depthMeters = ParseRequiredDouble(values, columnIndex["depth_m"], i + 1);

                    confidenceRatio = columnIndex.TryGetValue("confidence_pct", out var confidencePctIndex)
                        ? ParseRequiredDouble(values, confidencePctIndex, i + 1) / 100.0
                        : ParseRequiredDouble(values, columnIndex["confidence_ratio"], i + 1);
                    sourceIndex = columnIndex.TryGetValue("source_index", out var sourceIndexColumn)
                        ? ParseInt(values, sourceIndexColumn, parsedResults.Count + 1)
                        : parsedResults.Count + 1;
                }
                else
                {
                    throw new InvalidDataException(
                        "지원하지 않는 결과 CSV 형식입니다. 필수 거리, 심도, 신뢰도 열을 찾지 못했습니다.");
                }
            }
            catch (FormatException)
            {
                // 한 행이 손상됐다고 정상 결과 전체를 잃지 않도록 해당 행만 제외한다.
                continue;
            }

            if (!double.IsFinite(distanceMeters) ||
                !double.IsFinite(depthMeters) ||
                !double.IsFinite(confidenceRatio))
            {
                continue;
            }

            parsedResults.Add(new PredictionResult
            {
                SourceIndex = sourceIndex > 0 ? sourceIndex : parsedResults.Count + 1,
                DistanceMeters = distanceMeters,
                DepthMeters = depthMeters,
                ConfidenceRatio = Math.Clamp(confidenceRatio, 0, 1),
                RawLine = line
            });
        }

        var rankedResults = parsedResults
            .OrderByDescending(result => result.ConfidenceRatio)
            .ThenBy(result => result.SourceIndex)
            .Select((result, index) => new PredictionResult
            {
                Index = index + 1,
                SourceIndex = result.SourceIndex,
                DistanceMeters = result.DistanceMeters,
                DepthMeters = result.DepthMeters,
                ConfidenceRatio = result.ConfidenceRatio,
                RawLine = result.RawLine
            });

        return new ObservableCollection<PredictionResult>(rankedResults);
    }

    /// <summary>
    /// 지정된 컬럼 인덱스에서 실수 값을 읽고 실패 시 0을 반환
    /// </summary>
    private static double ParseRequiredDouble(string[] values, int index, int lineNumber)
    {
        if (index < 0 || index >= values.Length)
        {
            throw new FormatException($"CSV {lineNumber}행에 필수 열이 없습니다.");
        }

        if (!double.TryParse(
                values[index].Trim(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var value) ||
            !double.IsFinite(value))
        {
            throw new FormatException($"CSV {lineNumber}행의 숫자 형식이 올바르지 않습니다.");
        }

        return value;
    }

    /// <summary>
    /// 지정된 컬럼 인덱스에서 정수 값을 읽고 실패 시 fallback 값을 반환
    /// </summary>
    private static int ParseInt(string[] values, int index, int fallback)
    {
        if (index < 0 || index >= values.Length)
        {
            return fallback;
        }

        return int.TryParse(values[index].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;
    }

    private static string[] ParseCsvLine(string line)
    {
        var values = new List<string>();
        var current = new System.Text.StringBuilder();
        var insideQuotes = false;

        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (character == '"')
            {
                if (insideQuotes && index + 1 < line.Length && line[index + 1] == '"')
                {
                    current.Append('"');
                    index++;
                }
                else
                {
                    insideQuotes = !insideQuotes;
                }
            }
            else if (character == ',' && !insideQuotes)
            {
                values.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(character);
            }
        }

        if (insideQuotes)
        {
            throw new FormatException("닫히지 않은 큰따옴표가 있는 CSV 행입니다.");
        }

        values.Add(current.ToString());
        return values.ToArray();
    }
}
