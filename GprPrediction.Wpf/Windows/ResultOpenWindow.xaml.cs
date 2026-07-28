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
//
/// </summary>
public partial class ResultOpenWindow : Window
{
    private readonly MainViewModel viewModel;
    private readonly ObservableCollection<SavedResultFileSelectionItem> items = [];
    private readonly CancellationTokenSource lifetimeCts = new();
    private bool loadedOnce;
    private bool applying;

    /// <summary>
//
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
//
    /// </summary>
    private void MinimizeWindow(object sender, ExecutedRoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    /// <summary>
//
    /// </summary>
    private void MaximizeRestoreWindow(object sender, ExecutedRoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    /// <summary>
//
    /// </summary>
    private void CloseWindow(object sender, ExecutedRoutedEventArgs e)
        => Close();

    /// <summary>
//
    /// </summary>
    private void Close_Click(object sender, RoutedEventArgs e)
        => Close();

    /// <summary>
//
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
        finally
        {
            ResultList.IsEnabled = true;
            LoadingOverlay.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
//
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
        finally
        {
            applying = false;
        }
    }

    /// <summary>
//
    /// </summary>
    private void SelectionChanged_Update(object sender, RoutedEventArgs e)
        => UpdateSummary();

    /// <summary>
//
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
//
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
//
    /// </summary>
    private void UpdateSummary()
    {
        var selected = items.Count(item => item.IsSelected);
        SummaryText.Text = $"{items.Count}개 중 {selected}개 선택";
    }

    /// <summary>
//
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
//
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
//
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
//
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
//
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

