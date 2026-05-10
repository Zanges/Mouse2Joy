# Third-party notices

Mouse2Joy bundles or links the following third-party software.

---

## ViGEmBus / ViGEm.NET

- Project: https://github.com/nefarius/ViGEmBus, https://github.com/nefarius/ViGEm.NET
- Author: Benjamin Höglinger-Stelzer (Nefarius Software Solutions)
- License: MIT

Used to expose a virtual Xbox 360 / XInput gamepad on Windows.

---

## Interception

- Project: https://github.com/oblitum/Interception
- Author: Francisco Lopes
- License: Dual-licensed.
  - **Non-commercial use:** LGPL, with explicit permission to redistribute the
    library and driver binaries when communication with the drivers happens
    solely through the documented library API. Mouse2Joy redistributes
    `interception.dll` under these terms.
  - **Commercial use:** requires a commercial license obtained from
    francisco@oblita.com before redistribution.

Used for kernel-level mouse + keyboard capture so emulation can swallow real
input while a virtual pad is active.

The kernel driver itself is **not** bundled — users install it once with
`install-interception.exe /install` (elevated, reboot required), as documented
in the Setup tab.

---

## CommunityToolkit.Mvvm, Microsoft.Extensions.*, Serilog, xunit, FluentAssertions

Standard NuGet dependencies; respective licenses (MIT / Apache-2.0) ship with
their packages and are downloaded at build time.

## Hardcodet.NotifyIcon.Wpf

- Project: https://github.com/hardcodet/wpf-notifyicon
- License: Code Project Open License (CPOL) 1.02
