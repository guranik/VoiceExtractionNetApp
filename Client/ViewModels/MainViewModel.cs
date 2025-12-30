using System;
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

    private string _selectedFile;
    public bool CanSend => !string.IsNullOrEmpty(_selectedFile);

    private string _log = "";
    public string Log
    {
        get => _log;
        set => Set(ref _log, value);
    }

    public ICommand SelectFileCommand { get; }
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

    private readonly IDispatcher _dispatcher;

    public MainViewModel(IDispatcher dispatcher)
    {
        _dispatcher = dispatcher;

        SelectFileCommand = new RelayCommand(SelectFile);
        SendCommand = new RelayCommand(async () => await SendAsync());

        _client.OnLog += AppendLog;
        _client.OnProgress += OnProgressReceived;
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

    private async Task SendAsync()
    {
        await _client.ConnectAsync();

        var msg = new ClientInputMessage
        {
            File = new FilePayload
            {
                FileName = Path.GetFileName(_selectedFile),
                Base64Content = Base64FileHelper.ReadFileAsBase64(_selectedFile)
            }
        };

        await _client.SendAsync(msg);
        AppendLog("File sent to manager");
    }

    private void OnProgressReceived(ClientProgressMessage msg)
    {
        _dispatcher.Invoke(() =>
        {
            ExtractProgress = msg.InputFileDuration > 0
                ? (double)msg.LatestExtractSegmenStart / msg.InputFileDuration
                : 0;

            TranscribeProgress = msg.TotalTranscribeSegments > 0
                ? (double)msg.TotalTranscriptions / msg.TotalTranscribeSegments
                : 0;

            AppendLog(
                $"Progress: extract {ExtractProgress:P0}, transcribe {TranscribeProgress:P0}");
        });
    }

    private void AppendLog(string msg)
        => Log += $"[{DateTime.Now:HH:mm:ss}] {msg}\n";
}
