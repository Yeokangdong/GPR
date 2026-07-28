using System.Diagnostics;
using System.Windows.Input;

namespace GprPrediction.Wpf.ViewModels;

/// <summary>
/// 비동기 작업을 ICommand로 노출해 버튼, 메뉴, 단축키와 ViewModel 로직을 연결
/// 관련 책임을 한곳에 모아 구조와 수명 경계 명확화
/// </summary>
public sealed class RelayCommand : ICommand
{
    private readonly Func<object?, Task> execute;
    private readonly Predicate<object?>? canExecute;
    private readonly Action<Exception>? errorHandler;
    private bool isExecuting;

    /// <summary>
    /// RelayCommand 인스턴스 초기화
    /// 필수 의존성과 초기 상태를 생성 시점에 확정
    /// </summary>
    public RelayCommand(
        Func<object?, Task> execute,
        Predicate<object?>? canExecute = null,
        Action<Exception>? errorHandler = null)
    {
        this.execute = execute ?? throw new ArgumentNullException(nameof(execute));
        this.canExecute = canExecute;
        this.errorHandler = errorHandler;
    }

    public event EventHandler? CanExecuteChanged;

    /// <summary>
    /// CanExecute 실행 가능 여부 확인
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    public bool CanExecute(object? parameter)
    {
        return !isExecuting && (canExecute?.Invoke(parameter) ?? true);
    }

    /// <summary>
    /// Execute 명령 실행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        try
        {
            isExecuting = true;
            RaiseCanExecuteChanged();
            await execute(parameter);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            errorHandler?.Invoke(ex);
        }
        finally
        {
            isExecuting = false;
            RaiseCanExecuteChanged();
        }
    }

    /// <summary>
    /// RaiseCanExecuteChanged 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    public void RaiseCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
