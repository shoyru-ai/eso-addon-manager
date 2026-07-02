using EsoAddons.Services;
using Xunit;

namespace ESOAddons.Tests;

public class LicenseServiceTests
{
    [Fact]
    public void Activate_success_parses_status_instance_product_customer()
    {
        const string json = """
        {
          "activated": true,
          "error": null,
          "license_key": { "id": 1, "status": "active", "key": "ABC", "activation_limit": 3, "activation_usage": 1 },
          "instance": { "id": "inst-123", "name": "PC", "created_at": "2026-06-26" },
          "meta": { "store_id": 1, "order_id": 2, "product_id": 1178447, "customer_name": "Jake" }
        }
        """;
        var r = LicenseService.ParseResponse(json, "activated");
        Assert.True(r.Ok);
        Assert.Equal("active", r.Status);
        Assert.Equal("inst-123", r.InstanceId);
        Assert.Equal("1178447", r.ProductId);
        Assert.Equal("Jake", r.CustomerName);
        Assert.True(r.IsPro);   // active + matches the configured Pro product
    }

    [Fact]
    public void Activate_failure_surfaces_error_and_is_not_pro()
    {
        const string json = """
        { "activated": false, "error": "This license key has reached the activation limit.",
          "license_key": { "status": "active" }, "instance": null, "meta": { "product_id": 999 } }
        """;
        var r = LicenseService.ParseResponse(json, "activated");
        Assert.False(r.Ok);
        Assert.Contains("activation limit", r.Error);
        Assert.False(r.IsPro);
    }

    [Fact]
    public void Validate_inactive_key_is_not_pro()
    {
        const string json = """
        { "valid": false, "error": null, "license_key": { "status": "expired" }, "instance": null, "meta": {} }
        """;
        var r = LicenseService.ParseResponse(json, "valid");
        Assert.False(r.Ok);
        Assert.Equal("expired", r.Status);
        Assert.False(r.IsPro);
    }

    [Fact]
    public void Garbage_response_does_not_throw()
    {
        var r = LicenseService.ParseResponse("not json", "activated");
        Assert.False(r.Ok);
        Assert.False(r.IsPro);
        Assert.NotEqual("", r.Error);
    }

    [Fact]
    public void ProductMatches_only_the_configured_product()
    {
        Assert.True(LicenseService.ProductMatches("1178447"));    // our Pro product
        Assert.False(LicenseService.ProductMatches("999"));       // a key from a different product
    }

    [Fact]
    public void DeviceId_is_stable_and_nonempty()
    {
        var a = DeviceId.Current;
        var b = DeviceId.Current;
        Assert.False(string.IsNullOrWhiteSpace(a));
        Assert.Equal(a, b);
    }
}
