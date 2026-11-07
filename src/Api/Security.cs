using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;

namespace SolarInsight.Api;

public record RuntimeOptions(bool Demo, string State, bool AllowHttp);

public sealed class LocalAccess(RuntimeOptions options)
{
    static readonly PasswordHasher<string> Hasher = new();
    readonly string hashPath = Path.Combine(options.State, "owner.hash");

    public void Initialize(string? secretFile)
    {
        if (options.Demo || File.Exists(hashPath)) return;
        if (secretFile is null || !File.Exists(secretFile))
            throw new InvalidOperationException("Für die Erstinstallation wird eine lokale Passwortdatei benötigt.");
        string password = File.ReadAllText(secretFile).TrimEnd('\r', '\n');
        Validate(password);
        Write(password);
    }

    public bool Verify(string password) => password.Length is >= 14 and <= 256 && File.Exists(hashPath) &&
        Hasher.VerifyHashedPassword("owner", File.ReadAllText(hashPath), password) != PasswordVerificationResult.Failed;

    public void Change(string current, string next)
    {
        if (!Verify(current)) throw new ArgumentException("Das aktuelle Passwort stimmt nicht.");
        Validate(next);
        Write(next);
    }

    public string Stamp => File.Exists(hashPath) ? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
        File.ReadAllBytes(hashPath))) : "";

    static void Validate(string password)
    {
        if (password.Length is < 14 or > 256) throw new ArgumentException("Passwort muss 14 bis 256 Zeichen enthalten.");
    }

    void Write(string password)
    {
        var temporary = hashPath + "." + Guid.NewGuid().ToString("N");
        File.WriteAllText(temporary, Hasher.HashPassword("owner", password));
        File.Move(temporary, hashPath, true);
    }
}

public sealed class Visitor(IDataProtectionProvider protection, RuntimeOptions options)
{
    const string Cookie = "solar_visitor";
    readonly IDataProtector protector = protection.CreateProtector("visitor-v1");

    public Guid Get(HttpContext context, bool create)
    {
        if (!options.Demo) return StorageOwner;
        if (context.Request.Cookies.TryGetValue(Cookie, out var token))
        {
            try { if (Guid.TryParse(protector.Unprotect(token), out var id)) return id; }
            catch (System.Security.Cryptography.CryptographicException) { }
        }
        if (!create) return Guid.Empty;
        var owner = Guid.NewGuid();
        context.Response.Cookies.Append(Cookie, protector.Protect(owner.ToString()), new CookieOptions
        {
            HttpOnly = true, Secure = !options.AllowHttp, SameSite = SameSiteMode.Strict, MaxAge = TimeSpan.FromHours(12)
        });
        return owner;
    }

    static readonly Guid StorageOwner = Guid.Parse("75d2db3a-3d02-43cc-9585-3ffb69f912e2");
}

