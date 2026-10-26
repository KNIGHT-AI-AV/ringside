# Changelog

## 2.0.4 (2026-10-26)

- The welcome card now says your usage stays on your computer. It used to say nothing leaves it, but update checks do reach GitHub.
- Check for updates now says what GitHub sees: your IP address, and maybe Ringside's version and your system type.

## 2.0.3 (2026-10-26)

- Updates get through even when GitHub is busy. If GitHub's API turns the update check away, which it can do when many people share one internet connection, Ringside now checks the latest release's own download link instead.
- After Ringside updates itself, it says so once: a notification names the new version, and clicking it opens what's new.
- Settings, About has a What's new button, and Check for updates now says when it couldn't reach GitHub instead of saying you're up to date.

## 2.0.2 (2026-10-22)

- Codex: Ringside now keeps up with a Codex thread you come back to over several days. Codex keeps adding a resumed thread to the log for the day the thread started, and Ringside only looked at logs from recent days, so the Codex meter could stop moving and keep showing an older reading. Ringside now reads whichever log Codex wrote most recently.

## 2.0.1 (2026-10-16)

- The Claude app's usage log is written only now and then, often right as a limit fills up. Once the 5-hour window could have reset since its last entry, Ringside now shows a dash and says the window may have reset, instead of an old 100%. Turn on Live Claude limits for live numbers.
- New pictures in the welcome cards and in Settings, Claude Code show Live Claude limits where Claude Code runs: the terminal, the Claude app's Code tab and VS Code.
- The welcome card that lists what Ringside found now calls Claude Code's part Live limits.
- The demo GIF ends with Live Claude limits.

## 2.0.0 (2026-10-16)

Ringside now runs on Windows, macOS and Linux, and has a new look and much more to set.

- macOS (Apple silicon) and Linux builds, from one codebase.
- Live Claude limits: a small Claude Code plugin keeps Claude's numbers current after every turn, in the terminal, the Claude app's Code tab and VS Code. It reads no credentials.
- Old readings now say when they were taken, such as yesterday 9:09 PM, and never pass for current numbers.
- New welcome cards that walk through setup with pictures: what Ringside found, your view, used or left, colors, alerts and the Claude Code link.
- Three views: the full card, the slim pill, and the orb, a ring inside a ring like the logo.
- Show limits as used (filling up) or as left (draining like a tank). Every number, tooltip, alert and the Claude Code status line follow the choice.
- Pick any color for each app, or use traffic light colors.
- Split Claude and Codex into two meters you can place anywhere; drop one onto the other to join them again.
- Show only one app's meter if you use only one. Ringside starts with just the one it finds.
- Smart forecast: when a limit climbs fast, Ringside shows when it will run out at your current pace, and can warn you before it does.
- Your own amber and red thresholds, rings or bars on the card, countdown or clock reset times, and a choice of which limit the main gauge follows.
- A right-click quick panel for views, used or left, split or join, keep on top and click-through.
- A new Settings window with a live preview.
- Notifications drawn by Ringside, the same on every system.

## 1.0.0 (2026-10-15)

First release.

- Always-on-top meter for Claude and Codex usage limits, with a compact mode.
- Claude limits from the Claude desktop app's usage log, with an estimated reset time.
- Optional Claude Code status line link for exact Claude reset times and the weekly limit.
- Codex 5-hour and weekly limits, plan and credits from Codex's session logs.
- Notifications at 75% and 90% and when a limit resets.
- Tray icon that shows both limits as rings.
- Hides over full-screen apps, click-through mode, size and opacity settings.
- One-click installer with automatic updates from GitHub releases.
