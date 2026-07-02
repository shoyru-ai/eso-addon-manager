using System.Diagnostics;
using System.IO;
using EsoAddons.Models;
using EsoAddons.Services;
using Xunit;

namespace EsoAddons.Tests;

public class InstallerTests
{
    // ---- ADD ----
    [Fact]
    public void ExtractZip_installs_addon_folder_and_returns_top_dirs()
    {
        using var t = new TempDir();
        var zip = Zips.Build(
            ("MyAddon/MyAddon.txt", "## Title: MyAddon\n## Version: 1\n"),
            ("MyAddon/MyAddon.lua", "return 1"));

        var dirs = AddonInstaller.ExtractZip(zip, t.Path);

        Assert.True(File.Exists(t.Combine("MyAddon", "MyAddon.txt")));
        Assert.True(File.Exists(t.Combine("MyAddon", "MyAddon.lua")));
        Assert.Contains("MyAddon", dirs);
    }

    [Fact]
    public void ExtractZip_handles_multi_folder_zip()
    {
        using var t = new TempDir();
        var zip = Zips.Build(
            ("LibA/LibA.txt", "## Title: LibA\n"),
            ("LibB/LibB.txt", "## Title: LibB\n"));
        var dirs = AddonInstaller.ExtractZip(zip, t.Path);
        Assert.Contains("LibA", dirs);
        Assert.Contains("LibB", dirs);
    }

    // ---- UPDATE ----
    [Fact]
    public void ExtractZip_overwrites_existing_files_on_update()
    {
        using var t = new TempDir();
        AddonInstaller.ExtractZip(Zips.Build(("A/A.txt", "## Version: 1\n")), t.Path);
        AddonInstaller.ExtractZip(Zips.Build(("A/A.txt", "## Version: 2\n")), t.Path);
        Assert.Contains("Version: 2", File.ReadAllText(t.Combine("A", "A.txt")));
    }

    // ---- SECURITY ----
    [Fact]
    public void ExtractZip_blocks_zip_slip_escape()
    {
        using var t = new TempDir();
        AddonInstaller.ExtractZip(Zips.Build(("../evil.txt", "pwned")), t.Path);
        var escaped = Path.GetFullPath(Path.Combine(t.Path, "..", "evil.txt"));
        Assert.False(File.Exists(escaped));
    }

    // ---- REMOVE ----
    [Fact]
    public void Uninstall_removes_a_normal_folder()
    {
        using var t = new TempDir();
        var dir = t.Combine("Foo");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "x.txt"), "1");
        var addon = new InstalledAddon { FolderName = "Foo", Title = "Foo", Path = dir };

        var (ok, _) = AddonInstaller.Uninstall(addon);

        Assert.True(ok);
        Assert.False(Directory.Exists(dir));
    }

    [Fact]
    public void Uninstall_unlinks_junction_and_leaves_target_intact()
    {
        using var t = new TempDir();
        var target = t.Combine("Target");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "x.txt"), "keep me");
        var link = t.Combine("Link");

        var psi = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        Process.Start(psi)!.WaitForExit();
        Assert.True(Directory.Exists(link));   // sanity: junction created

        var addon = new InstalledAddon { FolderName = "Link", Title = "Link", Path = link };
        var (ok, msg) = AddonInstaller.Uninstall(addon);

        Assert.True(ok);                                                      // junction is now removable
        Assert.Contains("unlinked", msg, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(link));                                // link removed
        Assert.True(File.Exists(Path.Combine(target, "x.txt")));             // real source untouched
    }

    [Fact]
    public void Uninstall_reports_when_folder_missing()
    {
        var addon = new InstalledAddon { FolderName = "Gone", Title = "Gone", Path = @"C:\nope\xyz\Gone" };
        var (ok, _) = AddonInstaller.Uninstall(addon);
        Assert.False(ok);
    }
}
