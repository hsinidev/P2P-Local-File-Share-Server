using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace P2PLocalFileShareServer.Models
{
    public partial class ActiveSession : ObservableObject
    {
        public string SessionId { get; set; } = string.Empty;
        public string ClientIp { get; set; } = string.Empty;
        public string UserAgent { get; set; } = string.Empty;
        public string ActionType { get; set; } = "DOWNLOAD"; // "DOWNLOAD" or "UPLOAD"
        public string FileName { get; set; } = string.Empty;

        [ObservableProperty]
        private long _bytesTransferred;

        [ObservableProperty]
        private long _totalBytes;

        [ObservableProperty]
        private double _speedMbps;

        [ObservableProperty]
        private double _progressPercentage;

        [ObservableProperty]
        private string _status = "ACTIVE"; // ACTIVE, COMPLETED, FAILED

        public DateTime StartTime { get; set; } = DateTime.Now;

        public string FormattedProgress => TotalBytes > 0 ? $"{BytesTransferred * 100 / TotalBytes}%" : "0%";
        public string FormattedSpeed => $"{SpeedMbps:F2} MB/s";
        public string FormattedTransferred => $"{SharedFileItem.FormatBytes(BytesTransferred)} / {SharedFileItem.FormatBytes(TotalBytes)}";
    }
}
