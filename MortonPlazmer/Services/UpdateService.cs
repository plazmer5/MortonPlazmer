using System.Net.Http.Json;
using MortonPlazmer.Models;
using Microsoft.Maui.ApplicationModel;

namespace MortonPlazmer.Services;

public class UpdateService
{
    private const string Url =
        "https://raw.githubusercontent.com/plazmer5/MortonPlazmer/refs/heads/master/MortonPlazmer/version.json";

    private readonly HttpClient _http = new();

    public async Task<UpdateInfo?> GetAsync()
    {
        try
        {
            var cacheBuster = $"{Url}?v={DateTime.UtcNow.Ticks}";
            return await _http.GetFromJsonAsync<UpdateInfo>(cacheBuster);
        }
        catch
        {
            return null;
        }
    }

    public bool IsUpdateAvailable(string current, string latest)
    {
        if (string.IsNullOrWhiteSpace(latest))
            return false;

        return Version.Parse(latest) > Version.Parse(current);
    }
}