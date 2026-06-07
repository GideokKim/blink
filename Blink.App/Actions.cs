// Blink.App (WPF, Windows-only). NOT built on macOS — verify on Windows.
using System.Diagnostics;
using System.IO;

namespace Blink.App;

/// <summary>Result of attempting to open a path.</summary>
internal readonly record struct OpenResult(bool Ok, string? Reason);

/// <summary>File-open actions: default-handler open and parent-folder reveal.</summary>
internal static class Actions
{
    /// <summary>Open <paramref name="path"/> with the OS default handler (UseShellExecute).</summary>
    public static OpenResult OpenDefault(string path)
    {
        try
        {
            if (!File.Exists(path) && !Directory.Exists(path))
                return new OpenResult(false, "File not found");

            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return new OpenResult(true, null);
        }
        catch (Exception ex)
        {
            return new OpenResult(false, ex.Message);
        }
    }

    /// <summary>Open Explorer with the file selected; falls back to opening the directory.</summary>
    public static OpenResult OpenParentFolder(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
                return new OpenResult(true, null);
            }

            var dir = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                return new OpenResult(false, "Parent folder not found");

            Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
            return new OpenResult(true, null);
        }
        catch (Exception ex)
        {
            return new OpenResult(false, ex.Message);
        }
    }
}
