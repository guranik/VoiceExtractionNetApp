using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using Client.Networking;
using Client.Utils;
using Common.Messages;
using Common.Models;
using Common.Utils;
using Microsoft.Win32;

namespace Client.ViewModels;

class MainViewModel : ObservableObject
{
    private readonly ManagerClient _client = new();

    private string _selectedFile;
    public bool CanSend => !string.IsNullOrEmpty(_selectedFile);

    public string Log
    {
        get => _log;
        set => Set(ref _log, value);
    }
    private string _log = "";

    public ICommand SelectFileCommand { get; }
    public ICommand SendCommand { get; }

    public MainViewModel()
    {
        SelectFileCommand = new RelayCommand(SelectFile);
        SendCommand = new RelayCommand(async () => await SendAsync());

        _client.OnLog += AppendLog;
        _client.OnTranscription += OnTranscriptionReceived;
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

    private void OnTranscriptionReceived(string name, string text)
    {
        File.WriteAllText($"{name}.txt", text);
        AppendLog($"Transcription saved: {name}.txt");
    }

    private void AppendLog(string msg)
        => Log += $"[{DateTime.Now:HH:mm:ss}] {msg}\n";
}
