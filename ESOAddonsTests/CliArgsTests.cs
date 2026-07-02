using EsoAddons.Services;
using Xunit;

namespace ESOAddons.Tests;

public class CliArgsTests
{
    [Fact]
    public void Reads_space_separated_value()
        => Assert.Equal(@"C:\AddOns", CliArgs.GetOption(new[] { "--addons", @"C:\AddOns" }, "--addons"));

    [Fact]
    public void Reads_equals_separated_value()
        => Assert.Equal(@"C:\AddOns", CliArgs.GetOption(new[] { "--addons=C:\\AddOns" }, "--addons"));

    [Fact]
    public void Is_case_insensitive_on_the_flag()
        => Assert.Equal("x", CliArgs.GetOption(new[] { "--ADDONS", "x" }, "--addons"));

    [Fact]
    public void Returns_null_when_absent()
        => Assert.Null(CliArgs.GetOption(new[] { "--selfupdate" }, "--addons"));

    [Fact]
    public void Returns_null_when_value_missing_or_blank()
    {
        Assert.Null(CliArgs.GetOption(new[] { "--addons" }, "--addons"));
        Assert.Null(CliArgs.GetOption(new[] { "--addons", "   " }, "--addons"));
        Assert.Null(CliArgs.GetOption(new[] { "--addons=" }, "--addons"));
    }
}
