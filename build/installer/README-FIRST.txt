Mouse2Joy — read me first
=========================

Mouse2Joy emulates an Xbox 360 / XInput gamepad on Windows and maps mouse +
keyboard input to its sticks, triggers, buttons, and d-pad.

Mouse2Joy MUST run as Administrator. The Interception driver requires admin
rights to attach. If you launch it normally the UI still opens, but the Setup
tab will report what is missing.


Required free drivers (install once, before running Mouse2Joy)
--------------------------------------------------------------

1) ViGEmBus (virtual gamepad, MIT-licensed)
   Download:  https://github.com/nefarius/ViGEmBus/releases
   Install:   run the latest ViGEmBus_*.msi. No reboot needed.

2) Interception (mouse capture driver, LGPL, non-commercial use)
   Download:  http://www.oblita.com/interception
   Install:   extract the archive, then from an *elevated* command prompt run:
                install-interception.exe /install
              REBOOT after install. The kernel driver is not active until
              after a reboot.

Both drivers are external dependencies because of licensing and signing
requirements. Mouse2Joy bundles only the user-mode interception.dll shim,
not the kernel driver itself.


First run
---------

1. Right-click Mouse2Joy and choose "Run as administrator".
2. Open the Setup tab — it confirms driver and admin state and tells you
   exactly what is missing if anything fails.
3. Profiles tab: create or pick a profile, add bindings.
4. Toggle emulation with the configurable hotkey, or panic-stop any time
   with Ctrl+Shift+F12 (always-on, even if the engine is wedged).


Storage
-------

User data lives under %APPDATA%\Mouse2Joy\:
  profiles\<name>.json   — one file per profile
  settings.json          — app-level settings
  logs\mouse2joy-*.log   — rolling Serilog log (7-day retention)


Source code and issue tracker
-----------------------------

  https://github.com/Zanges/Mouse2Joy

Licensed under GNU GPL v3.0 — see LICENSE.
See THIRD_PARTY_NOTICES.md for licensing of bundled and linked components.
