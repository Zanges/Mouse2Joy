using System.Security.Principal;
using Microsoft.Win32;
using Mouse2Joy.Input.Native;

namespace Mouse2Joy.Input;

public enum InterceptionStatus
{
    Available,
    DllNotFound,
    DriverNotInstalled,
    AdminRequired,
    Unknown
}

public static class DriverHealth
{
    public static InterceptionStatus Probe()
    {
        if (!IsAdministrator())
        {
            return InterceptionStatus.AdminRequired;
        }

        if (!IsDriverServiceRegistered())
        {
            return InterceptionStatus.DriverNotInstalled;
        }

        nint ctx = 0;
        try
        {
            ctx = InterceptionNative.CreateContext();
            if (ctx == 0)
            {
                return InterceptionStatus.DriverNotInstalled;
            }

            return InterceptionStatus.Available;
        }
        catch (DllNotFoundException)
        {
            return InterceptionStatus.DllNotFound;
        }
        catch
        {
            return InterceptionStatus.Unknown;
        }
        finally
        {
            if (ctx != 0)
            {
                try { InterceptionNative.DestroyContext(ctx); } catch { /* ignore */ }
            }
        }
    }

    public static bool IsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Cheap pre-check via the registry. Interception installs upper-filter
    /// services named 'keyboard' and 'mouse'; both should be present.
    /// </summary>
    public static bool IsDriverServiceRegistered()
    {
        try
        {
            using var kb = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\keyboard");
            using var ms = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\mouse");
            return kb is not null && ms is not null;
        }
        catch
        {
            return false;
        }
    }
}
