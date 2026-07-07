using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using AwesomeAssertions;
using Starling.Net.Tls;

namespace Starling.Net.Tests.Tls;

[TestClass]
public class CertificateVerifierTests
{
    [TestMethod]
    public void Accepts_a_leaf_that_chains_to_a_bundled_root_and_matches_the_host()
    {
        var (root, leaf) = BuildChain("CN=Test Root", "leaf.example");
        var roots = RootsOf(root);

        CertificateVerifier.Verify(leaf, null, "leaf.example", roots)
            .Should().BeTrue();
    }

    [TestMethod]
    public void Rejects_a_host_the_leaf_does_not_cover()
    {
        var (root, leaf) = BuildChain("CN=Test Root", "leaf.example");
        var roots = RootsOf(root);

        CertificateVerifier.Verify(leaf, null, "other.example", roots)
            .Should().BeFalse();
    }

    [TestMethod]
    public void Rejects_a_leaf_that_chains_to_an_untrusted_root()
    {
        var (_, leaf) = BuildChain("CN=Real Root", "leaf.example");
        var (otherRoot, _) = BuildChain("CN=Other Root", "unrelated.example");
        var roots = RootsOf(otherRoot);

        CertificateVerifier.Verify(leaf, null, "leaf.example", roots)
            .Should().BeFalse();
    }

    private static RootCertificates RootsOf(X509Certificate2 root)
    {
        var pem = Encoding.ASCII.GetBytes(root.ExportCertificatePem());
        return RootCertificates.FromPem(new MemoryStream(pem));
    }

    private static (X509Certificate2 Root, X509Certificate2 Leaf) BuildChain(string rootSubject, string leafHost)
    {
        var notBefore = DateTimeOffset.UtcNow.AddDays(-1);
        var notAfter = DateTimeOffset.UtcNow.AddDays(1);

        using var caKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var caRequest = new CertificateRequest(rootSubject, caKey, HashAlgorithmName.SHA256);
        caRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        var root = caRequest.CreateSelfSigned(notBefore, notAfter);

        using var leafKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var leafRequest = new CertificateRequest($"CN={leafHost}", leafKey, HashAlgorithmName.SHA256);
        leafRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName(leafHost);
        leafRequest.CertificateExtensions.Add(sanBuilder.Build());

        var serial = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var leaf = leafRequest.Create(root, notBefore, notAfter, serial);
        return (root, leaf);
    }
}
