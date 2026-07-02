using System.IO;
using System.IO.Compression;
using System.Text;

namespace EsoAddons.Tests;

/// <summary>A self-deleting temp directory for filesystem tests.</summary>
public sealed class TempDir : IDisposable
{
    public string Path { get; }
    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "esoaddtest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }
    public string Combine(params string[] parts) => System.IO.Path.Combine(new[] { Path }.Concat(parts).ToArray());
    public void Dispose() { try { Directory.Delete(Path, true); } catch { /* best effort */ } }
}

public static class Zips
{
    /// <summary>Builds an in-memory zip from (entryPath, textContent) pairs.</summary>
    public static byte[] Build(params (string path, string content)[] entries)
    {
        using var ms = new MemoryStream();
        using (var z = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in entries)
            {
                var e = z.CreateEntry(path);
                using var s = e.Open();
                var bytes = Encoding.UTF8.GetBytes(content);
                s.Write(bytes, 0, bytes.Length);
            }
        }
        return ms.ToArray();
    }
}

public static class AddonFolder
{
    /// <summary>Writes a fake installed addon (folder + manifest .txt) under <paramref name="addonsPath"/>.</summary>
    public static string Write(string addonsPath, string folder, string manifest)
    {
        var dir = Path.Combine(addonsPath, folder);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, folder + ".txt"), manifest);
        return dir;
    }
}
