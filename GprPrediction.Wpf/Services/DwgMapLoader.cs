using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using System.IO;

namespace GprPrediction.Wpf.Services;

/// <summary>
/// 원본 GPR 프로그램이 사용하는 DWG 지도를 화면 배경용 폴리라인 데이터로 변환
/// </summary>
public static class DwgMapLoader
{
    /// <summary>
    /// DWG에서 선, 폴리라인, 원을 읽어 화면 렌더링용 점 목록으로 변환
    /// </summary>
    public static List<List<(double X, double Y)>> LoadPolylines(string dwgPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dwgPath);
        if (!File.Exists(dwgPath))
        {
            throw new FileNotFoundException("DWG 지도 파일을 찾을 수 없습니다.", dwgPath);
        }

        var polylines = new List<List<(double X, double Y)>>();

        using var reader = new DwgReader(dwgPath);
        CadDocument document = reader.Read();

        foreach (var entity in document.Entities)
        {
            switch (entity)
            {
                case Line line:
                    if (AreFinite(line.StartPoint.X, line.StartPoint.Y, line.EndPoint.X, line.EndPoint.Y))
                    {
                        polylines.Add(new List<(double X, double Y)>
                        {
                            (line.StartPoint.X, line.StartPoint.Y),
                            (line.EndPoint.X, line.EndPoint.Y)
                        });
                    }
                    break;

                case LwPolyline lwPolyline:
                {
                    var points = lwPolyline.Vertices
                        .Select(v => (v.Location.X, v.Location.Y))
                        .Where(static point => double.IsFinite(point.X) && double.IsFinite(point.Y))
                        .ToList();
                    if (points.Count >= 2)
                    {
                        // 닫힌 폴리라인은 시작점을 한 번 더 붙여 윤곽이 끊기지 않게 만들기
                        if (lwPolyline.IsClosed && points.Count > 0)
                        {
                            points.Add(points[0]);
                        }

                        polylines.Add(points);
                    }

                    break;
                }

                case Polyline2D polyline2D:
                {
                    var points = polyline2D.Vertices
                        .Select(v => (v.Location.X, v.Location.Y))
                        .Where(static point => double.IsFinite(point.X) && double.IsFinite(point.Y))
                        .ToList();
                    if (points.Count >= 2)
                    {
                        if (polyline2D.IsClosed && points.Count > 0)
                        {
                            points.Add(points[0]);
                        }

                        polylines.Add(points);
                    }

                    break;
                }

                case Circle circle:
                    if (AreFinite(circle.Center.X, circle.Center.Y, circle.Radius) && circle.Radius > 0)
                    {
                        polylines.Add(BuildCirclePoints(circle.Center.X, circle.Center.Y, circle.Radius));
                    }
                    break;
            }
        }

        return polylines;
    }

    /// <summary>
    /// 폴리라인 전체의 최소/최대 좌표를 계산해 화면 맞춤 렌더링 기준을 제공
    /// </summary>
    public static (double MinX, double MinY, double MaxX, double MaxY)? GetBounds(List<List<(double X, double Y)>> polylines)
    {
        ArgumentNullException.ThrowIfNull(polylines);
        var any = false;
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;

        foreach (var polyline in polylines)
        {
            foreach (var (x, y) in polyline)
            {
                if (!double.IsFinite(x) || !double.IsFinite(y))
                {
                    continue;
                }

                any = true;
                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
            }
        }

        return any ? (minX, minY, maxX, maxY) : null;
    }

    private static bool AreFinite(params double[] values) => values.All(double.IsFinite);

    /// <summary>
    /// 원 엔티티를 다각형 점 목록으로 근사해 일반 폴리라인처럼 그릴 수 있게 만들기
    /// </summary>
    private static List<(double X, double Y)> BuildCirclePoints(double centerX, double centerY, double radius)
    {
        const int segments = 24;
        var points = new List<(double X, double Y)>(segments + 1);
        for (var i = 0; i <= segments; i++)
        {
            var angle = 2 * Math.PI * i / segments;
            points.Add((centerX + radius * Math.Cos(angle), centerY + radius * Math.Sin(angle)));
        }

        return points;
    }
}
