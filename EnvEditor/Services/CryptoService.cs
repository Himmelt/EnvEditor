using System.Security.Cryptography;
using System.Text;
using EnvEditor.Models;

namespace EnvEditor.Services;

/// <summary>
/// PBKDF2-HMAC-SHA256 (600k) + AES-256-GCM。
/// 落盘布局：magic(6) | salt(16) | nonce(12) | tag(16) | ciphertext，整体 Base64 成单行字符串。
/// </summary>
public sealed class CryptoService
{
    private const int Iterations = 600_000;     // OWASP 2023 (SHA-256)
    private const int SaltSize = 16;
    private const int NonceSize = 12;           // GCM 推荐 96-bit
    private const int TagSize = 16;             // 128-bit tag
    private const int KeySize = 32;             // AES-256
    private static readonly byte[] Magic = "ENVED1"u8.ToArray();

    public string Encrypt(string plaintext, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var key = DeriveKey(password, salt);
        var pt = Encoding.UTF8.GetBytes(plaintext);
        var ct = new byte[pt.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(key, TagSize);  // .NET 8+ 必须用两参构造
        aes.Encrypt(nonce, pt, ct, tag);

        var outBuf = new byte[Magic.Length + SaltSize + NonceSize + TagSize + ct.Length];
        var off = 0;
        Buffer.BlockCopy(Magic, 0, outBuf, off, Magic.Length); off += Magic.Length;
        Buffer.BlockCopy(salt, 0, outBuf, off, SaltSize); off += SaltSize;
        Buffer.BlockCopy(nonce, 0, outBuf, off, NonceSize); off += NonceSize;
        Buffer.BlockCopy(tag, 0, outBuf, off, TagSize); off += TagSize;
        Buffer.BlockCopy(ct, 0, outBuf, off, ct.Length);
        return Convert.ToBase64String(outBuf);
    }

    public string Decrypt(string encoded, string password)
    {
        byte[] all;
        try { all = Convert.FromBase64String(encoded); }
        catch (FormatException) { throw new DecryptionFailedException("数据不是合法的 Base64（可能已损坏）", false); }

        var header = Magic.Length + SaltSize + NonceSize + TagSize;
        if (all.Length < header) throw new DecryptionFailedException("数据长度不足，可能已损坏", false);

        if (!all.AsSpan(0, Magic.Length).SequenceEqual(Magic))
            throw new DecryptionFailedException("格式不符或文件已损坏（magic 头不匹配）", false);

        var salt = new byte[SaltSize];
        var nonce = new byte[NonceSize];
        var tag = new byte[TagSize];
        Buffer.BlockCopy(all, Magic.Length, salt, 0, SaltSize);
        Buffer.BlockCopy(all, Magic.Length + SaltSize, nonce, 0, NonceSize);
        Buffer.BlockCopy(all, Magic.Length + SaltSize + NonceSize, tag, 0, TagSize);
        var ct = new byte[all.Length - header];
        Buffer.BlockCopy(all, header, ct, 0, ct.Length);

        var key = DeriveKey(password, salt);
        var pt = new byte[ct.Length];
        using var aes = new AesGcm(key, TagSize);
        try
        {
            aes.Decrypt(nonce, ct, tag, pt);
        }
        catch (AuthenticationTagMismatchException)
        {
            // GCM tag 校验失败：密码错 与 密文损坏 不可区分
            throw new DecryptionFailedException("解密失败：密码错误或数据已损坏（二者无法区分）", true);
        }
        return Encoding.UTF8.GetString(pt);
    }

    private static byte[] DeriveKey(string password, byte[] salt)
    {
        return Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, KeySize);
    }
}
