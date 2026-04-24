using System.Net.Http;

namespace MortonPlazmer.Services;

public class DownloadService
{
    private readonly HttpClient _http = new();

    public async Task<string> DownloadFileAsync(
        string url,
        string fileName,
        IProgress<double> progress)
    {
        var filePath = Path.Combine(FileSystem.CacheDirectory, fileName);

        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? -1L;
        var canReport = total != -1 && progress != null;

        await using var input = await response.Content.ReadAsStreamAsync();
        await using var output = File.OpenWrite(filePath);

        var buffer = new byte[81920];
        long read = 0;
        int bytesRead;

        while ((bytesRead = await input.ReadAsync(buffer)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, bytesRead));
            read += bytesRead;

            if (canReport)
                if (progress != null && total > 0)
                {
                    progress.Report((double)read / total);
                }
        }

        return filePath;
    }
}