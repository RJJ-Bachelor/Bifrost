using System.Security.Cryptography;
using System.Text;

namespace EirService.Test;

internal static class TestIds
{
    public static Guid From(string value) => Guid.TryParse(value, out var id) ? id
        : Guid.Parse(Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes("bifrost:id:" + value))));
}
