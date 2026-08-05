namespace P2PLocalFileShareServer.Models
{
    public class NetworkAdapterInfo
    {
        public string Name { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public string InterfaceType { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public string Description { get; set; } = string.Empty;

        public string DisplayName => $"{Name} ({IpAddress}) - {InterfaceType}";

        public override string ToString() => DisplayName;
    }
}
