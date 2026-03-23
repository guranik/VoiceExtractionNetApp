using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;
using Client.Interfaces;
using Client.Networking;
using Client.Utils;
using Common.Models;
using Common.Tcp.Messages;
using Common.Tcp.Utils;
using Microsoft.Win32;

namespace Client.ViewModels;

public class MainViewModel : ObservableObject
{
    private readonly ManagerClient _client = new();
    private readonly IDispatcher _dispatcher;

    private readonly Stopwatch _executionTimer = new();
    private readonly Stopwatch _extractTimer = new();

    private string _selectedFile;
    private string? _outputDir;

    private bool _extractCompleted;

    public bool CanSend =>
        !string.IsNullOrEmpty(_selectedFile) &&
        !string.IsNullOrEmpty(_outputDir) &&
        File.Exists(_selectedFile) &&
        Directory.Exists(_outputDir);

    private string _log = "";
    public string Log
    {
        get => _log;
        set => Set(ref _log, value);
    }

    public ICommand SelectFileCommand { get; }
    public ICommand SelectOutputDirCommand { get; }
    public ICommand SendCommand { get; }

    private double _extractProgress;
    public double ExtractProgress
    {
        get => _extractProgress;
        set => Set(ref _extractProgress, value);
    }

    private double _transcribeProgress;
    public double TranscribeProgress
    {
        get => _transcribeProgress;
        set => Set(ref _transcribeProgress, value);
    }

    public string OutputDir
    {
        get => _outputDir;
        set
        {
            Set(ref _outputDir, value);
            OnPropertyChanged(nameof(CanSend));
        }
    }

    public MainViewModel(IDispatcher dispatcher)
    {
        _dispatcher = dispatcher;

        SelectFileCommand = new RelayCommand(SelectFile);
        SelectOutputDirCommand = new RelayCommand(SelectOutputDir);
        SendCommand = new RelayCommand(async () => await SendAsync());

        _client.OnLog += AppendLog;
        _client.OnProgress += OnProgressReceived;
        _client.OnFileReceived += OnFileReceived;
    }

    private void SelectFile()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "WAV files (*.wav)|*.wav"
        };

        if (dlg.ShowDialog() == true)
        {
            _selectedFile = dlg.FileName;
            AppendLog($"Selected: {_selectedFile}");
            OnPropertyChanged(nameof(CanSend));
        }
    }

    private void SelectOutputDir()
    {
        var dlg = new OpenFileDialog
        {
            CheckFileExists = false,
            CheckPathExists = true,
            FileName = "Select folder",
            Filter = "Folder|*."
        };

        if (dlg.ShowDialog() == true)
        {
            OutputDir = Path.GetDirectoryName(dlg.FileName);
            AppendLog($"Output dir: {OutputDir}");
            OnPropertyChanged(nameof(CanSend));
        }
    }

    private async Task SendAsync()
    {
        if (!File.Exists(_selectedFile))
        {
            AppendLog("Ошибка: входной файл не найден.");
            return;
        }

        if (!Directory.Exists(_outputDir))
        {
            AppendLog("Ошибка: выходная папка не существует.");
            return;
        }

        if (!ValidateWavFile(_selectedFile, out var errorMessage))
        {
            AppendLog($"Ошибка: {errorMessage}");
            return;
        }

        await _client.ConnectAsync();

        var msg = new ClientFileMessage
        {
            File = new FilePayload
            {
                FileName = Path.GetFileName(_selectedFile),
                Base64Content = Base64FileHelper.ReadFileAsBase64(_selectedFile)
            }
        };

        _executionTimer.Reset();
        _executionTimer.Start();

        _extractTimer.Reset();
        _extractTimer.Start();
        _extractCompleted = false;

        AppendLog("Файл отправлен. Запущены таймеры выполнения и экстракции.");

        await _client.SendAsync(msg);
    }

    private bool ValidateWavFile(string filePath, out string errorMessage)
    {
        errorMessage = null;

        try
        {
            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            using var reader = new BinaryReader(fileStream);

            var riff = reader.ReadBytes(4);
            if (System.Text.Encoding.ASCII.GetString(riff) != "RIFF")
            {
                errorMessage = "Файл не является корректным WAV (отсутствует RIFF заголовок).";
                return false;
            }

            fileStream.Seek(8, SeekOrigin.Begin);
            var wave = reader.ReadBytes(4);
            if (System.Text.Encoding.ASCII.GetString(wave) != "WAVE")
            {
                errorMessage = "Файл не является корректным WAV (отсутствует WAVE метка).";
                return false;
            }

            while (fileStream.Position < fileStream.Length)
            {
                var chunkId = reader.ReadBytes(4);
                var chunkSize = reader.ReadInt32();

                var chunkName = System.Text.Encoding.ASCII.GetString(chunkId);
                if (chunkName == "fmt ")
                {
                    var formatTag = reader.ReadInt16();
                    var channels = reader.ReadInt16();
                    var sampleRate = reader.ReadInt32();
                    reader.BaseStream.Seek(chunkSize - 8, SeekOrigin.Current);

                    if (formatTag != 1)
                    {
                        errorMessage = "Поддерживается только PCM-формат WAV.";
                        return false;
                    }

                    if (channels != 1)
                    {
                        errorMessage = "Поддерживается только моно-аудио (1 канал).";
                        return false;
                    }

                    if (sampleRate != 8000 && sampleRate != 16000)
                    {
                        errorMessage = "Поддерживается только частота дискретизации 8 кГц или 16 кГц.";
                        return false;
                    }

                    return true;
                }
                else
                {
                    fileStream.Seek(chunkSize, SeekOrigin.Current);
                }
            }

            errorMessage = "Не найден блок 'fmt ' в WAV-файле.";
            return false;
        }
        catch (Exception ex)
        {
            errorMessage = $"Ошибка при чтении WAV-файла: {ex.Message}";
            return false;
        }
    }

    private void OnProgressReceived(ClientProgressMessage msg)
    {
        _dispatcher.Invoke(() =>
        {
            ExtractProgress = msg.InputFileDuration > 0
                ? Math.Min(
                    1.0,
                    (double)msg.EarliestExtractSegmentStart / msg.InputFileDuration)
                : 0;

            TranscribeProgress = msg.InputFileDuration > 0
                ? Math.Min(
                    1.0,
                    (double)msg.LatestTranscriptionEnd / msg.InputFileDuration)
                : 0;

            if (!_extractCompleted &&
                msg.InputFileDuration > 0 &&
                msg.EarliestExtractSegmentStart >= msg.InputFileDuration)
            {
                _extractCompleted = true;
                _extractTimer.Stop();

                AppendLog("Экстракция завершена");
                AppendLog($"Время экстракции: {_extractTimer.Elapsed}");
            }

        });
    }

    private void OnFileReceived(ClientFileMessage msg)
    {
        _dispatcher.Invoke(() =>
        {
            var path = Path.Combine(OutputDir, msg.File.FileName);
            Base64FileHelper.WriteBase64ToFile(path, msg.File.Base64Content);

            if (_executionTimer.IsRunning)
            {
                _executionTimer.Stop();
                AppendLog($"Общее время выполнения: {_executionTimer.Elapsed}");
            }

            AppendLog($"Result saved: {path}");
        });
    }

    private void AppendLog(string msg)
        => Log += $"[{DateTime.Now:HH:mm:ss}] {msg}\n";
}