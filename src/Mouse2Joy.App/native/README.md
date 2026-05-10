# Native dependencies — Interception

Mouse2Joy depends on **Interception** (https://github.com/oblitum/Interception)
for kernel-level mouse and keyboard capture.

The Interception **kernel driver** is not bundled. Users install it once via
`install-interception.exe /install` from an elevated cmd, then reboot. The
in-app Setup tab guides this.

The Interception **user-mode shared library** (`interception.dll`) IS bundled
in this folder for archival robustness. The repo's `.csproj` copies it next
to the built `Mouse2Joy.exe` so the P/Invoke layer can load it.

## Architecture

Mouse2Joy publishes as `RuntimeIdentifier=win-x64` and runs as a 64-bit
process on every supported target (Windows 10/11 x64). Only the x64 DLL is
needed and bundled; the x86 DLL from upstream is not used.

## Verifying the bundled binary

`x64/interception.dll.sha256` pins the SHA-256 of the upstream-canonical
binary that was committed. To confirm a checked-out copy matches:

```powershell
(Get-FileHash src/Mouse2Joy.App/native/x64/interception.dll -Algorithm SHA256).Hash
# compare to the contents of x64/interception.dll.sha256
```

## License — LGPL relinking

`interception.dll` is distributed under the LGPL (non-commercial terms of
the dual-licensed Interception project — see `THIRD_PARTY_NOTICES.md` at
the repo root). LGPL grants you the right to **replace this binary with a
modified version**:

1. Get the source from https://github.com/oblitum/Interception
2. Build it (or modify, then build) following the upstream instructions.
3. Replace `interception.dll` next to `Mouse2Joy.exe` (or in this folder
   before rebuild) with your build.

Mouse2Joy uses only the documented C ABI of Interception (`interception_*`
functions), which matches LGPL's requirement that communication with the
library happens through its public interface.

**Commercial use:** if you intend to ship Mouse2Joy commercially, remove
the bundled DLL and obtain a commercial Interception license
(francisco@oblita.com) before redistributing.
