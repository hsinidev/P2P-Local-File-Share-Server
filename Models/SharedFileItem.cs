using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace P2PLocalFileShareServer.Models
{
    public partial class SharedFileItem : ObservableObject
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public long FileSizeBytes { get; set; }
        public string ContentType { get; set; } = "application/octet-stream";
        public DateTime AddedTime { get; set; } = DateTime.Now;

        [ObservableProperty]
        private int _downloadCount;

        public string FormattedSize => FormatBytes(FileSizeBytes);

        public static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{(bytes / 1024.0):F2} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{(bytes / (1024.0 * 1024.0)):F2} MB";
            return $"{(bytes / (1024.0 * 1024.0 * 1024.0)):F2} GB";
        }
    }
}
