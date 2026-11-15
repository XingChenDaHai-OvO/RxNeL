using System;
using System.Runtime.InteropServices;
using System.Text;

namespace RxNeL.Accounts;

/// <summary>Windows DPAPI（CryptProtectData）：把密码用当前用户凭据加密，换机器/换用户解不开。</summary>
public static class Dpapi
{
    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref DataBlob dataIn, string? description,
        IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, ref DataBlob dataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(ref DataBlob dataIn, IntPtr description,
        IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, ref DataBlob dataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr handle);

    private const int CryptProtectUiForbidden = 0x1;

    public static string Protect(string plain)
    {
        var input = Encoding.UTF8.GetBytes(plain);

        var inBlob = new DataBlob { Size = input.Length, Data = Marshal.AllocHGlobal(input.Length) };
        var outBlob = new DataBlob();

        try
        {
            Marshal.Copy(input, 0, inBlob.Data, input.Length);

            if (!CryptProtectData(ref inBlob, "RxNeL account", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                    CryptProtectUiForbidden, ref outBlob))
                throw new InvalidOperationException($"DPAPI 加密失败（{Marshal.GetLastWin32Error()}）");

            var result = new byte[outBlob.Size];
            Marshal.Copy(outBlob.Data, result, 0, outBlob.Size);

            return Convert.ToBase64String(result);
        }
        finally
        {
            if (inBlob.Data != IntPtr.Zero) Marshal.FreeHGlobal(inBlob.Data);
            if (outBlob.Data != IntPtr.Zero) LocalFree(outBlob.Data);
        }
    }

    public static string Unprotect(string protectedBase64)
    {
        var input = Convert.FromBase64String(protectedBase64);

        var inBlob = new DataBlob { Size = input.Length, Data = Marshal.AllocHGlobal(input.Length) };
        var outBlob = new DataBlob();

        try
        {
            Marshal.Copy(input, 0, inBlob.Data, input.Length);

            if (!CryptUnprotectData(ref inBlob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                    CryptProtectUiForbidden, ref outBlob))
                throw new InvalidOperationException($"DPAPI 解密失败（{Marshal.GetLastWin32Error()}）");

            var result = new byte[outBlob.Size];
            Marshal.Copy(outBlob.Data, result, 0, outBlob.Size);

            return Encoding.UTF8.GetString(result);
        }
        finally
        {
            if (inBlob.Data != IntPtr.Zero) Marshal.FreeHGlobal(inBlob.Data);
            if (outBlob.Data != IntPtr.Zero) LocalFree(outBlob.Data);
        }
    }
}
