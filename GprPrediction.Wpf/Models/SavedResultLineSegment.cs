namespace GprPrediction.Wpf.Models;

/// <summary>
/// 저장 결과 점들을 잇는 화면용 선분 정보를 보관
/// </summary>
public sealed class SavedResultLineSegment
{
    public double X1 { get; init; }

    public double Y1 { get; init; }

    public double X2 { get; init; }

    public double Y2 { get; init; }
}
