using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace UCREW.SecurePatch;

internal static class DeviceIdentity
{
    public static string GetHwid()
    {
        string machineGuid = ReadMachineGuid();
        string sid = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        string raw = string.Join(
            "|",
            machineGuid,
            sid,
            Environment.MachineName,
            Environment.OSVersion.VersionString);

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string ReadMachineGuid()
    {
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Cryptography",
                writable: false);

            return key?.GetValue("MachineGuid")?.ToString() ?? "unknown-machine";
        }
        catch
        {
            return "unknown-machine";
        }
    }
}
