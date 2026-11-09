using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace FleetFlow.IntegrationTests;

// Test-only external secrets, generated outside application startup and never added to an image/repository.
internal static class TestCertificate
{
    public static readonly string DirectoryPath;
    public static readonly string CertificatePath;
    public static readonly string PrivateKeyPath;
    public static readonly string PfxPath;
    static TestCertificate()
    {
        DirectoryPath = Path.Combine(Path.GetTempPath(), "fleetflow-tls-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DirectoryPath);
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment | X509KeyUsageFlags.KeyCertSign, true));
        var names = new SubjectAlternativeNameBuilder(); names.AddDnsName("localhost"); names.AddDnsName("host.docker.internal"); names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(7));
        CertificatePath = Path.Combine(DirectoryPath, "server.crt"); PrivateKeyPath = Path.Combine(DirectoryPath, "server.key"); PfxPath = Path.Combine(DirectoryPath, "protection.pfx");
        File.WriteAllText(CertificatePath, certificate.ExportCertificatePem());
        File.WriteAllText(PrivateKeyPath, rsa.ExportPkcs8PrivateKeyPem());
        File.WriteAllBytes(PfxPath, certificate.Export(X509ContentType.Pfx));
        if (!OperatingSystem.IsWindows())
            foreach (var path in new[] { CertificatePath, PrivateKeyPath, PfxPath }) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => { try { Directory.Delete(DirectoryPath, true); } catch (IOException) { } };
    }
}
