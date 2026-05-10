using Microsoft.Win32;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Exceptions;

namespace Mouse2Joy.VirtualPad;

public enum ViGEmStatus
{
    Available,
    BusNotInstalled,
    Unknown
}

public static class ViGEmHealth
{
    public static ViGEmStatus Probe()
    {
        try
        {
            using var client = new ViGEmClient();
            // Constructor throws VigemBusNotFoundException if the bus driver isn't installed.
            return ViGEmStatus.Available;
        }
        catch (VigemBusNotFoundException)
        {
            return ViGEmStatus.BusNotInstalled;
        }
        catch
        {
            return ViGEmStatus.Unknown;
        }
    }

    /// <summary>Cheap pre-check via the registry; not authoritative.</summary>
    public static bool RegistryHintInstalled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\ViGEmBus");
            return key is not null;
        }
        catch
        {
            return false;
        }
    }
}
