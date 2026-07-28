using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;

if (args.Length == 0)
{
    Console.WriteLine("Usage: DwgInspect <path-to-dwg>");
    return;
}

var dwgPath = args[0];
if (!File.Exists(dwgPath))
{
    Console.WriteLine($"Missing file: {dwgPath}");
    return;
}

using var reader = new DwgReader(dwgPath);
CadDocument document = reader.Read();

Console.WriteLine($"FILE {Path.GetFileName(dwgPath)}");
Console.WriteLine($"ENTITIES {document.Entities.Count}");

var typeCounts = document.Entities
    .GroupBy(entity => entity.GetType().Name)
    .OrderByDescending(group => group.Count())
    .ToList();

Console.WriteLine("ENTITY TYPES");
foreach (var group in typeCounts)
{
    Console.WriteLine($"  {group.Key,-20} {group.Count(),8}");
}

var suspicious = new List<PolylineStat>();
var layerStats = new Dictionary<string, LayerStat>(StringComparer.OrdinalIgnoreCase);
double globalMinX = double.MaxValue, globalMinY = double.MaxValue;
double globalMaxX = double.MinValue, globalMaxY = double.MinValue;

foreach (var entity in document.Entities)
{
    var points = entity switch
    {
        Line line => new List<(double X, double Y)>
        {
            (line.StartPoint.X, line.StartPoint.Y),
            (line.EndPoint.X, line.EndPoint.Y)
        },
        LwPolyline lwPolyline => ReadVertices(lwPolyline.Vertices.Select(v => (v.Location.X, v.Location.Y)), lwPolyline.IsClosed),
        Polyline2D polyline2D => ReadVertices(polyline2D.Vertices.Select(v => (v.Location.X, v.Location.Y)), polyline2D.IsClosed),
        Circle circle => BuildCirclePoints(circle.Center.X, circle.Center.Y, circle.Radius),
        _ => null
    };

    if (points is null || points.Count < 2)
    {
        continue;
    }

    var minX = points.Min(p => p.X);
    var maxX = points.Max(p => p.X);
    var minY = points.Min(p => p.Y);
    var maxY = points.Max(p => p.Y);

    var maxSegment = 0.0;
    for (var i = 1; i < points.Count; i++)
    {
        var dx = points[i].X - points[i - 1].X;
        var dy = points[i].Y - points[i - 1].Y;
        var segment = Math.Sqrt(dx * dx + dy * dy);
        if (segment > maxSegment)
        {
            maxSegment = segment;
        }
    }

    foreach (var (px, py) in points) {
        if (px < globalMinX) globalMinX = px;
        if (py < globalMinY) globalMinY = py;
        if (px > globalMaxX) globalMaxX = px;
        if (py > globalMaxY) globalMaxY = py;
    }

    suspicious.Add(new PolylineStat(
        entity.GetType().Name,
        entity.Layer?.Name ?? string.Empty,
        entity.Handle.ToString(),
        points.Count,
        maxX - minX,
        maxY - minY,
        maxSegment));

    var layerName = entity.Layer?.Name ?? string.Empty;
    if (!layerStats.TryGetValue(layerName, out var layerStat))
    {
        layerStat = new LayerStat(layerName);
        layerStats[layerName] = layerStat;
    }

    layerStat.EntityCount++;
    layerStat.TotalPointCount += points.Count;
    layerStat.MaxSpanX = Math.Max(layerStat.MaxSpanX, maxX - minX);
    layerStat.MaxSpanY = Math.Max(layerStat.MaxSpanY, maxY - minY);
    layerStat.MaxSegment = Math.Max(layerStat.MaxSegment, maxSegment);
}

Console.WriteLine($"GLOBAL BOUNDS X: {globalMinX:F3} to {globalMaxX:F3}  span={globalMaxX-globalMinX:F3}");
Console.WriteLine($"GLOBAL BOUNDS Y: {globalMinY:F3} to {globalMaxY:F3}  span={globalMaxY-globalMinY:F3}");
Console.WriteLine("TOP LAYERS");
foreach (var layer in layerStats.Values
             .OrderByDescending(item => item.TotalPointCount)
             .ThenByDescending(item => item.EntityCount)
             .Take(30))
{
    Console.WriteLine(
        $"  {layer.Name,-16} entities={layer.EntityCount,6} totalPts={layer.TotalPointCount,8} maxSpanX={layer.MaxSpanX,10:F1} maxSpanY={layer.MaxSpanY,10:F1} maxSeg={layer.MaxSegment,10:F1}");
}

Console.WriteLine("TOP BY MAX SEGMENT");
foreach (var stat in suspicious.OrderByDescending(item => item.MaxSegment).Take(25))
{
    Console.WriteLine(
        $"  {stat.Type,-16} layer={stat.Layer,-16} handle={stat.Handle,-8} pts={stat.PointCount,6} spanX={stat.SpanX,12:F3} spanY={stat.SpanY,12:F3} maxSeg={stat.MaxSegment,12:F3}");
}

Console.WriteLine("TOP BY SPAN Y");
foreach (var stat in suspicious.OrderByDescending(item => item.SpanY).Take(25))
{
    Console.WriteLine(
        $"  {stat.Type,-16} layer={stat.Layer,-16} handle={stat.Handle,-8} pts={stat.PointCount,6} spanX={stat.SpanX,12:F3} spanY={stat.SpanY,12:F3} maxSeg={stat.MaxSegment,12:F3}");
}

static List<(double X, double Y)> ReadVertices(IEnumerable<(double X, double Y)> vertices, bool isClosed)
{
    var points = vertices.ToList();
    if (isClosed && points.Count > 0)
    {
        points.Add(points[0]);
    }

    return points;
}

static List<(double X, double Y)> BuildCirclePoints(double centerX, double centerY, double radius)
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

internal sealed record PolylineStat(
    string Type,
    string Layer,
    string Handle,
    int PointCount,
    double SpanX,
    double SpanY,
    double MaxSegment);

internal sealed class LayerStat
{
    public LayerStat(string name)
    {
        Name = name;
    }

    public string Name { get; }
    public int EntityCount { get; set; }
    public int TotalPointCount { get; set; }
    public double MaxSpanX { get; set; }
    public double MaxSpanY { get; set; }
    public double MaxSegment { get; set; }
}
