using System.Runtime.Versioning;
using Pigeon.World.Desktop.Capabilities;

namespace Pigeon.Desktop.Linux;

/// <summary>Проверки, общие для X11 и Wayland: logind, Secret Service, XDG autostart.</summary>
[SupportedOSPlatform("linux")]
internal static class LinuxCommon
{
    public static string OsVersion()
    {
        try
        {
            foreach (var line in File.ReadLines("/etc/os-release"))
            {
                if (line.StartsWith("PRETTY_NAME=", StringComparison.Ordinal))
                {
                    return line["PRETTY_NAME=".Length..].Trim('"');
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return Environment.OSVersion.VersionString;
    }

    public static CapabilityCheck SessionEvents() =>
        Directory.Exists("/run/systemd/seats")
            ? new(Capability.SessionEvents, CapabilityStatus.Available, "logind (D-Bus): PrepareForSleep, Lock/Unlock", Verified: false)
            : new(Capability.SessionEvents, CapabilityStatus.Unknown, "logind", "systemd-logind не найден", Verified: false);

    /// <summary>Secret Service нужен keyring-демон в сессии; без D-Bus-сессии его точно нет.</summary>
    public static CapabilityCheck SecretStore() =>
        Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS") is null
            ? new(Capability.SecretStore, CapabilityStatus.Limited, "Secret Service (libsecret)",
                "нет D-Bus-сессии: fallback — зашифрованный файл с предупреждением", Verified: false)
            : new(Capability.SecretStore, CapabilityStatus.Limited, "Secret Service (libsecret)",
                "наличие keyring-демона проба не проверяет", Verified: false);

    public static CapabilityCheck Autostart() =>
        new(Capability.Autostart, CapabilityStatus.Available, "XDG autostart (~/.config/autostart/*.desktop)", Verified: false);
}
