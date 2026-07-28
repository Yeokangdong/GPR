using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GprPrediction.Wpf.Models;
using GprPrediction.Wpf.ViewModels;

namespace GprPrediction.Wpf.Windows;

/// <summary>
/// ResultOpenWindow 관련 상태와 동작 관리
/// 관련 책임을 한곳에 모아 구조와 수명 경계 명확화
/// </summary>
public partial class ResultOpenWindow : Window
{
    private readonly MainViewModel viewModel;
    private readonly ObservableCollection<SavedResultFileSelectionItem> items = [];
    private readonly CancellationTokenSource lifetimeCts = new();
    private bool loadedOnce;
    private bool applying;

    /// <summary>
    /// ResultOpenWindow 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    public ResultOpenWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        ResultList.ItemsSource = items;
        Loaded += async (_, _) =>
        {
            if (loadedOnce)
            {
                return;
            }

            loadedOnce = true;
            await LoadFilesAsync();
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
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

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
    /// LoadFilesAsync 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private async Task LoadFilesAsync()
    {
        LoadingOverlay.Visibility = Visibility.Visible;
        ResultList.IsEnabled = false;
        items.Clear();

        try
        {
            await Task.Yield();
            var opened = viewModel.GetOpenedSavedResultFiles();
            foreach (var filePath in viewModel.GetBundledSavedResultFiles())
            {
                lifetimeCts.Token.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                {
                    continue;
                }

                var fileInfo = new FileInfo(filePath);
                items.Add(new SavedResultFileSelectionItem
                {
                    FileName = Path.GetFileName(filePath),
                    FilePath = filePath,
                    DisplayPath = BuildDisplayPath(filePath),
                    FileSizeText = ToReadableSize(fileInfo.Length),
                    ModifiedText = fileInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture),
                    IsSelected = opened.Contains(filePath, StringComparer.OrdinalIgnoreCase)
                });
            }

            UpdateSummary();
        }
        catch (OperationCanceledException) when (lifetimeCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            if (IsVisible)
            {
                CustomMessageBox.Show(
                    $"저장 결과 목록을 불러오지 못했습니다.\n{ex.Message}",
                    "측정결과 열기",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        finally
        {
            ResultList.IsEnabled = true;
            LoadingOverlay.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Apply_Click 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (applying)
        {
            return;
        }

        var selectedPaths = items
            .Where(item => item.IsSelected)
            .Select(item => item.FilePath)
            .ToArray();

        if (selectedPaths.Length == 0)
        {
            CustomMessageBox.Show("하나 이상의 저장 결과를 선택해주세요.", "측정결과 열기", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        applying = true;
        try
        {
            await viewModel.LoadMergedSavedResultsAsync(selectedPaths);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            CustomMessageBox.Show(
                $"선택한 결과를 적용하지 못했습니다.\n{ex.Message}",
                "측정결과 열기",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            applying = false;
        }
    }

    /// <summary>
    /// SelectionChanged_Update 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void SelectionChanged_Update(object sender, RoutedEventArgs e)
        => UpdateSummary();

    /// <summary>
    /// SelectAll_Click 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in items)
        {
            item.IsSelected = true;
        }

        UpdateSummary();
    }

    /// <summary>
    /// ClearSelection_Click 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void ClearSelection_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in items)
        {
            item.IsSelected = false;
        }

        UpdateSummary();
    }

    /// <summary>
    /// UpdateSummary 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void UpdateSummary()
    {
        var selected = items.Count(item => item.IsSelected);
        SummaryText.Text = $"{items.Count}개 중 {selected}개 선택";
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
            CustomMessageBox.Show("저장 결과 폴더를 찾지 못했습니다.", "측정결과 열기", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{firstFile}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            CustomMessageBox.Show($"폴더를 열지 못했습니다.\n{ex.Message}", "측정결과 열기", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// ResultItemCard_MouseLeftButtonUp 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void ResultItemCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (FindVisualParent<CheckBox>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        if (sender is FrameworkElement { DataContext: SavedResultFileSelectionItem item })
        {
            item.IsSelected = !item.IsSelected;
            UpdateSummary();
        }
    }

    /// <summary>
    /// BuildDisplayPath 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static string BuildDisplayPath(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return filePath;
        }

        var folderName = Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.IsNullOrWhiteSpace(folderName)
            ? filePath
            : $"{folderName}\\{Path.GetFileName(filePath)}";
    }

    /// <summary>
    /// ToReadableSize 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static string ToReadableSize(long bytes)
    {
        if (bytes < 0)
        {
            return string.Empty;
        }
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        var kb = bytes / 1024d;
        if (kb < 1024)
        {
            return $"{kb:0.#} KB";
        }

        var mb = kb / 1024d;
        return $"{mb:0.#} MB";
    }

    /// <summary>
    /// FindVisualParent 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static T? FindVisualParent<T>(DependencyObject? child)
        where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T typed)
            {
                return typed;
            }

            child = VisualTreeHelper.GetParent(child);
        }

        return null;
    }
}

