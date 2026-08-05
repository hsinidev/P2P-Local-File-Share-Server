using System;

namespace P2PLocalFileShareServer.Models
{
    public abstract record ServerEvent;

    public record ClientConnectedEvent(string SessionId, string ClientIp, string UserAgent, string ActionType, string FileName) : ServerEvent;

    public record TransferProgressEvent(string SessionId, string ActionType, string FileName, long BytesTransferred, long TotalBytes, double CurrentSpeedMbps) : ServerEvent;

    public record TransferCompletedEvent(string SessionId, string ActionType, string FileName, long TotalBytes) : ServerEvent;

    public record SecurityAlertEvent(string ClientIp, string Message, string Severity) : ServerEvent;

    public record LogEvent(string Message, string Level, DateTime Timestamp) : ServerEvent;
}
