using EsoAddons.Services;
using Xunit;

namespace ESOAddons.Tests;

public class AccessGateTests
{
    [Theory]
    [InlineData("Neopia")]
    [InlineData("neopia")]
    [InlineData("NEOPIA")]
    [InlineData("  Neopia  ")]
    public void Accepts_the_password_case_insensitively_and_trimmed(string input)
        => Assert.True(AccessGate.IsCorrect(input));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("neopets")]
    [InlineData("wrong")]
    public void Rejects_blank_or_wrong_passwords(string? input)
        => Assert.False(AccessGate.IsCorrect(input));
}
