using System.Buffers.Text;
using System.Security.Cryptography;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Publishing;

namespace YggdrasilService.AppHost.Parameters;

internal sealed class VapidPrivateKeyDefault : ParameterDefault
{
    public override string GetDefaultValue()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return Base64Url.EncodeToString(key.ExportParameters(true).D!);
    }

    public override void WriteToManifest(ManifestPublishingContext context) =>
        throw new NotSupportedException("VAPID key generation is only available for local Aspire runs.");
}
