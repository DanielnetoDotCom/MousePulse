# Test results – MousePulse 1.0.0

Test date: 2026-11-24.

## Test environment

| Item | Value |
|---|---|
| Primary PC | Windows 11 Pro 10.0.26300, x64, two monitors (1920×1080 and 2560×1080, 100 % scaling) |
| Pointer settings | *Enhance pointer precision* **on** (acceleration active) |
| Mouse Without Borders | PowerToys 0.102.3101, running, connected to one secondary laptop over the LAN |
| MWB options | *Block screen saver on other machines* = on, *Move mouse relatively* = off, Easy Mouse on |
| Policies | No enforced idle-lock policy. Screen saver disabled. |
| Antivirus | Microsoft Defender, real-time protection on |
| Toolchain | .NET SDK 10.0.404, per-user install, no admin |

## Summary

| Area | Result |
|---|---|
| Unit tests (66) | ✅ Pass |
| Schedule (automatic on/off) | ✅ Pass (unit tests + live start/stop on the published exe) |
| Real `SendInput` / `GetLastInputInfo` behaviour on the primary PC | ✅ Pass (after fixing one defect) |
| End-to-end UI behaviour (published exe) | ✅ Pass |
| Resource usage | ✅ 0.08 % CPU in tray, ~0.8 % visible, ~23–32 MB private memory |
| Mouse Without Borders detection | ✅ Pass (after fixing one defect) |
| MWB forwarding mechanism (source analysis) | ✅ Established, see [MWB-COMPATIBILITY.md](MWB-COMPATIBILITY.md) |
| **Secondary PC's idle timer actually reset** | ⏳ **Not verified.** Needs a measurement on the secondary PC (procedure below). |

## 1. Unit tests

`dotnet test tests\MousePulse.Tests`: **66 passed, 0 failed**.

| Area | What is covered |
|---|---|
| Activity engine | Never injects when stopped or while the user is active. First move exactly at the threshold, then at the configured interval. Own moves don't count as user activity. Real input stops simulation and restarts the threshold. Locked session pauses it. Enforced policy blocks it. Unregistered and failed `SendInput` are reported. `GetTickCount` wrap-around. *Test move*. Live setting changes. |
| Nudge planner | Moves go toward the monitor centre and back. Edge pixels get a single inward step (MWB-safe). Zero-distance moves. Negative-coordinate monitors. Distance clamping. |
| Schedule | Mon–Fri 08:00–17:00 boundaries (start inclusive, end exclusive). Weekends. Next change (same day; Friday evening to Monday). Overnight windows. All-day. No days selected. Start/stop transitions. Launch inside or outside the window. Sleep across the end time. Disabled schedule. |
| Settings | Round trip, including the schedule with readable day names. Older files without a schedule. Corrupt or missing file. Out-of-range values are clamped. |
| Policy detection | Machine inactivity limit, enforced secure screen saver (REG_SZ values), MDM device lock. Non-enforcing combinations. Real-registry read without admin. |
| MWB | PowerToys `settings.json` parsing. Self-name matching (NetBIOS truncation). Compatibility advice for every combination. |

## 2. Real-machine integration (`tests/MousePulse.IntegrationHarness`)

The harness waits until nobody is using the PC (≥ 1.5 s without input) before each measurement.

| Check | Result |
|---|---|
| `SendInput` with moves of 0, 1, 3 and 10 px resets `GetLastInputInfo` | ✅ 12/12. The update is **synchronous**: visible immediately after `SendInput` returns, `lii - tick ≤ 16 ms`. |
| Pointer returns to exactly the same pixel | ✅ 12/12 after the fix below. |
| Drift over 40 consecutive 1 px moves | ✅ None (start = end). |
| Live engine run (threshold 5 s, interval 3 s, 40 s) | ✅ Waited 5 s, then moved every 3 s. *Windows idle timer* stayed ≤ 2 s while *Your inactivity* rose to 40 s, so own moves were never mistaken for the user. |
| Engine while someone was using the PC (first run) | ✅ Stayed in *Watching* for the whole 20 s and injected nothing. |

### Defects found and fixed

1. **Pointer drift with acceleration.**
   - Symptom: with *Enhance pointer precision* on, a relative `+d / −d` pair ended 1–2 px away from its start in 5 of 12 runs.
   - Absolute-coordinate moves were measured as an alternative and rejected: the documented and commonly used normalisation formulas missed the target pixel in 38 to 55 of 56 samples.
   - Fix: keep relative moves, which behave like a real mouse for MWB, then restore the exact position with `SetCursorPos`. That call is not an input event. It only runs for offsets of 3 px or less; larger offsets come from the user or from MWB parking the pointer.
2. **The app listed itself as an MWB peer.**
   - `Environment.MachineName` is the 15-character NetBIOS name (`…-OFFI`), while MWB stores the full host name (`…-Office1`).
   - Fix: compare against the DNS host name as well. Covered by unit tests.
3. **CPU usage with the window visible was 2.2 % of one core.**
   - Cause: auto-sized labels updated every second re-laid out the whole form, and each MWB check enumerated the process list four times.
   - Fix: fixed-size live labels, one process snapshot, and MWB/screen-saver checks every 30 s and only while visible.
   - Now 0.8 % visible, 0.08 % hidden in the tray.
4. **`build.ps1` failed on Windows PowerShell 5.1** (`$PSScriptRoot` in a `param()` default). Fixed.

