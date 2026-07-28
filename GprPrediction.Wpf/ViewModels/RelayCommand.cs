using GprPrediction.Wpf.Windows;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;

namespace GprPrediction.Wpf.ViewModels;

/// <summary>
/// 비동기 작업을 ICommand로 노출해 버튼, 메뉴, 단축키와 ViewModel 로직을 연결
/// </summary>
public sealed class RelayCommand : ICommand
{
    private readonly Func<object?, Task> execute;
    private readonly Predicate<object?>? canExecute;
    private bool isExecuting;

    /// <summary>
    /// 실행 델리게이트와 선택적 활성화 조건으로 명령 객체를 구성
    /// </summary>
    public RelayCommand(Func<object?, Task> execute, Predicate<object?>? canExecute = null)
    {
        this.execute = execute ?? throw new ArgumentNullException(nameof(execute));
        this.canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    /// <summary>
    /// 현재 실행 중이 아니고 추가 실행 조건도 만족하는지 검사
    /// </summary>
    public bool CanExecute(object? parameter)
    {
        return !isExecuting && (canExecute?.Invoke(parameter) ?? true);
    }

    /// <summary>
    /// 명령을 비동기로 실행하고, 실행 중 재진입을 막고 예외를 사용자에게 안내
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
            CustomMessageBox.Show(ex.Message, "실행 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            isExecuting = false;
            RaiseCanExecuteChanged();
        }
    }

    /// <summary>
    /// 버튼 활성화 상태를 다시 계산하도록 CanExecuteChanged를 발생
    /// </summary>
    public void RaiseCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}


