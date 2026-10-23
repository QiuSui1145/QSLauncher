namespace QSLauncher.Core.Models;

public sealed class DownloadProgress
{
    public DownloadProgress(
        string stage,
        string item,
        long bytesReceived,
        long totalBytes,
        int completedItems = 0,
        int totalItems = 0)
    {
        Stage = stage;
        Item = item;
        BytesReceived = bytesReceived;
        TotalBytes = totalBytes;
        CompletedItems = completedItems;
        TotalItems = totalItems;
    }

    public string Stage { get; }

    public string Item { get; }

    public long BytesReceived { get; }

    public long TotalBytes { get; }

    public int CompletedItems { get; }

    public int TotalItems { get; }

    public double Fraction => TotalBytes <= 0
        ? (TotalItems > 0 ? (double)CompletedItems / TotalItems : 0)
        : Math.Clamp((double)BytesReceived / TotalBytes, 0, 1);

    public int Percent => (int)Math.Round(Fraction * 100);
}
