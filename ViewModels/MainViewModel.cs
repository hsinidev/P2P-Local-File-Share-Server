using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using P2PLocalFileShareServer.Models;
using P2PLocalFileShareServer.Services;

namespace P2PLocalFileShareServer.ViewModels
{
    public partial class LogEntryViewModel
    {
        public string Message { get; set; } = string.Empty;
        public string Level { get; set; } = "INFO"; // INFO, WARNING, DANGER, SUCCESS, DOWNLOAD, UPLOAD
        public string FormattedTime { get; set; } = DateTime.Now.ToString("HH:mm:ss");

        public string ColorHex => Level switch
        {
            "SUCCESS" => "#10B981", // Emerald
            "DOWNLOAD" => "#06B6D4", // Cyan
            "UPLOAD" => "#3B82F6", // Blue
            "WARNING" => "#F59E0B", // Amber
            "DANGER" => "#EF4444", // Red
            "ERROR" => "#EF4444",
            _ => "#94A3B8" // Slate text
        };
    }

    public partial class MainViewModel : ObservableObject
    {
        private readonly NetworkDiscoveryService _networkDiscoveryService;
        private readonly QrCodeGeneratorService _qrCodeGeneratorService;
        private readonly KestrelServerService _kestrelServerService;

        [ObservableProperty]
        private bool _isServerRunning;

        [ObservableProperty]
        private string _serverStatusText = "Server Stopped";

        [ObservableProperty]
        private ObservableCollection<NetworkAdapterInfo> _networkAdapters = new();

        [ObservableProperty]
        private NetworkAdapterInfo? _selectedAdapter;

        [ObservableProperty]
        private int _port = 8080;

        [ObservableProperty]
        private string _pinCode = "1234";

        [ObservableProperty]
        private bool _isPinRequired = true;

        [ObservableProperty]
        private string _serverUrl = "http://127.0.0.1:8080";

        [ObservableProperty]
        private BitmapSource? _qrCodeImage;

        [ObservableProperty]
        private string _uploadDestinationFolder = string.Empty;

        [ObservableProperty]
        private double _currentDownloadSpeedMbps;

        [ObservableProperty]
        private double _currentUploadSpeedMbps;

        [ObservableProperty]
        private long _totalBytesSent;

        [ObservableProperty]
        private long _totalBytesReceived;

        [ObservableProperty]
        private int _activeSessionCount;

        public ObservableCollection<SharedFileItem> SharedFiles { get; } = new();
        public ObservableCollection<ActiveSession> ActiveSessions { get; } = new();
        public ObservableCollection<LogEntryViewModel> Logs { get; } = new();

