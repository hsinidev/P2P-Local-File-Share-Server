using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using P2PLocalFileShareServer.Models;

namespace P2PLocalFileShareServer.Services
{
    public class KestrelServerService
    {
        private WebApplication? _app;
        private CancellationTokenSource? _cts;

        public Channel<ServerEvent> EventChannel { get; } = Channel.CreateUnbounded<ServerEvent>();

        private readonly ConcurrentDictionary<string, int> _failedPinAttempts = new();
        private readonly ConcurrentDictionary<string, DateTime> _bannedIps = new();

        public bool IsRunning { get; private set; }
        public string ServerUrl { get; private set; } = string.Empty;

        // Shared state passed from ViewModel
        private Func<IEnumerable<SharedFileItem>>? _getSharedFilesFunc;
        private Action<SharedFileItem>? _onFileUploadedAction;

        public void Configure(Func<IEnumerable<SharedFileItem>> getSharedFilesFunc, Action<SharedFileItem> onFileUploadedAction)
        {
            _getSharedFilesFunc = getSharedFilesFunc;
            _onFileUploadedAction = onFileUploadedAction;
        }

        public async Task StartAsync(string ipAddress, int port, string pinCode, bool isPinRequired, string uploadFolder)
        {
            if (IsRunning) await StopAsync();

            _cts = new CancellationTokenSource();
            Directory.CreateDirectory(uploadFolder);

            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseKestrel(options =>
            {
                IPAddress listenIp = ipAddress == "0.0.0.0" ? IPAddress.Any : IPAddress.Parse(ipAddress);
                options.Listen(listenIp, port);
            });

            builder.Services.AddCors(options =>
            {
                options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
            });

            _app = builder.Build();
            _app.UseCors();

            ServerUrl = $"http://{(ipAddress == "0.0.0.0" ? "127.0.0.1" : ipAddress)}:{port}";

            // Read embedded html resource
            string webPortalHtml = LoadEmbeddedWebPortal();

            // Serve Root Web Portal
            _app.MapGet("/", async context =>
            {
                context.Response.ContentType = "text/html; charset=utf-8";
                await context.Response.WriteAsync(webPortalHtml);
            });

            // Auth endpoint
            _app.MapPost("/api/auth/login", async context =>
            {
                string clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "Unknown";

                if (IsIpBanned(clientIp))
                {
                    context.Response.StatusCode = 429;
                    await context.Response.WriteAsJsonAsync(new { error = "IP temporarily locked due to failed PIN attempts." });
                    return;
                }

                var body = await context.Request.ReadFromJsonAsync<PinRequest>();
                if (!isPinRequired || (body != null && body.Pin == pinCode))
                {
                    _failedPinAttempts.TryRemove(clientIp, out _);
                    await EventChannel.Writer.WriteAsync(new LogEvent($"Client {clientIp} authenticated successfully.", "INFO", DateTime.Now));
                    context.Response.StatusCode = 200;
                    await context.Response.WriteAsJsonAsync(new { success = true });
                }
                else
                {
                    int attempts = _failedPinAttempts.AddOrUpdate(clientIp, 1, (_, current) => current + 1);
                    await EventChannel.Writer.WriteAsync(new SecurityAlertEvent(clientIp, $"Failed PIN attempt ({attempts}/5)", "WARNING"));

                    if (attempts >= 5)
                    {
                        _bannedIps[clientIp] = DateTime.Now.AddMinutes(5);
                        await EventChannel.Writer.WriteAsync(new SecurityAlertEvent(clientIp, "IP banned for 5 minutes due to repeated PIN failures.", "DANGER"));
                    }

                    context.Response.StatusCode = 401;
                    await context.Response.WriteAsJsonAsync(new { error = "Invalid PIN code." });
                }
            });

            // Get shared files API
            _app.MapGet("/api/files", async context =>
            {
                if (isPinRequired && !IsAuthenticated(context, pinCode))
                {
                    context.Response.StatusCode = 401;
                    await context.Response.WriteAsJsonAsync(new { error = "Unauthorized. PIN required." });
                    return;
                }

                var files = _getSharedFilesFunc?.Invoke() ?? Enumerable.Empty<SharedFileItem>();
                await context.Response.WriteAsJsonAsync(files);
            });

            // File Download Endpoint (with chunked streaming & progress telemetry)
            _app.MapGet("/api/files/download/{id}", async context =>
            {
                if (isPinRequired && !IsAuthenticated(context, pinCode))
                {
                    context.Response.StatusCode = 401;
                    return;
                }

                string id = context.Request.RouteValues["id"]?.ToString() ?? string.Empty;
                var file = _getSharedFilesFunc?.Invoke()?.FirstOrDefault(f => f.Id == id);

                if (file == null || !File.Exists(file.FilePath))
                {
                    context.Response.StatusCode = 404;
                    await context.Response.WriteAsync("File not found");
                    return;
                }

                file.DownloadCount++;
                string sessionId = Guid.NewGuid().ToString("N")[..8];
                string clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "Localhost";
                string userAgent = context.Request.Headers.UserAgent.ToString();

                await EventChannel.Writer.WriteAsync(new ClientConnectedEvent(sessionId, clientIp, userAgent, "DOWNLOAD", file.FileName));
                await EventChannel.Writer.WriteAsync(new LogEvent($"Client {clientIp} started downloading '{file.FileName}'", "DOWNLOAD", DateTime.Now));

                context.Response.ContentType = file.ContentType;
                context.Response.Headers.ContentDisposition = $"attachment; filename=\"{Uri.EscapeDataString(file.FileName)}\"";
                context.Response.ContentLength = file.FileSizeBytes;

                const int bufferSize = 64 * 1024; // 64KB chunks
                byte[] buffer = new byte[bufferSize];
                long totalBytesSent = 0;
                var startTime = DateTime.Now;

                try
                {
                    using var fileStream = new FileStream(file.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, true);
                    int bytesRead;
                    while ((bytesRead = await fileStream.ReadAsync(buffer, 0, buffer.Length, context.RequestAborted)) > 0)
                    {
                        await context.Response.Body.WriteAsync(buffer, 0, bytesRead, context.RequestAborted);
                        totalBytesSent += bytesRead;

                        double elapsedSec = (DateTime.Now - startTime).TotalSeconds;
                        double speedMbps = elapsedSec > 0 ? (totalBytesSent / (1024.0 * 1024.0)) / elapsedSec : 0;

                        await EventChannel.Writer.WriteAsync(new TransferProgressEvent(sessionId, "DOWNLOAD", file.FileName, totalBytesSent, file.FileSizeBytes, speedMbps));
                    }

                    await EventChannel.Writer.WriteAsync(new TransferCompletedEvent(sessionId, "DOWNLOAD", file.FileName, totalBytesSent));
                    await EventChannel.Writer.WriteAsync(new LogEvent($"Completed download '{file.FileName}' to {clientIp}", "SUCCESS", DateTime.Now));
                }
                catch (OperationCanceledException)
                {
                    await EventChannel.Writer.WriteAsync(new LogEvent($"Download of '{file.FileName}' cancelled by client {clientIp}", "WARNING", DateTime.Now));
                }
                catch (Exception ex)
                {
                    await EventChannel.Writer.WriteAsync(new LogEvent($"Download error for '{file.FileName}': {ex.Message}", "ERROR", DateTime.Now));
                }
            });

            // File Upload Endpoint (with chunked progress telemetry)
            _app.MapPost("/api/upload", async context =>
            {
                if (isPinRequired && !IsAuthenticated(context, pinCode))
                {
                    context.Response.StatusCode = 401;
                    return;
                }

                if (!context.Request.HasFormContentType)
                {
                    context.Response.StatusCode = 400;
                    await context.Response.WriteAsync("Expected multipart/form-data");
                    return;
                }

                var form = await context.Request.ReadFormAsync();
                var formFile = form.Files.FirstOrDefault();
                if (formFile == null || formFile.Length == 0)
                {
                    context.Response.StatusCode = 400;
                    await context.Response.WriteAsync("No file provided");
                    return;
                }

                string safeFileName = Path.GetFileName(formFile.FileName);
                string destinationPath = Path.Combine(uploadFolder, safeFileName);

                string sessionId = Guid.NewGuid().ToString("N")[..8];
                string clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "Localhost";
                string userAgent = context.Request.Headers.UserAgent.ToString();

                await EventChannel.Writer.WriteAsync(new ClientConnectedEvent(sessionId, clientIp, userAgent, "UPLOAD", safeFileName));
                await EventChannel.Writer.WriteAsync(new LogEvent($"Client {clientIp} uploading '{safeFileName}' ({SharedFileItem.FormatBytes(formFile.Length)})", "UPLOAD", DateTime.Now));

                const int bufferSize = 64 * 1024;
                byte[] buffer = new byte[bufferSize];
                long totalBytesReceived = 0;
                var startTime = DateTime.Now;

                try
                {
                    using (var targetStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, true))
                    using (var uploadStream = formFile.OpenReadStream())
                    {
                        int bytesRead;
                        while ((bytesRead = await uploadStream.ReadAsync(buffer, 0, buffer.Length, context.RequestAborted)) > 0)
                        {
                            await targetStream.WriteAsync(buffer, 0, bytesRead, context.RequestAborted);
                            totalBytesReceived += bytesRead;

                            double elapsedSec = (DateTime.Now - startTime).TotalSeconds;
                            double speedMbps = elapsedSec > 0 ? (totalBytesReceived / (1024.0 * 1024.0)) / elapsedSec : 0;

                            await EventChannel.Writer.WriteAsync(new TransferProgressEvent(sessionId, "UPLOAD", safeFileName, totalBytesReceived, formFile.Length, speedMbps));
                        }
                    }

                    var newSharedFile = new SharedFileItem
                    {
                        FileName = safeFileName,
                        FilePath = destinationPath,
                        FileSizeBytes = formFile.Length,
                        ContentType = formFile.ContentType
                    };

                    _onFileUploadedAction?.Invoke(newSharedFile);

                    await EventChannel.Writer.WriteAsync(new TransferCompletedEvent(sessionId, "UPLOAD", safeFileName, totalBytesReceived));
                    await EventChannel.Writer.WriteAsync(new LogEvent($"Upload completed '{safeFileName}' saved to destination", "SUCCESS", DateTime.Now));

                    context.Response.StatusCode = 200;
                    await context.Response.WriteAsJsonAsync(new { success = true, filename = safeFileName });
                }
                catch (Exception ex)
                {
                    await EventChannel.Writer.WriteAsync(new LogEvent($"Upload failed for '{safeFileName}': {ex.Message}", "ERROR", DateTime.Now));
                    context.Response.StatusCode = 500;
                    await context.Response.WriteAsync("Upload failed");
                }
            });

