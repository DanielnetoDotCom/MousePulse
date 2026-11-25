# MousePulse

A small Windows desktop app (C# / .NET 10 / Windows Forms) that keeps a **personal or authorized** PC
from going idle. It makes tiny simulated mouse moves, **only while you are away**.

- Uses the documented Win32 APIs `GetLastInputInfo` (idle detection) and `SendInput` (simulation).
  `SetCursorPos` puts the pointer back on the exact pixel when pointer acceleration leaves a 1–3 px offset.
- Does nothing while you use the mouse or keyboard. The moves stop as soon as you come back.
- Pauses while Windows is locked, and refuses to run when an enforced idle-lock policy is detected.
- Detects Microsoft PowerToys **Mouse Without Borders** and explains what reaches the other PCs.
- Needs no administrator rights, makes no network connections, installs no hooks and has no external dependencies.

---

## Using it

1. Run `MousePulse.exe`. No installation is needed.
2. Set the three values in the **Simulation** card:

   | Setting | Meaning | Default |
   |---|---|---|
   | Start after inactivity of | How long you must be idle before the first move. | 60 s |
   | Move every | Interval between moves while you stay idle. | 30 s |
   | Move distance | Pixels per move (0–50). The pointer goes toward the screen centre and straight back. **0** sends a zero-length move: still an input event for Windows, but nothing visibly moves. | 1 px |

3. Press **Start**. Use **Test move** to send a single move right away.

The **Activity** card shows two idle times:

- **Your inactivity:** time since your last *real* input. MousePulse's own moves are excluded.
- **Windows idle timer:** what Windows sees, i.e. `GetLastInputInfo`. MousePulse keeps this low.

It also shows the time of the last simulated move, when the next one is due, and whether Windows
registered the last move.

### Schedule (automatic on/off)

In the **Schedule** card, tick **Start and stop automatically on these days**, choose the days, and set the
*From*/*To* times. Example: Mon–Fri, 08:00–17:00.

- At the start time MousePulse starts by itself; at the end time it stops. As usual, moves still happen only while you are idle.
- If the app is launched, or the PC wakes from sleep, inside the window, it starts immediately. If the PC slept past the end time, it stops on wake-up.
- Pressing **Start** or **Stop** yourself overrides the schedule until the next scheduled change. For example, Start on Saturday keeps it on until Monday 17:00.
- If the end time is earlier than the start time, the window crosses midnight: Fri 22:00–06:00 covers Saturday morning. Equal times mean the whole day.
- The schedule only works while MousePulse is running, so also turn on **Start with Windows**. The card shows a warning when it is off.

### Application options

- **Start with Windows** adds a per-user entry under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. The app then starts minimized in the tray.
- **Start simulating when MousePulse launches.**
- **Minimize to the system tray** and **Keep running in the tray when closed.** Left-click the tray icon to open the window; right-click it for Start, Stop and Exit.
- Preferences are saved to `%APPDATA%\MousePulse\settings.json`.

Only one instance runs at a time. Launching the exe again brings the running window to the front.

### Status values

| Status | Meaning |
|---|---|
| Stopped | Simulation is off. |
| Watching – you are active | Running. You used the PC within the threshold, so nothing is simulated. |
| Simulating activity | You have been idle longer than the threshold. Moves are being sent. |
| Paused – session locked | Windows is locked. Nothing is simulated; the app resumes after you unlock. |
| Blocked by security policy | An enforced idle-lock policy exists (see below). Nothing is simulated. |

---

## Mouse Without Borders

The **Mouse Without Borders** card reads only local data:

- whether MWB is installed, running and connected;
- the configured peer PCs;
- the linked addresses, from the local TCP table;
- the *Block screen saver on other machines* setting.

What reaches the other PC depends on where the pointer is. The table below comes from the MWB source code; see
[docs/MWB-COMPATIBILITY.md](docs/MWB-COMPATIBILITY.md) for the details.

| Pointer is on… | Effect on the other PC |
|---|---|
| the other PC | MousePulse's moves are forwarded to it as real mouse moves. |
| this PC, *Block screen saver on other machines* **on** | About every 60 s, MWB tells the other PCs that someone is active here, and each of them makes its own small mouse move. Keep **Move every ≤ 60 s**. |
| this PC, that option **off** | Nothing reaches the other PC. |

### Verifying the other PC (recommended)

This PC cannot observe the other PC's idle timer. To check it:

1. Copy `MousePulse.exe` to the **other** PC.
2. Run it there. You do **not** need to press Start. Click **Record idle log (10 min)**.
   - Command-line alternative: `MousePulse.exe --probe --seconds 600 --out C:\temp\idle.csv`.
3. On the **primary** PC, press Start, leave the pointer on the primary screen and stop using both PCs.
4. When the 10 minutes are up, open the CSV on the other PC (`idle_ms` column):
   - **Works:** `idle_ms` drops back regularly, about every 60 s.
   - **Doesn't work:** `idle_ms` grows steadily for the whole 10 minutes.
5. Optionally repeat with the pointer parked on the other PC's screen.

The measured results are in [docs/TEST-RESULTS.md](docs/TEST-RESULTS.md).

---

## Security and policy behaviour

- **Enforced idle locks are respected.** If MousePulse finds any of the following, simulation is disabled and the UI says why:
  - Group Policy *Interactive logon: Machine inactivity limit* (`InactivityTimeoutSecs`);
  - an enforced, password-protected screen saver (`Software\Policies\...\Control Panel\Desktop`);
  - the MDM `DeviceLock/MaxInactivityTimeDeviceLock` policy.
- **Locked sessions are never touched.** Simulation pauses on lock and on disconnect, and resumes on unlock.
- **No elevation, no UIAccess.** When an elevated window has focus, Windows (UIPI) discards injected input. MousePulse reports this ("not registered") instead of trying to get around it.
- **No hooks, injection, obfuscation or network access.** Idle detection uses `GetLastInputInfo` only.
- **AV-friendly packaging.** Standard Win32 API usage, an embedded manifest (`asInvoker`) and full version metadata. The single-file bundle is left uncompressed, because compressed bundles look "packed" to some heuristics. For best results, sign the exe with your own code-signing certificate.

---

## Building

Requirements: Windows 10/11 x64 and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```powershell
.\build.ps1             # runs the unit tests, then publishes dist\MousePulse.exe
.\build.ps1 -SkipTests
```

Manual equivalent:

```powershell
dotnet test  tests\MousePulse.Tests
dotnet publish src\MousePulse -c Release -r win-x64 -o dist
```

The output is a self-contained, single-file `MousePulse.exe` of about 100 MB. It runs on PCs without .NET installed.

- Windows Forms does not support trimming, so the bundle contains the full runtime.
- Adding `-p:EnableCompressionInSingleFile=true` shrinks it to roughly 45 MB, at the cost of the "packed" look mentioned above.

### CI/CD and versioning

[.github/workflows/build.yml](.github/workflows/build.yml) runs on GitHub Actions (`windows-latest`):

- **Every push to `main`:** runs the tests, publishes `MousePulse.exe`, and creates a GitHub Release `vX.Y.N` with the exe and its SHA-256 attached.
- **Pull requests:** tests and build only. The exe is available as a workflow artifact; no release is created.
- **Version:** `X.Y` comes from `<Version>` in `src/MousePulse/MousePulse.csproj`; `N` is the workflow run number, so it goes up automatically. To start a new major/minor line, change `<Version>` (for example to `1.1.0`).

Download the latest build from the repository's **Releases** page.

Real-machine integration check (it really moves the pointer by a few pixels, and waits until nobody is using the PC):

```powershell
dotnet run --project tests\MousePulse.IntegrationHarness -- --engine-seconds 40
```

## Project layout

```
MousePulse.slnx
build.ps1                          test + publish script
src/MousePulse/
  Program.cs                       entry point, single instance, --probe / --tray
  Core/ActivityEngine.cs           idle state machine (testable, no UI)
  Core/Schedule.cs                 weekly on/off window and start/stop transitions
  Core/NudgePlanner.cs             move geometry (edge-safe for Mouse Without Borders)
  Core/Win32SystemInput.cs         GetLastInputInfo / SendInput implementation
  Core/PolicyInspector.cs          enforced idle-lock policy detection
  Core/MwbDetector.cs, MwbAdvice.cs  Mouse Without Borders detection and explanation
  Core/IdleProbe.cs                idle-timer CSV recorder (for verifying the remote PC)
  Core/ScreenSaverInfo.cs          screen saver timeout (configuration hints)
  Settings/                        JSON preferences and the HKCU Run entry
  UI/MainForm.cs, Theme.cs         Windows Forms UI and tray
  Native/NativeMethods.cs          P/Invoke declarations (user32)
tests/MousePulse.Tests/            xUnit unit tests
tests/MousePulse.IntegrationHarness/  manual real-machine checks
tools/New-AppIcon.ps1              regenerates the icon
docs/                              MWB analysis and test results
```
