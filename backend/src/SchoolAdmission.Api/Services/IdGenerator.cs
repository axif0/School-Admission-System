using System.Security.Cryptography;

namespace SchoolAdmission.Api.Services;

/// <summary>
/// Generates unique IDs in the format: prefix + 12-char lowercase base32 of random bytes.
/// </summary>
public class IdGenerator
{
    private static readonly char[] Base32Chars = "0123456789abcdefghjkmnpqrstvwxyz".ToCharArray();

    public string NewClassId() => Generate("c_");
    public string NewHoldId() => Generate("h_");

    private static string Generate(string prefix)
    {
        // 8 bytes → 64 bits → 12 base32 chars (each char = ~5 bits → 60 bits; we use 8 bytes for entropy)
        var bytes = RandomNumberGenerator.GetBytes(8);
        var chars = new char[12];
        // Use 60 bits from 8 bytes
        ulong value = BitConverter.ToUInt64(bytes, 0);
        for (int i = 11; i >= 0; i--)
        {
            chars[i] = Base32Chars[value & 0x1F];
            value >>= 5;
        }
        return prefix + new string(chars);
    }
}
