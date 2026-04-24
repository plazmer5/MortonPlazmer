namespace MortonPlazmer.Models;

public class UpdateInfo
{
    public string? Version { get; set; }
    public bool ForceUpdate { get; set; }
    public string? Message { get; set; }

    public string? AndroidUrl { get; set; }
    public string? WindowsUrl { get; set; }
}