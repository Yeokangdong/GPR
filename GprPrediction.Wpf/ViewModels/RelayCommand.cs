using System.Diagnostics;
using System.Windows.Input;

namespace GprPrediction.Wpf.ViewModels;

/// <summary>
/// 비동기 작업을 ICommand로 노출해 버튼, 메뉴, 단축키와 ViewModel 로직을 연결합니다.
/// </summary>
public sealed class RelayCommand : ICommand
{
    private readonly Func<object?, Task> execute;
    private readonly Predicate<object?>? canExecute;
    private readonly Action<Exception>? errorHandler;
    private bool isExecuting;

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

    public bool CanExecute(object? parameter)
    {
        return !isExecuting && (canExecute?.Invoke(parameter) ?? true);
    }

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

    public void RaiseCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
