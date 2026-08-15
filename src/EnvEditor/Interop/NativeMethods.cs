using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace EnvEditor.Interop;

/// <summary>
/// P/Invoke 声明：广播环境变量变更 + 读取 Windows 凭据管理器。
/// 全部用 [LibraryImport]（.NET 6+ 源生成器），避免 DllImport 的 SYSLIB 过时告警。
/// </summary>
[SupportedOSPlatform("windows")]
internal static partial class NativeMethods
{
    // ── user32：广播 WM_SETTINGCHANGE("Environment") ──
    private const int HWND_BROADCAST = 0xFFFF;
    private const uint WM_SETTINGCHANGE = 0x001A;
    private const uint SMTO_ABORTIFHUNG = 0x0002;

    [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SendMessageTimeout(
        IntPtr hWnd, uint msg, UIntPtr wParam,
        string? lParam, uint fuFlags, uint uTimeout, out UIntPtr lpdwResult);

    public static void BroadcastEnvironmentChange()
    {
        SendMessageTimeout(
            (IntPtr)HWND_BROADCAST, WM_SETTINGCHANGE, UIntPtr.Zero,
            "Environment", SMTO_ABORTIFHUNG, 1000, out _);
    }

    // ── advapi32：读取 Windows 凭据管理器 ──
    // CREDENTIAL 结构体在 64 位下关键字段偏移：
    //   +0x00 Flags (uint)
    //   +0x04 Type (uint)
    //   +0x08 TargetName (ptr)
    //   +0x10 Comment (ptr)
    //   +0x18 LastWritten (FILETIME, 8)
    //   +0x20 CredentialBlobSize (uint)
    //   +0x28 CredentialBlob (ptr)
    private const uint CRED_TYPE_GENERIC = 0x1;

    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CredRead(
        string target, uint type, uint reserved, out nint credential);

    [LibraryImport("advapi32.dll")]
    public static partial void CredFree(nint buffer);

    /// <summary>
    /// 从 Windows 凭据管理器读取目标名为 <paramref name="target"/> 的通用凭据，
    /// 返回其密码（CredentialBlob，UTF-16LE）。找不到返回 null。
    /// </summary>
    public static string? ReadCredential(string target)
    {
        if (!CredRead(target, CRED_TYPE_GENERIC, 0, out var ptr) || ptr == nint.Zero)
            return null;
        try
        {
            var blobSize = Marshal.ReadInt32(ptr, 0x20);  // CredentialBlobSize
            var blobPtr = Marshal.ReadIntPtr(ptr, 0x28);  // CredentialBlob
            if (blobSize <= 0 || blobPtr == nint.Zero)
                return null;
            var bytes = new byte[blobSize];
            Marshal.Copy(blobPtr, bytes, 0, blobSize);
            return System.Text.Encoding.Unicode.GetString(bytes);
        }
        finally
        {
            CredFree(ptr);
        }
    }
}
