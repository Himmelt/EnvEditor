using EnvEditor.Models;
using EnvEditor.Services;

namespace EnvEditorTests.Services;

public class CryptoServiceTests
{
    private readonly CryptoService _crypto = new();

    [Fact]
    public void EncryptDecrypt_RoundTripsUtf8Payload()
    {
        const string password = "p@ssw0rd-密钥";
        const string plain = """
            {"SyncId":"team-a","Variables":[{"Name":"FOO","Value":"%USERPROFILE%\\bin","Kind":"Expand"}]}
            """;

        var enc = _crypto.Encrypt(plain, password);

        Assert.Equal(plain, _crypto.Decrypt(enc, password));
    }

    [Fact]
    public void Encrypt_IsSingleLineBase64_ForGitFileStorage()
    {
        var enc = _crypto.Encrypt("hello", "pwd");

        Assert.DoesNotContain('\n', enc);
        Assert.DoesNotContain('\r', enc);
        var bytes = Convert.FromBase64String(enc); // 不抛异常即为合法 Base64
        Assert.True(bytes.Length > 6 + 16 + 12 + 16);
    }

    [Fact]
    public void Encrypt_ProducesDistinctOutputOnEachCall()
    {
        // salt/nonce 每次随机，相同明文+密码也不应产出相同密文
        var a = _crypto.Encrypt("hello", "pwd");
        var b = _crypto.Encrypt("hello", "pwd");

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Decrypt_WrongPassword_ThrowsAmbiguous()
    {
        var enc = _crypto.Encrypt("hello", "right-password");

        var ex = Assert.Throws<DecryptionFailedException>(() => _crypto.Decrypt(enc, "wrong-password"));

        Assert.True(ex.Ambiguous); // 密码错与密文损坏不可区分，UI 据此给"密钥遗忘不可恢复"提示
    }

    [Fact]
    public void Decrypt_TamperedCiphertext_ThrowsAmbiguous()
    {
        var enc = _crypto.Encrypt("hello", "pwd");
        var bytes = Convert.FromBase64String(enc);
        bytes[^1] ^= 0xFF; // 翻转密文最后一字节，GCM tag 必然校验失败

        var ex = Assert.Throws<DecryptionFailedException>(
            () => _crypto.Decrypt(Convert.ToBase64String(bytes), "pwd"));

        Assert.True(ex.Ambiguous);
    }

    [Fact]
    public void Decrypt_NotBase64_ThrowsNotAmbiguous()
    {
        var ex = Assert.Throws<DecryptionFailedException>(() => _crypto.Decrypt("这不是 base64 !!", "pwd"));

        Assert.False(ex.Ambiguous);
    }

    [Fact]
    public void Decrypt_TooShort_ThrowsNotAmbiguous()
    {
        var ex = Assert.Throws<DecryptionFailedException>(
            () => _crypto.Decrypt(Convert.ToBase64String(new byte[3]), "pwd"));

        Assert.False(ex.Ambiguous);
    }

    [Fact]
    public void Decrypt_WrongMagic_ThrowsNotAmbiguous()
    {
        var bytes = Convert.FromBase64String(_crypto.Encrypt("hello", "pwd"));
        bytes[0] = (byte)'X'; // 破坏 "ENVED1" magic 头

        var ex = Assert.Throws<DecryptionFailedException>(
            () => _crypto.Decrypt(Convert.ToBase64String(bytes), "pwd"));

        Assert.False(ex.Ambiguous);
    }

    [Fact]
    public void EncryptDecrypt_HandlesEmptyPlaintext()
    {
        var enc = _crypto.Encrypt("", "pwd");

        Assert.Equal("", _crypto.Decrypt(enc, "pwd"));
    }
}