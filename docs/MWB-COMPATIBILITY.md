# Mouse Without Borders compatibility: technical analysis

This document explains what happens to MousePulse's simulated input when Microsoft PowerToys
**Mouse Without Borders** (MWB) is running. It is based on the MWB source code
(`microsoft/PowerToys`, `src/modules/MouseWithoutBorders/App/`, `main` branch, read on 2026-11-24).
For what was actually measured, see [TEST-RESULTS.md](TEST-RESULTS.md).

## Short answer

| Where is the pointer? | MWB setting on the PC running MousePulse | Effect on the other PC |
|---|---|---|
| On the **other** PC | any | MousePulse's moves are **forwarded** as real mouse moves. |
| On **this** PC | *Block screen saver on other machines* = **On** (default) | About every **60 s**, MWB sends an "awake" packet. The other PC then **makes its own small mouse move**. |
| On **this** PC | *Block screen saver on other machines* = **Off** | **Nothing** reaches the other PC. |

MousePulse does not talk to MWB or to the network. Every effect on the other PC comes from MWB's own
documented behaviour, which treats MousePulse's `SendInput` events like any other input.

## Evidence from the source

### 1. Injected input is not filtered

`Class/InputHook.cs`, `MouseHookProc` (WH_MOUSE_LL):

```csharp
Event.InputEventCount++;
if (!RealData) { RealData = true; rv = CallNextHookEx(...); }   // MWB's own injected events
else {
    Event.RealInputEventCount++;                                // everything else, incl. other apps' SendInput
    ...
```

- MWB never inspects `LLMHF_INJECTED` or `dwExtraInfo`.
- MWB recognises its own injections only through a static `RealData` flag, which it sets just before its own `SendInput`.
- A move from MousePulse is therefore counted and handled exactly like a hardware move.
- While the pointer is on a remote machine, the hook swallows the event (`rv = 1`) and forwards it.

### 2. "Awake" beat (Block screen saver on other machines)

`Form/frmScreen.cs` runs a 100 ms timer. Every 600 ticks (60 s) it calls `Common.SendAwakeBeat()`, provided `BlockScreenSaver` is on:

```csharp
internal static void SendAwakeBeat() {
    if (!RunOnLogonDesktop && !RunOnScrSaverDesktop && WinAPI.IsMyDesktopActive() &&
        Setting.Values.BlockScreenSaver && lastRealInputEventCount != Event.RealInputEventCount)
        SendPackage(ID.ALL, PackageType.Awake);
    ...
```

- The packet goes to **all** connected machines, whichever machine the pointer is on.
- It is sent only if the hook saw at least one non-MWB input event during the last 60 s.
- MousePulse's moves count as such events.

The receiver handles it in `Core/Receiver.cs` and `Core/Common.cs`:

```csharp
case PackageType.Awake: ... Common.HumanBeingDetected();

internal static void HumanBeingDetected() {
    if (lastInputEventCount == Event.InputEventCount)          // no local input since last check
        if (!RunOnLogonDesktop && !RunOnScrSaverDesktop && WinAPI.IsMyDesktopActive())
            PokeMyself();                                       // random 0..9 px move and back
```

- The receiver does **not** check its own "Block screen saver" setting; only the sender's setting matters.
- `PokeMyself` injects input on the receiver. Windows normally counts injected input in `GetLastInputInfo`, but MWB moves the pointer through the WinRT `InputInjector` when it is available. Whether that resets the receiver's idle timer has to be measured on the receiver.
- Administrators can disable the feature through Group Policy (`MwbDisallowBlockingScreensaver`). MousePulse then has no effect on remote PCs while the pointer is local. That is intended.

### 3. Edge switching is position-based

`Core/MachineStuff.cs`, `MoveToMyNeighbourIfNeeded`:

- Any `WM_MOUSEMOVE` whose position is within 1 px of a screen edge facing a neighbour can switch control to that machine. This includes a zero-length move.
- MousePulse's [`NudgePlanner`](../src/MousePulse/Core/NudgePlanner.cs) therefore:
  - always moves **toward the centre** of the current monitor;
  - when the pointer is already within 2 px of an edge, sends **one inward move only**, and does not return the pointer to the edge.
- A simulated move therefore never pushes the pointer onto another PC.

### 4. What MousePulse shows

The **Mouse Without Borders** card reads only local information:

- the process list: `PowerToys.MouseWithoutBorders`, `PowerToys.MouseWithoutBordersService`, and the legacy `MouseWithoutBorders`;
- `%LOCALAPPDATA%\Microsoft\PowerToys\MouseWithoutBorders\settings.json`: machine matrix, `BlockScreenSaverOnOtherMachines`, `TCPPort`;
- the local TCP table: established connections on `TCPPort` / `TCPPort+1` (15100/15101 by default).

The card cannot see which machine the pointer is on, or whether the remote idle timer was reset. Only
the remote PC can tell; use **Record idle log** there (see the README).

## Technical limitations

1. **Up to ~60 s of granularity.** With the pointer on this PC, the other PC is poked at most once per
   MWB beat (60 s), and only if MousePulse moved during that window. Use a move interval of 60 s or less.
   The other PC's idle time then peaks at roughly 60–120 s, so its screen-saver/lock timeout must be longer than 2 minutes.
2. **Remote lock and screen-saver policies win.** A remote PC that is already locked (logon or secure desktop) is
   never poked, because `IsMyDesktopActive()` fails there. Nothing is unlocked or bypassed.
3. **This PC is locked.** MousePulse pauses, and MWB does not send awake beats from a locked desktop.
4. **UIPI.** When an elevated window has focus, Windows discards input injected by a non-elevated process.
   MousePulse reports "not registered" in that case. It never requests elevation.
5. **Unverified until measured:** whether the receiver's `InputInjector`-based poke resets its
   `GetLastInputInfo`, and whether this PC's own idle timer is reset while MWB swallows the move (pointer on
   the remote PC). [TEST-RESULTS.md](TEST-RESULTS.md) records what was measured and what is still open.
