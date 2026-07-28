using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace GprPrediction.Wpf.Services;

/// <summary>
/// Python/Julia 기반 외부 알고리즘을 실행하고 결과 CSV 및 로그를 수집
/// 관련 책임을 한곳에 모아 구조와 수명 경계 명확화
/// </summary>
public sealed class AlgorithmRunner
{
    private const int MaximumCapturedCharactersPerStream = 8 * 1024 * 1024;
    private const int MaximumProgressLineCharacters = 4_000;
    private static readonly TimeSpan PreprocessTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan TdaTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan PredictionTimeout = TimeSpan.FromMinutes(15);
    private const string VendorFolderName = "HBC";
    private const string ProductFolderName = "GPR";
    private const string AlgorithmWorkFolderName = "algorithm-work";
    private const string RuntimeWorkFolderName = ".gpr-runtime";
    private const string TdaWorkFolderName = "tda";
    private const string StageArtifactFolderName = "stage-artifacts";
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
    private static readonly string[] TransientWorkDirectories = ["data", "results", "cropped_data", RuntimeWorkFolderName, TdaWorkFolderName, StageArtifactFolderName, "__pycache__"];

    /// <summary>
    /// 입력 파일과 파라미터를 기준으로 전처리, TDA, 예측 단계를 순차 실행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    public async Task<AlgorithmRunResult> RunAsync(
        AlgorithmRunRequest request,
        CancellationToken cancellationToken,
        IProgress<string>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);
        progress?.Report("1/7단계 입력 파일과 알고리즘 폴더를 확인하는 중...");
        if (!File.Exists(request.ScanFilePath))
        {
            throw new FileNotFoundException("스캔 파일을 찾을 수 없습니다.", request.ScanFilePath);
        }

        if (!Directory.Exists(request.AlgorithmDirectory))
        {
            throw new DirectoryNotFoundException($"알고리즘 폴더를 찾을 수 없습니다. {request.AlgorithmDirectory}");
        }

        progress?.Report($"1/7단계 [입력] {DescribeFile(request.ScanFilePath)}");
        progress?.Report($"1/7단계 [알고리즘 원본] \"{Path.GetFullPath(request.AlgorithmDirectory)}\"");
        progress?.Report(
            $"1/7단계 [요청 설정] 범위 X/Z={request.ScanRangeX:0.###}/{request.ScanRangeY:0.###}m, " +
            $"Scale X/Z={request.XScale:0.###}/{request.YScale:0.###}, " +
            $"Threshold={request.Threshold:0.###}, TDA={(request.UseTda ? $"사용({request.TdaThreshold:0.###})" : "미사용")}");

        var algorithmDirectory = PrepareWritableAlgorithmDirectory(request.AlgorithmDirectory);
        progress?.Report($"2/7단계 [작업 폴더] \"{algorithmDirectory}\"");
        progress?.Report(
            $"2/7단계 [폴더 모드] " +
            $"{(Path.GetFullPath(request.AlgorithmDirectory).Equals(algorithmDirectory, StringComparison.OrdinalIgnoreCase) ? "원본 폴더 직접 사용" : "사용자 쓰기 가능 폴더로 복사")}");
        var stagedScanFilePath = StageInputIfInsideWorkDirectory(request.ScanFilePath, algorithmDirectory);
        var sourceScanFilePath = stagedScanFilePath ?? request.ScanFilePath;
        if (stagedScanFilePath is not null)
        {
            progress?.Report($"2/7단계 [입력 보호] 작업 폴더 내부 원본을 임시 위치로 보존: \"{stagedScanFilePath}\"");
        }

        progress?.Report("2/7단계 이전 실행의 data/results/TDA/진단 산출물을 정리하는 중...");
        CleanTransientWorkDirectories(algorithmDirectory);
        var tdaDirectory = PrepareTdaWorkDirectory(algorithmDirectory);
        var tdaImagePath = Path.Combine(tdaDirectory, "data.png");
        var algorithmEnvironment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["GPR_TDA_DIR"] = tdaDirectory,
            ["GPR_STAGE_ARTIFACT_DIR"] = Path.Combine(algorithmDirectory, StageArtifactFolderName),
            ["GPR_PREPROCESSOR_MODE"] = request.UseTda ? "tda" : "normal"
        };
        progress?.Report(
            $"2/7단계 [환경 변수] GPR_PREPROCESSOR_MODE={algorithmEnvironment["GPR_PREPROCESSOR_MODE"]}, " +
            $"GPR_TDA_DIR=\"{algorithmEnvironment["GPR_TDA_DIR"]}\"");

        var mainStage1FileName = request.UseTda ? "main_1.py" : "main.py";
        var mainStage1 = Path.Combine(algorithmDirectory, mainStage1FileName);
        var mainStage3 = Path.Combine(algorithmDirectory, "main_2.py");
        var tdaScript = Path.Combine(algorithmDirectory, "tda.jl");

        if (!File.Exists(mainStage1) || !File.Exists(mainStage3))
        {
            throw new FileNotFoundException(
                $"알고리즘 폴더에서 {mainStage1FileName} 또는 main_2.py를 찾을 수 없습니다.",
                algorithmDirectory);
        }

        var dataDirectory = Path.Combine(algorithmDirectory, "data");
        Directory.CreateDirectory(dataDirectory);
        Directory.CreateDirectory(Path.Combine(algorithmDirectory, "data", "processed_data"));

        var scanFileName = Path.GetFileName(request.ScanFilePath);
        var algorithmInputPath = Path.Combine(dataDirectory, scanFileName);
        if (!Path.GetFullPath(sourceScanFilePath).Equals(Path.GetFullPath(algorithmInputPath), StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(sourceScanFilePath, algorithmInputPath, overwrite: true);
        }

        // 외부 스크립트가 읽는 텍스트 설정 파일을 실행 직전에 다시 생성
        var inputInfoPath = Path.Combine(dataDirectory, "input_info.txt");
        await File.WriteAllTextAsync(inputInfoPath, BuildInputInfo(request, scanFileName, tdaDirectory), Utf8NoBom, cancellationToken);

        var modelInfoPath = Path.Combine(algorithmDirectory, "model_info.txt");
        var modelInfo = BuildModelInfo(request, algorithmDirectory, scanFileName, tdaDirectory);
        await File.WriteAllTextAsync(modelInfoPath, modelInfo, Utf8NoBom, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(dataDirectory, "model_info.txt"), modelInfo, Utf8NoBom, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(tdaDirectory, "input_info.txt"), BuildInputInfo(request, scanFileName, tdaDirectory), Utf8NoBom, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(tdaDirectory, "model_info.txt"), modelInfo, Utf8NoBom, cancellationToken);
        SnapshotStageArtifacts(algorithmDirectory, tdaDirectory, "after-input");
        var selectedNormalWeights = ReadSettingValue(modelInfo, "normal_weights_file");
        var selectedTdaWeights = ReadSettingValue(modelInfo, "tda_weights_file");
        progress?.Report(
            $"3/7단계 입력 준비 완료: 스캔=\"{algorithmInputPath}\", " +
            $"설정=\"{inputInfoPath}\", 모델설정=\"{modelInfoPath}\"");
        progress?.Report(
            $"3/7단계 [적용값] X Scale={ResolveXScale(request, scanFileName):0.###}, " +
            $"Y Scale={request.YScale:0.###}, Threshold={request.Threshold:0.###}, " +
            $"모델 모드={(request.UseTda ? "TDA" : "일반")}");
        progress?.Report(
            $"3/7단계 [모델] 일반=\"{selectedNormalWeights}\", TDA=\"{selectedTdaWeights}\"");

        var pythonExecutable = PythonRuntimeLocator.Resolve(request.PythonExecutable);
        progress?.Report($"3/7단계 [Python] \"{pythonExecutable}\"");
        var log = new StringBuilder();
        var stageResults = new List<ProcessStageResult>();

        // 1단계: AGC 전처리(DZT/SGY/CSV -> data.jpg, TDA 작업 폴더 복사)
        progress?.Report($"4/7단계 {mainStage1FileName}: 스캔 파일 전처리 중...");
        var stage1 = await RunProcessAsync(
            pythonExecutable,
            $"\"{mainStage1}\"",
            algorithmDirectory,
            PreprocessTimeout,
            cancellationToken,
            progress,
            $"4/7단계 {mainStage1FileName}",
            algorithmEnvironment);
        stageResults.Add(stage1);
        WriteStageLog(algorithmDirectory, $"stage1-{Path.GetFileNameWithoutExtension(mainStage1FileName)}.log", stage1);
        SnapshotStageArtifacts(algorithmDirectory, tdaDirectory, $"after-{Path.GetFileNameWithoutExtension(mainStage1FileName)}");
        AppendStageLog(log, $"1단계 ({mainStage1FileName} - AGC 전처리)", stage1);
        progress?.Report(
            $"4/7단계 [산출물] 전처리 이미지: {DescribeFile(Path.Combine(algorithmDirectory, "data", "processed_data", "data.jpg"))}");

        if (stage1.ExitCode != 0)
        {
            DeleteStagedInput(stagedScanFilePath);
            return BuildResult(stageResults, log, algorithmDirectory, tdaApplied: false);
        }

        // 2단계(선택): Julia TDA 분석
        var tdaApplied = false;
        if (request.UseTda)
        {
            progress?.Report("5/7단계 TDA 실행 환경 확인 중...");
            var juliaExecutable = JuliaRuntimeLocator.Resolve(request.JuliaExecutable);
            if (juliaExecutable is null)
            {
                log.AppendLine("[TDA] Julia 실행 파일을 찾지 못해 TDA 단계를 건너뜁니다. main_2.py는 일반 모델로 자동 전환됩니다.");
            }
            else if (!File.Exists(tdaScript))
            {
                log.AppendLine($"[TDA] tda.jl을 찾을 수 없습니다 ({tdaScript}). TDA 단계를 건너뜁니다.");
            }
            else
            {
                progress?.Report($"5/7단계 [Julia] \"{juliaExecutable}\"");
                progress?.Report($"5/7단계 [스크립트] {DescribeFile(tdaScript)}");
                progress?.Report("5/7단계 tda.jl: TDA 분석 중...");
                var stage2 = await RunProcessAsync(juliaExecutable, $"\"{tdaScript}\"", algorithmDirectory, TdaTimeout, cancellationToken, progress, "5/7단계 tda.jl", algorithmEnvironment);
                stageResults.Add(stage2);
                WriteStageLog(algorithmDirectory, "stage2-tda.log", stage2);
                SnapshotStageArtifacts(algorithmDirectory, tdaDirectory, "after-tda");
                AppendStageLog(log, "2단계 (tda.jl - TDA 분석)", stage2);
                tdaApplied = stage2.ExitCode == 0 && File.Exists(tdaImagePath);
                progress?.Report(
                    $"5/7단계 [산출물] TDA 이미지: {DescribeFile(tdaImagePath)}, " +
                    $"적용 결과={(tdaApplied ? "성공" : "미적용")}");

                if (!tdaApplied)
                {
                    log.AppendLine($"[TDA] {tdaImagePath} 생성이 확인되지 않아 main_2.py는 일반 모델로 자동 전환됩니다.");
                }
            }
        }

        if (request.UseTda && !tdaApplied)
        {
            TryDeleteFile(tdaImagePath);
            log.AppendLine("[TDA] TDA 결과가 생성되지 않아 main_2.py는 일반 모델로 자동 전환됩니다.");
            progress?.Report("5/7단계 TDA 결과 없음: 일반 모델로 전환");
            var fallbackModelInfo = BuildModelInfo(request, algorithmDirectory, scanFileName, tdaDirectory, useTda: false);
            File.WriteAllText(modelInfoPath, fallbackModelInfo, Utf8NoBom);
            File.WriteAllText(Path.Combine(dataDirectory, "model_info.txt"), fallbackModelInfo, Utf8NoBom);
            File.WriteAllText(Path.Combine(tdaDirectory, "model_info.txt"), fallbackModelInfo, Utf8NoBom);
            SnapshotStageArtifacts(algorithmDirectory, tdaDirectory, "after-tda-fallback");
        }

        // 3단계: YOLOv5 예측 + 좌표 변환
        SnapshotStageArtifacts(algorithmDirectory, tdaDirectory, "before-main2");
        progress?.Report("6/7단계 main_2.py: 객체 예측 및 결과 변환 중...");
        var stage3 = await RunProcessAsync(pythonExecutable, $"\"{mainStage3}\"", algorithmDirectory, PredictionTimeout, cancellationToken, progress, "6/7단계 main_2.py", algorithmEnvironment);
        stageResults.Add(stage3);
        WriteStageLog(algorithmDirectory, "stage3-main_2.log", stage3);
        SnapshotStageArtifacts(algorithmDirectory, tdaDirectory, "after-main2");
        AppendStageLog(log, "3단계 (main_2.py - YOLO 예측)", stage3);

        progress?.Report("7/7단계 결과 파일 확인 중...");
        progress?.Report(
            $"7/7단계 [결과 CSV] {DescribeFile(Path.Combine(algorithmDirectory, "results", "prediction_results.csv"))}");
        progress?.Report(
            $"7/7단계 [결과 이미지] {DescribeFile(Path.Combine(algorithmDirectory, "results", "data.jpg"))}");
        progress?.Report(
            $"7/7단계 [진단 로그] \"{Path.Combine(algorithmDirectory, StageArtifactFolderName, "logs")}\"");
        DeleteStagedInput(stagedScanFilePath);
        return BuildResult(stageResults, log, algorithmDirectory, tdaApplied);
    }

    /// <summary>
    /// DescribeFile 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static string DescribeFile(string path)
    {
        if (!File.Exists(path))
        {
            return $"\"{path}\" [파일 없음]";
        }

        var info = new FileInfo(path);
        return $"\"{info.FullName}\" [크기 {FormatFileSize(info.Length)}, 수정 {info.LastWriteTime:yyyy-MM-dd HH:mm:ss}]";
    }

    /// <summary>
    /// FormatFileSize 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static string FormatFileSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var value = (double)Math.Max(0, bytes);
        var unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.##} {units[unitIndex]}";
    }

    /// <summary>
    /// ReadSettingValue 데이터 읽기
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static string ReadSettingValue(string content, string key)
    {
        var prefix = key + ":";
        var line = content
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(candidate => candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        return line is null ? "확인 불가" : line[prefix.Length..].Trim();
    }

    /// <summary>
    /// 설치 폴더가 읽기 전용이면 사용자별 작업 폴더에 알고리즘 파일을 복사
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static string PrepareWritableAlgorithmDirectory(string sourceDirectory)
    {
        var fullSourceDirectory = Path.GetFullPath(sourceDirectory);
        if (CanWriteToDirectory(fullSourceDirectory))
        {
            return fullSourceDirectory;
        }

        var workDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            VendorFolderName,
            ProductFolderName,
            AlgorithmWorkFolderName);

        Directory.CreateDirectory(workDirectory);
        CleanTransientWorkDirectories(workDirectory);
        CopyDirectoryIfNeeded(fullSourceDirectory, workDirectory);
        return workDirectory;
    }

    /// <summary>
    /// StageInputIfInsideWorkDirectory 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static string? StageInputIfInsideWorkDirectory(string scanFilePath, string algorithmDirectory)
    {
        if (!IsPathUnderDirectory(scanFilePath, algorithmDirectory))
        {
            return null;
        }

        var stagedPath = Path.Combine(
            Path.GetTempPath(),
            $"gpr-input-{Guid.NewGuid():N}{Path.GetExtension(scanFilePath)}");
        File.Copy(scanFilePath, stagedPath, overwrite: true);
        return stagedPath;
    }

    /// <summary>
    /// IsPathUnderDirectory 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static bool IsPathUnderDirectory(string filePath, string directoryPath)
    {
        var fullFilePath = Path.GetFullPath(filePath);
        var fullDirectoryPath = Path.GetFullPath(directoryPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        return fullFilePath.StartsWith(fullDirectoryPath, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// DeleteStagedInput 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static void DeleteStagedInput(string? stagedScanFilePath)
    {
        if (string.IsNullOrWhiteSpace(stagedScanFilePath) || !File.Exists(stagedScanFilePath))
        {
            return;
        }

        try { File.Delete(stagedScanFilePath); } catch { }
    }

    /// <summary>
    /// TryDeleteFile 처리 가능 여부 확인
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static void TryDeleteFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return;
        }

        try { File.Delete(filePath); } catch { }
    }

    /// <summary>
    /// 폴더에 임시 파일 생성/삭제가 가능한지 검사
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static bool CanWriteToDirectory(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var probePath = Path.Combine(directory, $".write-test-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probePath, string.Empty, Utf8NoBom);
            File.Delete(probePath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 이전 실행에서 남은 입력/결과 폴더를 제거해 새 분석과 섞이지 않게
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static void CleanTransientWorkDirectories(string workDirectory)
    {
        foreach (var directoryName in TransientWorkDirectories)
        {
            var path = Path.Combine(workDirectory, directoryName);
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
    }

    /// <summary>
    /// PrepareTdaWorkDirectory 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static string PrepareTdaWorkDirectory(string workDirectory)
    {
        var tdaDirectory = Path.Combine(workDirectory, RuntimeWorkFolderName, TdaWorkFolderName);
        Directory.CreateDirectory(tdaDirectory);
        return tdaDirectory;
    }

    /// <summary>
    /// 알고리즘 원본 파일을 작업 폴더로 복사하되 변경된 파일만 갱신
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static void CopyDirectoryIfNeeded(string sourceDirectory, string targetDirectory)
    {
        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativeDirectory = Path.GetRelativePath(sourceDirectory, directory);
            if (ShouldSkipRelativePath(relativeDirectory))
            {
                continue;
            }

            Directory.CreateDirectory(Path.Combine(targetDirectory, relativeDirectory));
        }

        foreach (var sourceFile in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativeFile = Path.GetRelativePath(sourceDirectory, sourceFile);
            if (ShouldSkipRelativePath(relativeFile))
            {
                continue;
            }

            var targetFile = Path.Combine(targetDirectory, relativeFile);
            var targetParent = Path.GetDirectoryName(targetFile);
            if (!string.IsNullOrWhiteSpace(targetParent))
            {
                Directory.CreateDirectory(targetParent);
            }

            if (File.Exists(targetFile))
            {
                var sourceInfo = new FileInfo(sourceFile);
                var targetInfo = new FileInfo(targetFile);
                if (sourceInfo.Length == targetInfo.Length &&
                    sourceInfo.LastWriteTimeUtc <= targetInfo.LastWriteTimeUtc.AddSeconds(1))
                {
                    continue;
                }
            }

            File.Copy(sourceFile, targetFile, overwrite: true);
        }
    }

    /// <summary>
    /// 입력/결과 같은 실행 산출물 경로를 복사 대상에서 제외
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static bool ShouldSkipRelativePath(string relativePath)
    {
        var firstSegment = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
        return TransientWorkDirectories.Contains(firstSegment, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 마지막 단계 결과를 기준으로 앱 내부 표준 결과 객체를 생성
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static AlgorithmRunResult BuildResult(
        List<ProcessStageResult> stageResults,
        StringBuilder log,
        string algorithmDirectory,
        bool tdaApplied)
    {
        var resultCsvPath = Path.Combine(algorithmDirectory, "results", "prediction_results.csv");
        var resultImagePath = Path.Combine(algorithmDirectory, "results", "data.jpg");
        var inputInfoPath = Path.Combine(algorithmDirectory, "data", "input_info.txt");
        var finalExitCode = stageResults[^1].ExitCode;

        return new AlgorithmRunResult(
            finalExitCode,
            stageResults[^1].ScriptPath,
            algorithmDirectory,
            inputInfoPath,
            resultCsvPath,
            resultImagePath,
            log.ToString(),
            string.Empty,
            tdaApplied);
    }

    /// <summary>
    /// 각 단계의 stdout/stderr와 종료 코드를 사람 읽기 좋은 로그 형식으로 누적
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static void AppendStageLog(StringBuilder log, string stageName, ProcessStageResult stage)
    {
        log.AppendLine($"=== {stageName} ===");
        log.AppendLine($"script: {stage.ScriptPath}");
        if (!string.IsNullOrWhiteSpace(stage.StandardOutput))
        {
            log.AppendLine(stage.StandardOutput.TrimEnd());
        }

        if (!string.IsNullOrWhiteSpace(stage.StandardError))
        {
            log.AppendLine(stage.StandardError.TrimEnd());
        }

        log.AppendLine($"exit code: {stage.ExitCode}");
        log.AppendLine();
    }

    /// <summary>
    /// 외부 프로세스를 실행하고 표준 출력, 표준 오류, 종료 상태를 모두 수집
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static void WriteStageLog(string algorithmDirectory, string fileName, ProcessStageResult stage)
    {
        try
        {
            var logDirectory = Path.Combine(algorithmDirectory, StageArtifactFolderName, "logs");
            Directory.CreateDirectory(logDirectory);

            var log = new StringBuilder();
            AppendStageLog(log, fileName, stage);
            File.WriteAllText(Path.Combine(logDirectory, fileName), log.ToString(), Utf8NoBom);
        }
        catch
        {
            // Logging must not make analysis fail.
        }
    }

    /// <summary>
    /// SnapshotStageArtifacts 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static void SnapshotStageArtifacts(string algorithmDirectory, string tdaDirectory, string label)
    {
        try
        {
            var targetDirectory = Path.Combine(algorithmDirectory, StageArtifactFolderName, label);
            Directory.CreateDirectory(targetDirectory);

            CopyArtifactIfExists(Path.Combine(algorithmDirectory, "model_info.txt"), Path.Combine(targetDirectory, "model_info.txt"));
            CopyArtifactIfExists(Path.Combine(algorithmDirectory, "data.jpg"), Path.Combine(targetDirectory, "root_data.jpg"));
            CopyArtifactIfExists(Path.Combine(algorithmDirectory, "data", "input_info.txt"), Path.Combine(targetDirectory, "input_info.txt"));
            CopyArtifactIfExists(Path.Combine(algorithmDirectory, "data", "model_info.txt"), Path.Combine(targetDirectory, "data_model_info.txt"));
            CopyArtifactIfExists(Path.Combine(algorithmDirectory, "data", "processed_data", "data.jpg"), Path.Combine(targetDirectory, "processed_data.jpg"));
            CopyArtifactIfExists(Path.Combine(algorithmDirectory, "data", "processed_data", "data.png"), Path.Combine(targetDirectory, "processed_data.png"));
            CopyArtifactIfExists(Path.Combine(algorithmDirectory, "results", "data.jpg"), Path.Combine(targetDirectory, "result_data.jpg"));
            CopyArtifactIfExists(Path.Combine(algorithmDirectory, "results", "prediction_results.csv"), Path.Combine(targetDirectory, "prediction_results.csv"));
            CopyArtifactIfExists(Path.Combine(tdaDirectory, "data.jpg"), Path.Combine(targetDirectory, "tda_data.jpg"));
            CopyArtifactIfExists(Path.Combine(tdaDirectory, "data.png"), Path.Combine(targetDirectory, "tda_data.png"));
            CopyArtifactIfExists(Path.Combine(tdaDirectory, "input_info.txt"), Path.Combine(targetDirectory, "tda_input_info.txt"));
            CopyArtifactIfExists(Path.Combine(tdaDirectory, "model_info.txt"), Path.Combine(targetDirectory, "tda_model_info.txt"));
        }
        catch
        {
            // Artifact capture is diagnostic only.
        }
    }

    /// <summary>
    /// CopyArtifactIfExists 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static void CopyArtifactIfExists(string sourcePath, string targetPath)
    {
        if (!File.Exists(sourcePath))
        {
            return;
        }

        var parent = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        File.Copy(sourcePath, targetPath, overwrite: true);
    }

    /// <summary>
    /// RunProcessAsync 작업 실행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static async Task<ProcessStageResult> RunProcessAsync(
        string fileName,
        string arguments,
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        IProgress<string>? progress = null,
        string stageLabel = "알고리즘",
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        if (environment is not null)
        {
            foreach (var pair in environment)
            {
                startInfo.EnvironmentVariables[pair.Key] = pair.Value;
            }
        }

        // 번들 Python은 격리 모드라 현재 폴더가 import 경로에 자동으로 들어가지 않는다.
        // 알고리즘 폴더를 직접 넘겨 data_agc.py, detect.py 같은 보조 모듈을 찾게 한다.
        var existingPythonPath = startInfo.EnvironmentVariables.ContainsKey("PYTHONPATH")
            ? startInfo.EnvironmentVariables["PYTHONPATH"]
            : null;
        startInfo.EnvironmentVariables["PYTHONPATH"] = string.IsNullOrWhiteSpace(existingPythonPath)
            ? workingDirectory
            : workingDirectory + Path.PathSeparator + existingPythonPath;
        startInfo.EnvironmentVariables["PYTHONUTF8"] = "1";
        startInfo.EnvironmentVariables["PYTHONUNBUFFERED"] = "1";
        startInfo.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();
        var outputClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var errorClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                outputClosed.TrySetResult();
                return;
            }

            lock (outputBuilder)
            {
                AppendBoundedOutput(outputBuilder, e.Data);
            }

            var line = e.Data.Trim();
            if (!string.IsNullOrWhiteSpace(line))
            {
                progress?.Report($"{stageLabel} [stdout] {ShortenProgressLine(line)}");
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                errorClosed.TrySetResult();
                return;
            }

            lock (errorBuilder)
            {
                AppendBoundedOutput(errorBuilder, e.Data);
            }

            var line = e.Data.Trim();
            if (!string.IsNullOrWhiteSpace(line))
            {
                progress?.Report($"{stageLabel} [stderr] {ShortenProgressLine(line)}");
            }
        };

        progress?.Report(
            $"{stageLabel} [실행] 파일=\"{fileName}\" 인수=\"{arguments}\" " +
            $"작업폴더=\"{workingDirectory}\" 제한시간={timeout.TotalMinutes:0}분");
        if (!process.Start())
        {
            throw new InvalidOperationException($"프로세스를 시작하지 못했습니다. {fileName}");
        }
        progress?.Report($"{stageLabel} [프로세스] PID={process.Id} 시작됨");
        // 출력 읽기와 종료 대기를 동시에 수행해 표준 출력 버퍼 교착을 방지
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token);
        var startedAt = DateTimeOffset.Now;
        var heartbeatTask = ReportHeartbeatAsync(stageLabel, startedAt, progress, heartbeatCts.Token);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            KillProcessTree(process);
            await WaitForKilledProcessAsync(process);
            throw;
        }
        catch (OperationCanceledException)
        {
            KillProcessTree(process);
            await WaitForKilledProcessAsync(process);
            await WaitForOutputFlushAsync(outputClosed.Task, errorClosed.Task);
            var timeoutOutput = outputBuilder.ToString();
            var timeoutError = errorBuilder.ToString();
            var timeoutMessage = $"Process timed out after {timeout.TotalMinutes:0} minutes and was terminated.";
            var combinedError = string.IsNullOrWhiteSpace(timeoutError)
                ? timeoutMessage
                : timeoutError.TrimEnd() + Environment.NewLine + timeoutMessage;
            return new ProcessStageResult(fileName + " " + arguments, 124, timeoutOutput, combinedError);
        }
        finally
        {
            heartbeatCts.Cancel();
            await IgnoreCancellationAsync(heartbeatTask);
        }

        await WaitForOutputFlushAsync(outputClosed.Task, errorClosed.Task);
        var standardOutput = outputBuilder.ToString();
        var standardError = errorBuilder.ToString();
        var elapsed = DateTimeOffset.Now - startedAt;
        progress?.Report(
            $"{stageLabel} [종료] exit code={process.ExitCode}, " +
            $"소요={elapsed:hh\\:mm\\:ss}, stdout={CountOutputLines(standardOutput)}줄, " +
            $"stderr={CountOutputLines(standardError)}줄");

        return new ProcessStageResult(fileName + " " + arguments, process.ExitCode, standardOutput, standardError);
    }

    /// <summary>
    /// CountOutputLines 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static int CountOutputLines(string output) =>
        string.IsNullOrEmpty(output)
            ? 0
            : output.Count(character => character == '\n') +
              (output.EndsWith('\n') ? 0 : 1);

    /// <summary>
    /// ShortenProgressLine 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static string ShortenProgressLine(string line) =>
        line.Length <= MaximumProgressLineCharacters
            ? line
            : line[..MaximumProgressLineCharacters] + "… [잘림]";

    /// <summary>
    /// AppendBoundedOutput 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static void AppendBoundedOutput(StringBuilder builder, string line)
    {
        if (builder.Length >= MaximumCapturedCharactersPerStream)
        {
            return;
        }

        var remaining = MaximumCapturedCharactersPerStream - builder.Length;
        if (line.Length + Environment.NewLine.Length <= remaining)
        {
            builder.AppendLine(line);
            return;
        }

        builder.Append(line.AsSpan(0, Math.Max(0, remaining - 32)));
        builder.AppendLine("… [출력 크기 제한으로 잘림]");
    }

    /// <summary>
    /// WaitForOutputFlushAsync 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static async Task WaitForOutputFlushAsync(Task outputClosed, Task errorClosed)
    {
        var flushTask = Task.WhenAll(outputClosed, errorClosed);
        var completed = await Task.WhenAny(flushTask, Task.Delay(TimeSpan.FromSeconds(2)));
        if (completed == flushTask)
        {
            await flushTask;
        }
    }

    /// <summary>
    /// ReportHeartbeatAsync 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static async Task ReportHeartbeatAsync(
        string stageLabel,
        DateTimeOffset startedAt,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        if (progress is null)
        {
            return;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            var elapsed = DateTimeOffset.Now - startedAt;
            progress.Report($"{stageLabel}: 진행 중... 경과 {elapsed:hh\\:mm\\:ss}");
        }
    }

    /// <summary>
    /// IgnoreCancellationAsync 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static async Task IgnoreCancellationAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>
    /// KillProcessTree 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
    }

    /// <summary>
    /// WaitForKilledProcessAsync 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static async Task WaitForKilledProcessAsync(Process process)
    {
        try
        {
            using var killWaitCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await process.WaitForExitAsync(killWaitCts.Token);
        }
        catch
        {
        }
    }

    /// <summary>
    /// 전처리 스크립트가 읽는 input_info.txt 내용을 구성
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static string BuildInputInfo(AlgorithmRunRequest request, string scanFileName, string tdaDirectory)
    {
        var culture = CultureInfo.InvariantCulture;
        var xScale = ResolveXScale(request, scanFileName);

        return string.Join(
            Environment.NewLine,
            $"file_name: {scanFileName}",
            $"scan range x: {request.ScanRangeX.ToString(culture)}",
            $"scan range y: {request.ScanRangeY.ToString(culture)}",
            $"x scale: {xScale.ToString(culture)}",
            $"y scale: {request.YScale.ToString(culture)}",
            $"threshold: {request.Threshold.ToString(culture)}",
            $"model_mode: {(request.UseTda ? "tda" : "normal")}",
            $"use_tda: {(request.UseTda ? "true" : "false")}",
            $"tda_threshold: {request.TdaThreshold.ToString(culture)}",
            $"tda_dir: {tdaDirectory}",
            string.Empty);
    }

    /// <summary>
    /// main_2.py가 읽는 모델 선택 설정 파일 내용을 구성
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static string BuildModelInfo(AlgorithmRunRequest request, string algorithmDirectory, string scanFileName, string tdaDirectory, bool? useTda = null)
    {
        var culture = CultureInfo.InvariantCulture;
        var effectiveUseTda = useTda ?? request.UseTda;
        var tdaThreshold = request.TdaThreshold;
        var normalWeightsFile = ResolvePreferredWeightFileName(
            algorithmDirectory,
            "Best_V1.260522.pt",
            "Fine_tuned_V_best.pt",
            "best.pt");
        var tdaWeightsFile = ResolvePreferredWeightFileName(
            algorithmDirectory,
            "Best_Tda_V1.260522.pt",
            "Fine_tuned_VI_tda_best.pt",
            normalWeightsFile);

        return string.Join(
            Environment.NewLine,
            $"model_mode: {(effectiveUseTda ? "tda" : "normal")}",
            $"tda_threshold: {tdaThreshold.ToString(culture)}",
            $"normal_weights_file: ./{normalWeightsFile}",
            $"tda_weights_file: ./{tdaWeightsFile}",
            string.Empty);
    }

    /// <summary>
    /// ResolveXScale 실행 값 결정
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static double ResolveXScale(AlgorithmRunRequest request, string scanFileName)
    {
        var name = Path.GetFileNameWithoutExtension(scanFileName).ToUpperInvariant();
        var isAb = ContainsToken(name, "AB");
        var isCd = ContainsToken(name, "CD");
        var isG = ContainsSeriesToken(name, "G");
        var isH = ContainsSeriesToken(name, "H");

        if (isAb && isH)
        {
            return 26;
        }

        if (isAb && isG)
        {
            return 6;
        }

        if (isCd && isH)
        {
            return 4;
        }

        if (isCd && isG)
        {
            return 13;
        }

        if (TryResolveRawXScale(name, out var rawXScale))
        {
            return rawXScale;
        }

        return request.XScale;
    }

    /// <summary>
    /// TryResolveRawXScale 처리 가능 여부 확인
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static bool TryResolveRawXScale(string name, out double xScale)
    {
        if (Regex.IsMatch(name, @"(^|[^A-Z0-9])H[-_ ]?\d+(?:[-_ ]\d+)?\s*\(?500\)?([^A-Z0-9]|$)"))
        {
            xScale = 26;
            return true;
        }

        if (Regex.IsMatch(name, @"(^|[^A-Z0-9])H[-_ ]?\d+[A-Z]?([^A-Z0-9]|$)"))
        {
            xScale = 4;
            return true;
        }

        xScale = 0;
        return false;
    }

    /// <summary>
    /// ContainsToken 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static bool ContainsToken(string name, string token)
    {
        return Regex.IsMatch(name, $@"(^|[^A-Z0-9]){Regex.Escape(token)}([^A-Z0-9]|$)");
    }

    /// <summary>
    /// ContainsSeriesToken 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static bool ContainsSeriesToken(string name, string token)
    {
        return Regex.IsMatch(name, $@"(^|[^A-Z0-9]){Regex.Escape(token)}([0-9]|$|[^A-Z0-9])");
    }

    /// <summary>
    /// ValidateRequest 입력 유효성 검증
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static void ValidateRequest(AlgorithmRunRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ScanFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.AlgorithmDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PythonExecutable);

        if (!new[]
            {
                request.ScanRangeX,
                request.ScanRangeY,
                request.XScale,
                request.YScale,
                request.Threshold,
                request.TdaThreshold
            }.All(double.IsFinite))
        {
            throw new ArgumentOutOfRangeException(nameof(request), "알고리즘 입력값은 유한한 숫자여야 합니다.");
        }

        if (request.ScanRangeX <= 0 ||
            request.ScanRangeY <= 0 ||
            request.XScale <= 0 ||
            request.YScale <= 0 ||
            request.Threshold is < 0 or > 1 ||
            request.TdaThreshold is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "범위·스케일·Threshold 값이 허용 범위를 벗어났습니다.");
        }
    }

    /// <summary>
    /// 가중치 파일 후보 목록을 순서대로 확인해 실제 존재하는 파일명을 선택
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static string ResolvePreferredWeightFileName(string algorithmDirectory, params string[] candidates)
    {
        foreach (var candidate in candidates.Where(static candidate => !string.IsNullOrWhiteSpace(candidate)))
        {
            if (File.Exists(Path.Combine(algorithmDirectory, candidate)))
            {
                return candidate;
            }
        }

        return candidates.FirstOrDefault(static candidate => !string.IsNullOrWhiteSpace(candidate))
            ?? "best.pt";
    }

    /// <summary>
    /// 외부 프로세스 1회 실행 결과를 담는 내부 전용 레코드
    /// 관련 책임을 한곳에 모아 구조와 수명 경계 명확화
    /// </summary>
    private sealed record ProcessStageResult(string ScriptPath, int ExitCode, string StandardOutput, string StandardError);
}

/// <summary>
/// 외부 알고리즘 실행에 필요한 입력값 집합
/// 관련 책임을 한곳에 모아 구조와 수명 경계 명확화
/// </summary>
public sealed record AlgorithmRunRequest(
    string ScanFilePath,
    string AlgorithmDirectory,
    string PythonExecutable,
    double ScanRangeX,
    double ScanRangeY,
    double XScale,
    double YScale,
    double Threshold,
    bool UseTda = false,
    double TdaThreshold = 0.35,
    string? JuliaExecutable = null);

/// <summary>
/// 외부 알고리즘 실행 후 앱이 소비할 결과 정보 집합
/// 관련 책임을 한곳에 모아 구조와 수명 경계 명확화
/// </summary>
public sealed record AlgorithmRunResult(
    int ExitCode,
    string ScriptPath,
    string AlgorithmDirectory,
    string InputInfoPath,
    string ResultCsvPath,
    string ResultImagePath,
    string StandardOutput,
    string StandardError,
    bool TdaApplied = false);
