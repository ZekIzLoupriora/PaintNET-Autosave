using System.IO;
using System.Globalization;

namespace AutoSavePdnPoc;

internal static class SnapshotExporter
{
    public static void TryExport(object document, string? filePath)
    {
        try
        {
            var outputPath = CreateOutputPath(filePath);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

            // Clone first. SaveToStream updates serialization bookkeeping on the
            // object being saved; the live document is therefore never passed
            // directly to the native PDN serializer.
            var cloneMethod = document.GetType().GetMethod("Clone", Type.EmptyTypes)
                ?? throw new InvalidOperationException("Paint.NET Document.Clone() was not found.");
            var clone = cloneMethod.Invoke(document, null)
                ?? throw new InvalidOperationException("Paint.NET Document.Clone() returned null.");
            using var stream = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            var saveMethod = clone.GetType().GetMethod("SaveToStream", new[] { typeof(Stream) })
                ?? throw new InvalidOperationException("Paint.NET Document.SaveToStream(Stream) was not found.");
            saveMethod.Invoke(clone, new object[] { stream });
            (clone as IDisposable)?.Dispose();
        }
        catch (Exception ex)
        {
            WriteError(ex);
        }
    }

    private static string CreateOutputPath(string? filePath)
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Paint.NET",
            "AutoSavePdnPoc",
            "snapshots");

        var baseName = string.IsNullOrWhiteSpace(filePath)
            ? "snapshot"
            : Path.GetFileNameWithoutExtension(filePath);
        if (string.IsNullOrWhiteSpace(baseName))
            baseName = "snapshot";

        var timestamp = DateTime.Now.ToString("H-mm-ss_dd.MM.yyyy", CultureInfo.InvariantCulture);
        var path = Path.Combine(directory, $"{baseName}_{timestamp}.pdn");
        var suffix = 2;
        while (File.Exists(path))
        {
            path = Path.Combine(directory, $"{baseName}_{timestamp}_{suffix++}.pdn");
        }

        return path;
    }

    private static void WriteError(Exception ex)
    {
        var diagnosticsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Paint.NET",
            "AutoSavePdnPoc");
        Directory.CreateDirectory(diagnosticsDirectory);
        File.WriteAllText(Path.Combine(diagnosticsDirectory, "snapshot-error.txt"), ex.ToString());
    }

}
