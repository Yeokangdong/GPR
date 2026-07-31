using System.IO;
using System.Windows;

namespace GprPrediction.Wpf.Services;

/// <summary>
/// 애플리케이션의 라이트·다크 테마 전환 관리
/// 모든 창이 동일한 팔레트와 사용자 선택을 공유하도록 단일 인스턴스 제공
/// </summary>
public sealed class ThemeManager
{
    private const string DarkThemeSource = "Styles/Theme.Dark.xaml";
    private const string LightThemeSource = "Styles/Theme.Light.xaml";
    private readonly string preferencePath;

    /// <summary>
    /// 프로세스 전체에서 공유하는 테마 관리자 제공
    /// 창마다 서로 다른 테마 상태가 생기는 문제 방지
    /// </summary>
    public static ThemeManager Instance { get; } = new();

    /// <summary>
    /// 현재 적용된 테마 값 제공
    /// 전환 버튼과 새 창이 같은 팔레트를 선택하도록 상태 노출
    /// </summary>
    public AppTheme CurrentTheme { get; private set; } = AppTheme.Dark;

    /// <summary>
    /// 테마 설정 저장 경로 초기화
    /// 사용자별 선택을 다음 실행에서도 복원하기 위한 위치 준비
    /// </summary>
    private ThemeManager()
    {
        preferencePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HBC",
            "GPR",
            "ui-theme.txt");
    }

    /// <summary>
    /// 저장된 테마를 읽어 애플리케이션에 적용
    /// 런처와 메인 화면이 처음부터 동일한 팔레트로 표시되도록 시작 시 실행
    /// </summary>
    public void LoadAndApply()
    {
        var theme = AppTheme.Dark;
        try
        {
            if (File.Exists(preferencePath) &&
                Enum.TryParse(File.ReadAllText(preferencePath).Trim(), true, out AppTheme savedTheme))
            {
                theme = savedTheme;
            }
        }
        catch (IOException)
        {
            // 설정 파일을 읽지 못하면 안전한 기본 다크 테마 사용
        }
        catch (UnauthorizedAccessException)
        {
            // 사용자 설정 경로에 접근할 수 없으면 기본 테마 유지
        }

        Apply(theme, persist: false);
    }

    /// <summary>
    /// 라이트와 다크 테마를 교대로 전환
    /// 한 번의 버튼 입력으로 전체 화면 팔레트를 변경하도록 단순화
    /// </summary>
    public AppTheme Toggle()
    {
        var nextTheme = CurrentTheme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
        Apply(nextTheme, persist: true);
        return nextTheme;
    }

    /// <summary>
    /// 지정한 팔레트 리소스를 애플리케이션에 교체 적용
    /// 공통 스타일을 유지하면서 색상 리소스만 일관되게 변경
    /// </summary>
    public void Apply(AppTheme theme, bool persist)
    {
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var palette = dictionaries.FirstOrDefault(IsThemePalette);
        var nextSource = new Uri(
            theme == AppTheme.Light ? LightThemeSource : DarkThemeSource,
            UriKind.Relative);

        if (palette is null)
        {
            dictionaries.Add(new ResourceDictionary { Source = nextSource });
        }
        else
        {
            palette.Source = nextSource;
        }

        CurrentTheme = theme;
        if (persist)
        {
            SavePreference(theme);
        }
    }

    /// <summary>
    /// 리소스 사전이 교체 가능한 테마 팔레트인지 판별
    /// 공통 컨트롤 스타일 사전을 실수로 제거하는 상황 방지
    /// </summary>
    private static bool IsThemePalette(ResourceDictionary dictionary)
    {
        var source = dictionary.Source?.OriginalString;
        return source?.EndsWith("Theme.Dark.xaml", StringComparison.OrdinalIgnoreCase) == true ||
               source?.EndsWith("Theme.Light.xaml", StringComparison.OrdinalIgnoreCase) == true;
    }

    /// <summary>
    /// 선택된 테마 값을 사용자 설정 파일에 저장
    /// 애플리케이션 재실행 후에도 마지막 화면 모드 유지
    /// </summary>
    private void SavePreference(AppTheme theme)
    {
        try
        {
            var directory = Path.GetDirectoryName(preferencePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(preferencePath, theme.ToString());
        }
        catch (IOException)
        {
            // 테마는 적용하되 설정 저장 실패로 화면 전환을 중단하지 않음
        }
        catch (UnauthorizedAccessException)
        {
            // 쓰기 권한이 없는 환경에서도 현재 세션 테마는 유지
        }
    }
}

/// <summary>
/// 애플리케이션에서 지원하는 화면 테마 정의
/// 팔레트 파일 선택을 명확한 값으로 제한
/// </summary>
public enum AppTheme
{
    Dark,
    Light
}
