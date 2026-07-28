using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using GprPrediction.Wpf.Models;
using GprPrediction.Wpf.Services;
using GprPrediction.Wpf.ViewModels;
using Microsoft.Win32;

namespace GprPrediction.Wpf.Windows;

/// <summary>
/// MergeWindow 관련 상태와 동작 관리
/// 관련 책임을 한곳에 모아 구조와 수명 경계 명확화
/// </summary>
public partial class MergeWindow : Window
{
    private readonly MainViewModel viewModel;
    private readonly SavedResultReader savedResultReader = new();
    private readonly ObservableCollection<MergeResultRowItem> mergeRows = [];
    private readonly List<MergeFileColumn> fileColumns = [];
    private readonly List<string> loadErrors = [];
    private readonly CancellationTokenSource lifetimeCts = new();
    private bool loadedOnce;
    private bool loading;
    private bool applying;

    /// <summary>
    /// MergeWindow 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    public MergeWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        MergeGrid.ItemsSource = mergeRows;
        Loaded += async (_, _) =>
        {
            if (loadedOnce)
            {
                return;
            }

            loadedOnce = true;
            await LoadBundledFilesAsync();
        };
        Closed += (_, _) =>
        {
            lifetimeCts.Cancel();
            lifetimeCts.Dispose();
        };
    }

    /// <summary>
    /// MinimizeWindow 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void MinimizeWindow(object sender, ExecutedRoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    /// <summary>
    /// MaximizeRestoreWindow 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void MaximizeRestoreWindow(object sender, ExecutedRoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    /// <summary>
    /// CloseWindow 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void CloseWindow(object sender, ExecutedRoutedEventArgs e)
        => Close();

    /// <summary>
    /// Close_Click 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void Close_Click(object sender, RoutedEventArgs e)
        => Close();

    /// <summary>
    /// LoadBundledFilesAsync 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private async Task LoadBundledFilesAsync()
    {
        var files = viewModel.GetOpenedSavedResultFiles().Count > 0
            ? viewModel.GetOpenedSavedResultFiles()
            : viewModel.GetBundledSavedResultFiles();

        await LoadFilesAsync(files);
    }

    /// <summary>
    /// LoadFilesAsync 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private async Task LoadFilesAsync(IEnumerable<string> filePaths)
    {
        if (loading)
        {
            return;
        }

        ArgumentNullException.ThrowIfNull(filePaths);
        loading = true;
        LoadingOverlay.Visibility = Visibility.Visible;
        MergeGrid.IsEnabled = false;
        loadErrors.Clear();

        try
        {
            fileColumns.Clear();
            mergeRows.Clear();

            foreach (var filePath in filePaths)
            {
                lifetimeCts.Token.ThrowIfCancellationRequested();
                await AddFileColumnAsync(filePath);
            }

            RebuildGridColumns();
            RebuildRows();
            UpdateSelectionSummary();
            ShowLoadErrorsIfNeeded();
        }
        catch (OperationCanceledException) when (lifetimeCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            loadErrors.Add($"병합 목록 구성: {ex.Message}");
            if (IsVisible)
            {
                ShowLoadErrorsIfNeeded();
            }
        }
        finally
        {
            loading = false;
            MergeGrid.IsEnabled = true;
            LoadingOverlay.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// AddBrowsedFilesAsync 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private async Task AddBrowsedFilesAsync(IEnumerable<string> filePaths)
    {
        if (loading)
        {
            return;
        }

        ArgumentNullException.ThrowIfNull(filePaths);
        loading = true;
        LoadingOverlay.Visibility = Visibility.Visible;
        MergeGrid.IsEnabled = false;
        loadErrors.Clear();

        try
        {
            foreach (var filePath in filePaths)
            {
                lifetimeCts.Token.ThrowIfCancellationRequested();
                await AddFileColumnAsync(filePath);
            }

            RebuildGridColumns();
            RebuildRows();
            UpdateSelectionSummary();
            ShowLoadErrorsIfNeeded();
        }
        catch (OperationCanceledException) when (lifetimeCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            loadErrors.Add($"파일 추가: {ex.Message}");
            if (IsVisible)
            {
                ShowLoadErrorsIfNeeded();
            }
        }
        finally
        {
            loading = false;
            MergeGrid.IsEnabled = true;
            LoadingOverlay.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// AddFileColumnAsync 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private async Task AddFileColumnAsync(string filePath)
    {
        if (!File.Exists(filePath) ||
            fileColumns.Any(item => string.Equals(item.FilePath, filePath, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        try
        {
            var points = await savedResultReader.ReadAsync(filePath, lifetimeCts.Token);
            if (points.Count == 0)
            {
                return;
            }

            fileColumns.Add(new MergeFileColumn(
                Path.GetFileName(filePath),
                filePath,
                points));
        }
        catch (OperationCanceledException) when (lifetimeCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            loadErrors.Add($"{Path.GetFileName(filePath)}: {ex.Message}");
        }
    }

    /// <summary>
    /// RebuildGridColumns 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void RebuildGridColumns()
    {
        MergeGrid.Columns.Clear();
        MergeGrid.Columns.Add(CreateSelectionColumn());

        MergeGrid.Columns.Add(new DataGridTextColumn
        {
            Header = string.Empty,
            Width = new DataGridLength(42),
            IsReadOnly = true,
            Binding = new Binding(nameof(MergeResultRowItem.RowLabel))
        });

        for (var index = 0; index < fileColumns.Count; index++)
        {
            MergeGrid.Columns.Add(CreateFileColumn(index, fileColumns[index].FileName));
        }
    }

    /// <summary>
    /// CreateSelectionColumn 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private DataGridTemplateColumn CreateSelectionColumn()
    {
        var borderFactory = new FrameworkElementFactory(typeof(Border));
        borderFactory.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        borderFactory.SetValue(Border.PaddingProperty, new Thickness(10, 3, 10, 3));

        var checkBoxFactory = new FrameworkElementFactory(typeof(CheckBox));
        checkBoxFactory.SetValue(FrameworkElement.WidthProperty, 18d);
        checkBoxFactory.SetValue(FrameworkElement.HeightProperty, 18d);
        checkBoxFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        checkBoxFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        checkBoxFactory.SetValue(Control.FocusableProperty, false);
        checkBoxFactory.SetBinding(ToggleButton.IsCheckedProperty, new Binding(nameof(MergeResultRowItem.IsSelected))
        {
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        });
        checkBoxFactory.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(SelectionToggle_Click));

        borderFactory.AppendChild(checkBoxFactory);

        return new DataGridTemplateColumn
        {
            Header = "사용여부",
            Width = new DataGridLength(76),
            CellTemplate = new DataTemplate
            {
                VisualTree = borderFactory
            }
        };
    }

    /// <summary>
    /// CreateFileColumn 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private DataGridTemplateColumn CreateFileColumn(int index, string fileName)
    {
        var comboFactory = new FrameworkElementFactory(typeof(ComboBox));
        comboFactory.SetValue(ComboBox.MarginProperty, new Thickness(2, 1, 2, 1));
        comboFactory.SetValue(ComboBox.PaddingProperty, new Thickness(4, 1, 4, 1));
        comboFactory.SetValue(ComboBox.MinWidthProperty, 170d);
        comboFactory.SetValue(ComboBox.HorizontalContentAlignmentProperty, HorizontalAlignment.Left);
        comboFactory.SetValue(ComboBox.DisplayMemberPathProperty, nameof(SavedResultPoint.MergeDisplayText));
        comboFactory.SetBinding(ItemsControl.ItemsSourceProperty, new Binding($"Choices[{index}].AvailablePoints"));
        comboFactory.SetBinding(Selector.SelectedItemProperty, new Binding($"Choices[{index}].SelectedPoint")
        {
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        });

        return new DataGridTemplateColumn
        {
            Header = fileName,
            Width = new DataGridLength(1, DataGridLengthUnitType.SizeToHeader),
            MinWidth = 188,
            CellTemplate = new DataTemplate
            {
                VisualTree = comboFactory
            }
        };
    }

    /// <summary>
    /// RebuildRows 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void RebuildRows()
    {
        var previousStates = mergeRows
            .ToDictionary(row => row.RowLabel, row => row.IsSelected, StringComparer.Ordinal);

        mergeRows.Clear();

        if (fileColumns.Count == 0)
        {
            return;
        }

        var rowCount = Math.Max(11, fileColumns.Max(column => column.Points.Count));
        for (var rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            var rowKey = rowIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var row = new MergeResultRowItem
            {
                RowLabel = rowKey,
                IsSelected = previousStates.TryGetValue(rowKey, out var isSelected) && isSelected
            };

            foreach (var column in fileColumns)
            {
                var choice = new MergeResultRowChoice
                {
                    AvailablePoints = new ObservableCollection<SavedResultPoint>(column.Points)
                };

                if (rowIndex < column.Points.Count)
                {
                    choice.SelectedPoint = column.Points[rowIndex];
                }

                row.Choices.Add(choice);
            }

            mergeRows.Add(row);
        }
    }

    /// <summary>
    /// Browse_Click 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private async void Browse_Click(object sender, RoutedEventArgs e)
    {
        if (loading)
        {
            return;
        }
        var dialog = new OpenFileDialog
        {
            Filter = "SEN files (*.sen)|*.sen|All files (*.*)|*.*",
            Title = "병합할 SEN 파일 선택",
            Multiselect = true
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            await AddBrowsedFilesAsync(dialog.FileNames);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            CustomMessageBox.Show(
                $"SEN 파일을 추가하지 못했습니다.\n{ex.Message}",
                "GPR 병합",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// OpenFolder_Click 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var firstFile = viewModel.GetBundledSavedResultFiles().FirstOrDefault();
        if (firstFile is null)
        {
            CustomMessageBox.Show("샘플 SEN 폴더를 찾지 못했습니다.", "GPR 병합", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{firstFile}\"",
            UseShellExecute = true
        });
    }

    /// <summary>
    /// Apply_Click 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (applying || loading)
        {
            return;
        }
        var selectedRows = mergeRows
            .Where(row => row.IsSelected)
            .Select(row => (IReadOnlyList<SavedResultPoint>)row.Choices
                .Select(choice => choice.SelectedPoint)
                .Where(point => point is not null)
                .Cast<SavedResultPoint>()
                .ToList())
            .Where(row => row.Count > 0)
            .ToList();

        if (selectedRows.Count == 0)
        {
            CustomMessageBox.Show("하나 이상의 병합 행을 선택해주세요.", "GPR 병합", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        applying = true;
        try
        {
            await viewModel.LoadMergedSelectionRowsAsync(selectedRows);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            CustomMessageBox.Show(
                $"병합 결과를 적용하지 못했습니다.\n{ex.Message}",
                "GPR 병합",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            applying = false;
        }
    }

    /// <summary>
    /// UpdateSelectionSummary 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void UpdateSelectionSummary()
    {
        var selectedRowCount = mergeRows.Count(row => row.IsSelected);
        SelectionSummaryText.Text = $"{selectedRowCount}개 행 선택 / {fileColumns.Count}개 결과 파일";
    }

    /// <summary>
    /// ShowLoadErrorsIfNeeded 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void ShowLoadErrorsIfNeeded()
    {
        if (loadErrors.Count == 0)
        {
            return;
        }

        var preview = string.Join(Environment.NewLine, loadErrors.Take(5));
        var suffix = loadErrors.Count > 5
            ? $"{Environment.NewLine}..."
            : string.Empty;

        CustomMessageBox.Show(
            $"일부 SEN 파일을 읽지 못했습니다.{Environment.NewLine}{Environment.NewLine}{preview}{suffix}",
            "GPR 병합",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

        loadErrors.Clear();
    }

    /// <summary>
    /// MergeGrid_CurrentCellChanged 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void MergeGrid_CurrentCellChanged(object? sender, EventArgs e)
        => UpdateSelectionSummary();

    /// <summary>
    /// MergeGrid_CellEditEnding 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void MergeGrid_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
        => Dispatcher.BeginInvoke(UpdateSelectionSummary);

    /// <summary>
    /// SelectionToggle_Click 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void SelectionToggle_Click(object sender, RoutedEventArgs e)
        => Dispatcher.BeginInvoke(UpdateSelectionSummary);

    /// <summary>
    /// MergeFileColumn 관련 상태와 동작 관리
    /// 관련 책임을 한곳에 모아 구조와 수명 경계 명확화
    /// </summary>
    private sealed record MergeFileColumn(
        string FileName,
        string FilePath,
        IReadOnlyList<SavedResultPoint> Points);
}

