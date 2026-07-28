using ProjNet.CoordinateSystems;
using ProjNet.CoordinateSystems.Transformations;

namespace GprPrediction.Wpf.Services;

/// <summary>
/// DWG 원좌표를 위경도(WGS84)로 변환하는 좌표계 정의와 변환기를 제공
/// </summary>
public static class CoordinateTransformService
{
    private const string Korean1985ModifiedCentralBeltWkt = """
        PROJCS["Korean 1985 / Modified Central Belt",
            GEOGCS["Korean 1985",
                DATUM["Korean_Datum_1985",
                    SPHEROID["Bessel 1841",6377397.155,299.1528128],
                    TOWGS84[-145.907,505.034,685.756,-1.162,2.347,1.592,6.342]],
                PRIMEM["Greenwich",0],
                UNIT["degree",0.0174532925199433]],
            PROJECTION["Transverse_Mercator"],
            PARAMETER["latitude_of_origin",38],
            PARAMETER["central_meridian",127.002890277778],
            PARAMETER["scale_factor",1],
            PARAMETER["false_easting",200000],
            PARAMETER["false_northing",500000],
            UNIT["metre",1]]
        """;

    private static readonly Lazy<MathTransform?> ProjectedToWgs84Transform = new(CreateTransform);

    public static string CoordinateReferenceText
        => "좌표계: DWG 원좌표 (중부원점 TM 계열)  |  위경도: WGS84";

    /// <summary>
    /// 투영 좌표를 위도/경도로 변환하고 실패 시 false를 반환
    /// </summary>
    public static bool TryConvertProjectedToWgs84(double x, double y, out double latitude, out double longitude)
    {
        latitude = 0;
        longitude = 0;
        if (!double.IsFinite(x) || !double.IsFinite(y))
        {
            return false;
        }

        var transform = ProjectedToWgs84Transform.Value;
        if (transform is null)
        {
            return false;
        }

        try
        {
            var result = transform.Transform([x, y]);
            if (result.Length < 2)
            {
                return false;
            }

            longitude = result[0];
            latitude = result[1];
            return double.IsFinite(longitude) &&
                   double.IsFinite(latitude) &&
                   longitude is >= -180 and <= 180 &&
                   latitude is >= -90 and <= 90;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Korean 1985 Modified Central Belt와 WGS84 사이의 수학 변환기를 생성
    /// </summary>
    private static MathTransform? CreateTransform()
    {
        try
        {
            var factory = new CoordinateSystemFactory();
            var transformFactory = new CoordinateTransformationFactory();
            var source = factory.CreateFromWkt(Korean1985ModifiedCentralBeltWkt);
            var target = GeographicCoordinateSystem.WGS84;
            return transformFactory.CreateFromCoordinateSystems(source, target).MathTransform;
        }
        catch
        {
            return null;
        }
    }
}