## 3. End-to-end UI tests (published `dist\MousePulse.exe`, driven through UI Automation)

| Scenario | Result |
|---|---|
| Launch, layout, MWB card and policy card shown correctly | ✅ (screenshot reviewed) |
| **Start** → after 5 s idle the status is *Simulating activity*; moves every 5 s, each registered | ✅ |
| Input from another process while simulating (stands in for the user) → *Watching – you are active* within 1.5 s; resumes only after a new full idle threshold | ✅ |
| **Stop** → *Stopped*; Start re-enabled, Stop disabled | ✅ |
| *Start with Windows* on → `HKCU\…\Run\MousePulse = "<exe>" --tray`; off → entry removed; `settings.json` updated | ✅ |
| Title-bar ✕ (`SC_CLOSE`) with *Keep running in the tray* → window hidden, process alive | ✅ |
| Minimize with *Minimize to tray* → hidden. A second launch restores it (`Normal`). | ✅ |
| `--tray` launch → no window. A second launch shows the existing window. Only one instance runs. | ✅ |
| Second launch while running → exits with code 0 and activates the first instance | ✅ |
| `--probe --seconds 6 --out x.csv` → CSV with one row per second; idle resets recorded | ✅ |
| Schedule Mon–Fri 08:00–17:00, launched Tuesday 14:26 → started automatically (Start disabled, Stop enabled) | ✅ |
| Schedule end set to 14:28 → at 14:28 the status became *Stopped*: "The schedule starts it tomorrow at 08:00" | ✅ |
| External `WM_CLOSE` (e.g. `CloseMainWindow`, sign-out tools) → app exits normally instead of hiding | ✅ (intended) |

Not exercised on the real machine:

- **Locking the session** would have locked the user's PC. The engine's pause logic is unit-tested; the `SessionSwitch` wiring is not.
- **Enforced policy block.** Writing Group Policy keys on this PC was avoided; the detection rules are unit-tested instead.
- **Tray context-menu clicks.**
- **Scaling other than 100 %.** The UI uses `PerMonitorV2` with DPI auto-scaling, but was only seen at 100 %.

## 4. Resource usage (published exe)

| State | CPU (one core) | Private memory |
|---|---|---|
| Hidden in the tray, simulating | 0.08 % (47 ms per minute) | 27 MB |
| Window visible, simulating | 0.8 % | 32 MB |
| Just launched, stopped | – | 23 MB |

The single-file bundle is 102 MB on disk, uncompressed and self-contained. Windows Forms cannot be trimmed.

## 5. Antivirus

Microsoft Defender, with real-time protection on, raised **no detections** across every build, publish and launch in this session.

## 6. Mouse Without Borders: two-computer verification

### What was established

- **From the MWB source code** (see [MWB-COMPATIBILITY.md](MWB-COMPATIBILITY.md)):
  - MWB does not filter third-party injected input.
  - With the pointer on the remote PC, MousePulse's moves are forwarded.
  - With the pointer local and *Block screen saver on other machines* on, MWB sends an "awake" packet about every 60 s, and the receiving PC makes its own small mouse move.
- **On the primary PC:**
  - MWB was detected as running and connected to the laptop, with the correct settings.
  - MousePulse's moves never switched control to the laptop. The pointer stayed on the primary screen throughout every test, because moves always go toward the screen centre and never return to an edge pixel.

### What was not verified, and why

Only the primary PC was under test control. The secondary laptop could not be accessed, so whether its
**`GetLastInputInfo` timer is actually reset** has **not been measured**. MWB's local logs on the primary
PC contain no entries for today, so the awake packets could not be observed from this side either.

Unverified points:

1. The secondary PC's idle timer resets when the pointer is on the **primary** PC (awake-packet path).
2. The secondary PC's idle timer resets when the pointer is on the **secondary** PC (forwarding path).
3. Whether the primary PC's own idle timer is reset while MWB swallows the move (pointer on the secondary PC). MousePulse shows "last one NOT registered" if it is not.

### Procedure to complete it (about 25 minutes)

On the **secondary** PC:

1. Copy `dist\MousePulse.exe` and run it. Do **not** press Start.
2. Click **Record idle log (10 min)**, or run `MousePulse.exe --probe --seconds 600`.

On the **primary** PC:

3. Run MousePulse with **Start after inactivity = 30 s**, **Move every = 30 s**, then press **Start**.

Test A, pointer on the primary PC:

4. Leave the pointer on the primary screen and don't touch either PC for 10 minutes.
5. On the secondary PC, open `Desktop\MousePulse-idle-<name>-<time>.csv`:
   - **Pass:** `idle_ms` keeps dropping back to near 0, roughly every 60 s, and never exceeds ~130 000.
   - **Fail:** `idle_ms` climbs steadily to ~600 000.

Test B, pointer on the secondary PC:

6. Start a new log on the secondary PC. Move the pointer onto the secondary screen and stop touching both PCs.
7. **Pass:** `idle_ms` resets about every 30 s.
8. On the primary PC, check *Simulated moves* for point 3: *last one registered* or *NOT registered*.

| Test | Expected (from the source analysis) | Measured |
|---|---|---|
| A: pointer on primary, Block screen saver on | Secondary idle resets about every 60 s | *pending* |
| B: pointer on secondary | Secondary idle resets every move interval | *pending* |
| B: primary registers its own swallowed moves | Unknown | *pending* |
| C (optional): repeat A with Block screen saver **off** | No resets on secondary | *pending* |
