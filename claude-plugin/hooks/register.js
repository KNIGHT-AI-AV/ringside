// Ringside's Claude Code mod.
//
// After each turn, and whenever a plan limit moves a whole point, Claude Code hands mods the
// same 5-hour and weekly limits its status line gets. This mod writes them to the small file
// the Ringside meter reads, so the meter stays current in every place Claude Code runs: the
// terminal, the Claude desktop app's Code tab and the VS Code extension.
//
// It reads no credentials, calls no network and touches no other file.

let last = ''
let lastAt = 0

async function feedPath($) {
  const custom = await $.env.get('RINGSIDE_DATA')
  if (custom) return `${custom}/claude-statusline.json`
  const appData = await $.env.get('APPDATA')
  if (appData) return `${appData}\\Ringside\\claude-statusline.json`
  const home = await $.env.get('HOME')
  if (!home) return null
  if (await $.fs.exists(`${home}/Library/Application Support`)) return `${home}/Library/Application Support/Ringside/claude-statusline.json`
  const xdg = await $.env.get('XDG_CONFIG_HOME')
  return `${xdg || `${home}/.config`}/Ringside/claude-statusline.json`
}

async function publish($, rateLimits) {
  const windows = {}
  for (const w of rateLimits ?? []) {
    if (w.kind !== 'five_hour' && w.kind !== 'seven_day') continue
    const item = { used_percentage: Math.round(w.percentUsed * 100) / 100 }
    const resets = w.resetsAt ? Date.parse(w.resetsAt) : NaN
    if (!Number.isNaN(resets)) item.resets_at = Math.floor(resets / 1000)
    windows[w.kind] = item
  }
  if (Object.keys(windows).length === 0) return
  const shape = JSON.stringify(windows)
  const now = await $.clock.now()
  // The same reading again only refreshes the time, at most once a minute.
  if (shape === last && now - lastAt < 60_000) return
  last = shape
  lastAt = now
  const path = await feedPath($)
  if (path) await $.fs.write(path, JSON.stringify({ updated: Math.floor(now / 1000), ...windows }))
}

export function register(on) {
  on('session.measure', async ($, e, next) => {
    try {
      await publish($, e.rateLimits)
    } catch {
      // A meter must never get in the way of the session.
    }
    return next(e)
  })
}
