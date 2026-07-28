namespace GprPrediction.Wpf.Services;

/// <summary>
/// 측정 시작점과 방향점을 기준으로 거리 값을 실제 지도 좌표로 투영하는 계산 유틸리티
/// </summary>
public static class SurveyLineProjector
{
    /// <summary>
    /// 시작점과 방향점을 기준으로 주어진 거리만큼 진행한 실제 좌표를 계산
    /// </summary>
    public static (double X, double Y) ProjectAlongLine(
        double startX,
        double startY,
        double directionPointX,
        double directionPointY,
        double distanceAlongLine)
    {
        if (!new[] { startX, startY, directionPointX, directionPointY, distanceAlongLine }.All(double.IsFinite))
        {
            throw new ArgumentOutOfRangeException(nameof(distanceAlongLine), "좌표와 거리는 유한한 숫자여야 합니다.");
        }

        var dx = directionPointX - startX;
        var dy = directionPointY - startY;
        var vectorScale = Math.Max(Math.Abs(dx), Math.Abs(dy));

        if (!double.IsFinite(vectorScale))
        {
            return (startX, startY);
        }

        if (vectorScale < 1e-9)
        {
            return (startX, startY);
        }

        // 먼저 스케일을 줄여 제곱 연산의 overflow를 방지한 뒤 단위 벡터를 계산한다.
        var scaledX = dx / vectorScale;
        var scaledY = dy / vectorScale;
        var scaledLength = Math.Sqrt(scaledX * scaledX + scaledY * scaledY);
        var unitX = scaledX / scaledLength;
        var unitY = scaledY / scaledLength;

        var projectedX = startX + unitX * distanceAlongLine;
        var projectedY = startY + unitY * distanceAlongLine;
        if (!double.IsFinite(projectedX) || !double.IsFinite(projectedY))
        {
            return (startX, startY);
        }

        return (projectedX, projectedY);
    }
}
