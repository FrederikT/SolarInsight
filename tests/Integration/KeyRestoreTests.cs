using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace SolarInsight.Tests;

public sealed class KeyRestoreTests
{
    [Fact]
    public void A24_ProtectedProviderFixtureSurvivesKeyRestore()
    {
        string root = Path.Combine(Path.GetTempPath(), "solar-keys-" + Guid.NewGuid().ToString("N"));
        var original = Directory.CreateDirectory(Path.Combine(root, "original"));
        var restored = Directory.CreateDirectory(Path.Combine(root, "restored"));
        using var owner = Provider(original);
        const string fixture = "synthetic-refresh-token-fixture";
        string cipher = Protector(owner).Protect(fixture);
        Assert.DoesNotContain(fixture, cipher);
        foreach (var key in original.GetFiles()) key.CopyTo(Path.Combine(restored.FullName, key.Name));
        using var backup = Provider(restored);
        Assert.Equal(fixture, Protector(backup).Unprotect(cipher));
    }

    static ServiceProvider Provider(DirectoryInfo folder)
    {
        var services = new ServiceCollection();
        services.AddDataProtection().SetApplicationName("SolarInsight").PersistKeysToFileSystem(folder);
        return services.BuildServiceProvider();
    }

    static IDataProtector Protector(ServiceProvider provider) =>
        provider.GetRequiredService<IDataProtectionProvider>().CreateProtector("sungrow-tokens-v1");
}

