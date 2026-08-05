using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using P2PLocalFileShareServer.Models;

namespace P2PLocalFileShareServer.Services
{
    public class NetworkDiscoveryService
    {
        public List<NetworkAdapterInfo> GetActiveIPv4Adapters()
        {
            var result = new List<NetworkAdapterInfo>();

            try
            {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(ni => ni.OperationalStatus == OperationalStatus.Up &&
                                 ni.NetworkInterfaceType != NetworkInterfaceType.Loopback);

                foreach (var ni in interfaces)
                {
                    var ipProperties = ni.GetIPProperties();
                    var unicastAddresses = ipProperties.UnicastAddresses
                        .Where(ua => ua.Address.AddressFamily == AddressFamily.InterNetwork &&
                                     !IPAddress.IsLoopback(ua.Address) &&
                                     !ua.Address.ToString().StartsWith("169.254."));

                    foreach (var ua in unicastAddresses)
                    {
                        var adapter = new NetworkAdapterInfo
                        {
                            Name = ni.Name,
                            IpAddress = ua.Address.ToString(),
                            InterfaceType = ni.NetworkInterfaceType.ToString(),
                            IsActive = true,
                            Description = ni.Description
                        };

                        result.Add(adapter);
                    }
                }

                // Sort so Wi-Fi and Ethernet come first
                result = result.OrderByDescending(a => a.InterfaceType.Contains("Wireless") || a.InterfaceType.Contains("Ethernet"))
                               .ThenBy(a => a.Name)
                               .ToList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error discovering network adapters: {ex.Message}");
            }

            if (!result.Any())
            {
                // Fallback default
                result.Add(new NetworkAdapterInfo
                {
                    Name = "All Interfaces (0.0.0.0)",
                    IpAddress = "0.0.0.0",
                    InterfaceType = "Any",
                    IsActive = true,
                    Description = "Listen on all local interfaces"
                });
            }

            return result;
        }

        public string GetPrimaryLocalIp()
        {
            var adapters = GetActiveIPv4Adapters();
            var primary = adapters.FirstOrDefault(a => a.IpAddress != "0.0.0.0");
            return primary?.IpAddress ?? "127.0.0.1";
        }
    }
}
