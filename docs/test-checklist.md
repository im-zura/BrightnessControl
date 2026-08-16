# Manual test checklist — v1.3.0

The automated suite (`dotnet test`) covers the logic. Everything below needs **real DDC/CI
hardware** and has to be walked through by hand.

After each step, check the log: `%AppData%\Brightness Control\log.txt`
(previous file: `log.prev.txt`).

## Already verified on this machine (2026-08-16/17)

Some of these are automated against the attached monitors — they are skipped unless you opt in:

```powershell
$env:BC_HARDWARE_TESTS="1"
dotnet test --filter Category=Hardware -l "console;verbosity=detailed"
```

| Step | Result |
|---|---|
| 3.2 / 3.3 switch off → back on | ✅ Display 2 left the desktop, stayed off, and came back responsive |
| 3.1 power button only on the secondary | ✅ primary rejected (`refusing to switch off the primary display`) |
| 2.1 game start → quit | ✅ 40% applied on start, schedule level restored on quit |
| 2.2 game killed ~1s after start | ✅ ended at the schedule level and **stayed** — no late 40% write |
| 2.7 per-monitor game targeting | ✅ `applying game profile 'War Thunder' at 40% to mon-xmi2001-…` |
| 4.9 startup registry path | ✅ repaired a stale `Desktop\…\bin\Debug` entry automatically |
| verified brightness write | ✅ 10% → 30% → restored, read back each time |
| stable monitor identity | ✅ both monitors keyed by device path, config migrated v1 → v3 |

**Ruled out on this hardware** — DDC power (VCP `0xD6`): value `4` is accepted and ignored (the
panel blinks and wakes back up while Windows keeps driving the output); value `5` sticks but kills
the monitor's DDC circuit, so only its physical button brings it back. The app uses the Windows
display topology instead.

Zura confirmed by hand on 2026-08-17: switching the monitor off and on **at its own physical
button** while the app is running (section 1), and switching it off and on **from the app**
(3.2/3.3) — both work.

**Still open:** 2.3–2.6, 3.4–3.7, 4.1–4.8, and the cable/sleep/resume steps in section 1.

---

## 1. Displays that come and go

| # | Steps | Expected |
|---|---|---|
| 1.1 | Turn the second monitor **off** at its own power button, then start the app | Flyout shows only the main display. Log: one `monitors:` line with one entry |
| 1.2 | With the app running, turn the second monitor **on** | Within ~2s it appears in the open flyout and in the log. Its slider moves it |
| 1.3 | With the app running, turn the second monitor **off** | It disappears from the flyout. No error in the log, tray icon still there |
| 1.4 | Unplug the HDMI/DP cable, wait 5s, plug it back in | Same as 1.2 — reappears with its saved brightness |
| 1.5 | Sleep the PC, wake it | Both displays are back and at their schedule/idle level. Log shows `resume from sleep` |
| 1.6 | Lock (Win+L), unlock | No duplicate monitors, no error |
| 1.7 | Change resolution in Windows Settings | Log shows `display settings changed`; sliders still work |

## 2. Brightness after a game

| # | Steps | Expected |
|---|---|---|
| 2.1 | Launch RDR2, wait for the profile to apply, quit the game | Brightness returns to the schedule level (**20% day / 10% night** in the current config), not 50% |
| 2.2 | Launch a game and **kill it within 1 second** (Task Manager) | Brightness returns to idle and **stays** there. Log: `abandoned — superseded` or `dropped — superseded` |
| 2.3 | Alt-tab out of a fullscreen game and back in | No brightness change, no error |
| 2.4 | Turn the schedule **off** in Settings, repeat 2.1 | Brightness returns to the idle level instead |
| 2.5 | While a game is running, change brightness with `Ctrl+Alt+↑` | Brightness changes, but on quitting the game it returns to the everyday level — the mid-game tweak is **not** remembered |
| 2.6 | With no game running, change brightness with `Ctrl+Alt+↑`, quit and restart the app | The new level is restored |
| 2.7 | Run a game on the **second** monitor | Only that monitor dims; the main one is untouched |

## 3. Turning a screen off (new)

| # | Steps | Expected |
|---|---|---|
| 3.1 | Open the flyout | The **second** display's card has a power button. The main one does **not** |
| 3.2 | Press it | That screen goes dark within ~1s and stays dark. Its card is replaced by a row reading "Display 2 · off" with a button to bring it back |
| 3.3 | Press that button | The screen comes back with its old resolution and position |
| 3.4 | Turn the second screen off, then right-click the tray icon → **Turn all screens on** | It comes back |
| 3.5 | Turn the second screen off, then exit the app (tray → Exit) | It comes back on before the app closes |
| 3.6 | Turn the second screen off, start a game on the main one | The game runs normally; the dark screen stays dark |
| 3.7 | Turn the second screen off, kill the app from Task Manager, start it again | It is restored at startup — a screen is never stranded off |

> ℹ️ Windows sitting on the second screen move to the main one when it is switched off, and stay
> there afterwards. That is the same thing that happens when the monitor is switched off by hand.

## 4. Everything else still works

| # | Steps | Expected |
|---|---|---|
| 4.1 | Drag a slider | Brightness follows smoothly, no lag |
| 4.2 | Scroll the wheel over the tray icon | Brightness changes in steps of 5 |
| 4.3 | `Ctrl+Alt+↑` / `↓` | Brightness changes in steps of 20 |
| 4.4 | "All monitors" master slider | Both displays move together |
| 4.5 | Add / edit / delete a game profile | Saved, tile updates, tracking follows |
| 4.6 | Cross a day/night boundary (or move the times in Settings) | Brightness switches blocks. Log shows the profile being applied |
| 4.7 | Contrast slider under **Advanced** | Contrast changes (only on monitors that expose it) |
| 4.8 | Launch the app a second time | No second tray icon; the flyout of the running instance opens |
| 4.9 | Exit and check `HKCU\...\Run` | Points at the **installed** exe, not an old build folder |

## 5. Log health

- No `ERROR` lines during normal use.
- The app is still in the tray after an hour of use with a game started and stopped.
- `log.txt` rolls to `log.prev.txt` at ~1 MB instead of growing forever.

---

## Automated suite

```powershell
dotnet test "E:\imzura-claude\PROJECTS\BrightnessControl\BrightnessControl.sln"
```

65 tests: monitor identity/migration, schedule resolution, the superseded-apply guard,
write verification and retry, hot-plug handle replacement, power-off guardrails,
and the process watcher's start/stop/dedupe/crash-proofing.