        public MainViewModel()
        {
            _networkDiscoveryService = new NetworkDiscoveryService();
            _qrCodeGeneratorService = new QrCodeGeneratorService();
            _kestrelServerService = new KestrelServerService();

            _kestrelServerService.Configure(
                getSharedFilesFunc: () => SharedFiles.ToList(),
                onFileUploadedAction: (file) =>
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (!SharedFiles.Any(f => f.FilePath.Equals(file.FilePath, StringComparison.OrdinalIgnoreCase)))
                        {
                            SharedFiles.Add(file);
                        }
                    });
                }
            );

            string defaultUploadDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "P2P_Received");
            UploadDestinationFolder = defaultUploadDir;

            RefreshAdapters();
            UpdateQrCode();

            _ = ListenToTelemetryEventsAsync();
        }

        [RelayCommand]
        public void RefreshAdapters()
        {
            var adapters = _networkDiscoveryService.GetActiveIPv4Adapters();
            NetworkAdapters.Clear();
            foreach (var adapter in adapters)
            {
                NetworkAdapters.Add(adapter);
            }

            SelectedAdapter = NetworkAdapters.FirstOrDefault();
        }

        partial void OnSelectedAdapterChanged(NetworkAdapterInfo? value)
        {
            UpdateServerUrl();
        }

        partial void OnPortChanged(int value)
        {
            UpdateServerUrl();
        }

        private void UpdateServerUrl()
        {
            string ip = SelectedAdapter?.IpAddress ?? "127.0.0.1";
            if (ip == "0.0.0.0") ip = _networkDiscoveryService.GetPrimaryLocalIp();

            ServerUrl = $"http://{ip}:{Port}";
            UpdateQrCode();
        }

        private void UpdateQrCode()
        {
            try
            {
                QrCodeImage = _qrCodeGeneratorService.GenerateQrCode(ServerUrl);
            }
            catch (Exception ex)
            {
                AddLog($"QR Generation error: {ex.Message}", "ERROR");
            }
        }

        [RelayCommand]
        public async Task ToggleServerAsync()
        {
            if (IsServerRunning)
            {
                await StopServerAsync();
            }
            else
            {
                await StartServerAsync();
            }
        }

        [RelayCommand]
        public async Task StartServerAsync()
        {
            try
            {
                string ip = SelectedAdapter?.IpAddress ?? "0.0.0.0";
                await _kestrelServerService.StartAsync(ip, Port, PinCode, IsPinRequired, UploadDestinationFolder);

                IsServerRunning = true;
                ServerStatusText = $"Running on {ServerUrl}";
                AddLog($"P2P Server started successfully on {ServerUrl}", "SUCCESS");
            }
            catch (Exception ex)
            {
                IsServerRunning = false;
                ServerStatusText = "Server Error";
                AddLog($"Failed to start server: {ex.Message}", "ERROR");
                MessageBox.Show($"Server launch error: {ex.Message}\nCheck port availability.", "Server Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        public async Task StopServerAsync()
        {
            try
            {
                await _kestrelServerService.StopAsync();
                IsServerRunning = false;
                ServerStatusText = "Server Stopped";
                CurrentDownloadSpeedMbps = 0;
                CurrentUploadSpeedMbps = 0;
                AddLog("P2P Server stopped.", "INFO");
            }
            catch (Exception ex)
            {
                AddLog($"Error stopping server: {ex.Message}", "ERROR");
            }
        }

        [RelayCommand]
        public void AddSharedFiles()
        {
            var dialog = new OpenFileDialog
            {
                Multiselect = true,
                Title = "Select Files to Share on LAN"
            };

            if (dialog.ShowDialog() == true)
            {
                foreach (string file in dialog.FileNames)
                {
                    if (File.Exists(file) && !SharedFiles.Any(f => f.FilePath.Equals(file, StringComparison.OrdinalIgnoreCase)))
                    {
                        var info = new FileInfo(file);
                        SharedFiles.Add(new SharedFileItem
                        {
                            FileName = info.Name,
                            FilePath = info.FullName,
                            FileSizeBytes = info.Length
                        });

                        AddLog($"Added '{info.Name}' ({SharedFileItem.FormatBytes(info.Length)}) to share list", "INFO");
                    }
                }
            }
        }

        [RelayCommand]
        public void RemoveSharedFile(SharedFileItem file)
        {
            if (file != null && SharedFiles.Contains(file))
            {
                SharedFiles.Remove(file);
                AddLog($"Removed '{file.FileName}' from share list", "INFO");
            }
        }

        [RelayCommand]
        public void BrowseUploadFolder()
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Select Destination Directory for Mobile Uploads"
            };

            if (dialog.ShowDialog() == true)
            {
                UploadDestinationFolder = dialog.FolderName;
                AddLog($"Updated upload destination to '{UploadDestinationFolder}'", "INFO");
            }
        }

        [RelayCommand]
        public void GenerateNewPin()
        {
            var random = new Random();
            PinCode = random.Next(1000, 9999).ToString();
            AddLog($"Generated new Security PIN: {PinCode}", "WARNING");
        }

        [RelayCommand]
        public void CopyServerUrl()
        {
            try
            {
                Clipboard.SetText(ServerUrl);
                AddLog($"Copied Server URL '{ServerUrl}' to clipboard", "SUCCESS");
            }
            catch (Exception ex)
            {
                AddLog($"Clipboard error: {ex.Message}", "ERROR");
            }
        }

        private async Task ListenToTelemetryEventsAsync()
        {
            var reader = _kestrelServerService.EventChannel.Reader;
            while (await reader.WaitToReadAsync())
            {
                while (reader.TryRead(out var evt))
                {
                    await Application.Current.Dispatcher.InvokeAsync(() => ProcessServerEvent(evt));
                }
            }
        }

        private void ProcessServerEvent(ServerEvent evt)
        {
            switch (evt)
            {
                case LogEvent logEvt:
                    AddLog(logEvt.Message, logEvt.Level);
                    break;

                case ClientConnectedEvent connEvt:
                    var session = ActiveSessions.FirstOrDefault(s => s.SessionId == connEvt.SessionId);
                    if (session == null)
                    {
                        session = new ActiveSession
                        {
                            SessionId = connEvt.SessionId,
                            ClientIp = connEvt.ClientIp,
                            UserAgent = connEvt.UserAgent,
                            ActionType = connEvt.ActionType,
                            FileName = connEvt.FileName
                        };
                        ActiveSessions.Insert(0, session);
                    }
                    ActiveSessionCount = ActiveSessions.Count(s => s.Status == "ACTIVE");
                    break;

                case TransferProgressEvent progressEvt:
                    var activeSession = ActiveSessions.FirstOrDefault(s => s.SessionId == progressEvt.SessionId);
                    if (activeSession != null)
                    {
                        activeSession.BytesTransferred = progressEvt.BytesTransferred;
                        activeSession.TotalBytes = progressEvt.TotalBytes;
                        activeSession.SpeedMbps = progressEvt.CurrentSpeedMbps;
                        activeSession.ProgressPercentage = progressEvt.TotalBytes > 0 ? (progressEvt.BytesTransferred * 100.0 / progressEvt.TotalBytes) : 0;
                    }

                    if (progressEvt.ActionType == "DOWNLOAD")
                    {
                        CurrentDownloadSpeedMbps = progressEvt.CurrentSpeedMbps;
                        TotalBytesSent += 64 * 1024;
                    }
                    else
                    {
                        CurrentUploadSpeedMbps = progressEvt.CurrentSpeedMbps;
                        TotalBytesReceived += 64 * 1024;
                    }
                    break;

                case TransferCompletedEvent completedEvt:
                    var compSession = ActiveSessions.FirstOrDefault(s => s.SessionId == completedEvt.SessionId);
                    if (compSession != null)
                    {
                        compSession.Status = "COMPLETED";
                        compSession.ProgressPercentage = 100;
                    }

                    CurrentDownloadSpeedMbps = 0;
                    CurrentUploadSpeedMbps = 0;
                    ActiveSessionCount = ActiveSessions.Count(s => s.Status == "ACTIVE");
                    break;

                case SecurityAlertEvent alertEvt:
                    AddLog($"[SECURITY ALERT] {alertEvt.ClientIp}: {alertEvt.Message}", alertEvt.Severity);
                    break;
            }
        }

        private void AddLog(string message, string level)
        {
            Logs.Insert(0, new LogEntryViewModel
            {
                Message = message,
                Level = level,
                FormattedTime = DateTime.Now.ToString("HH:mm:ss")
            });

            // Keep max 200 log items
            while (Logs.Count > 200) Logs.RemoveAt(Logs.Count - 1);
        }
    }
}