            await _app.StartAsync(_cts.Token);
            IsRunning = true;
            await EventChannel.Writer.WriteAsync(new LogEvent($"Kestrel HTTP/2 Server listening at {ServerUrl}", "INFO", DateTime.Now));
        }

        public async Task StopAsync()
        {
            if (_app != null)
            {
                await _app.StopAsync();
                await _app.DisposeAsync();
                _app = null;
            }

            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;

            IsRunning = false;
            await EventChannel.Writer.WriteAsync(new LogEvent("Kestrel server stopped.", "INFO", DateTime.Now));
        }

        private bool IsAuthenticated(HttpContext context, string pinCode)
        {
            string headerPin = context.Request.Headers["X-P2P-PIN"].ToString();
            string queryPin = context.Request.Query["pin"].ToString();

            return (headerPin == pinCode) || (queryPin == pinCode);
        }

        private bool IsIpBanned(string ip)
        {
            if (_bannedIps.TryGetValue(ip, out DateTime banExpiry))
            {
                if (DateTime.Now < banExpiry) return true;
                _bannedIps.TryRemove(ip, out _);
            }
            return false;
        }

        private string LoadEmbeddedWebPortal()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                string resourceName = assembly.GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith("WebPortal.html")) ?? string.Empty;

                if (!string.IsNullOrEmpty(resourceName))
                {
                    using Stream? stream = assembly.GetManifestResourceStream(resourceName);
                    if (stream != null)
                    {
                        using StreamReader reader = new StreamReader(stream, Encoding.UTF8);
                        return reader.ReadToEnd();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load embedded WebPortal.html: {ex.Message}");
            }

            // Fallback inline web portal HTML
            return "<html><body><h1>P2P Local Share Server Running</h1></body></html>";
        }
    }

    public record PinRequest(string Pin);
}
