using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;
using Client.Interfaces;
using Client.Networking;
using Client.Utils;
using Common.Messages;
using Common.Models;
using Common.Utils;
using Microsoft.Win32;

namespace Client.ViewModels;

public class MainViewModel : ObservableObject
{
    private readonly ManagerClient _client = new();
    private readonly IDispatcher _dispatcher;

    private readonly Stopwatch _executionTimer = new();
    private readonly Stopwatch _extractTimer = new();

    private string _selectedFile;
    private string _outputDir;

    private bool _extractCompleted;

    public bool CanSend =>
        !string.IsNullOrEmpty(_selectedFile) &&
        !string.IsNullOrEmpty(_outputDir);

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
        }
    }

    private async Task SendAsync()
    {
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

            AppendLog(
                $"Progress: extract {ExtractProgress:P0}, transcribe {TranscribeProgress:P0}");
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
