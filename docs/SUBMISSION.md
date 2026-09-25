# Marketplace submission notes

State of the package as built by `./tools/package.sh`, and the reasoning behind the choices a
reviewer is most likely to ask about.

## Package

| Field | Value |
|---|---|
| Name | `CopilotCLI` |
| Display name | Copilot CLI |
| Version | 1.0.0 |
| Artifact | `bin/CopilotCLI_1_0_0.lplug4` (6 files, 64 KB) |
| Verified | `logiplugintool verify` — OK |
| Installed test | passed — see below |

## What is blocking submission

**The three public URLs do not resolve.** `homePageUrl`, `supportPageUrl` and `licenseUrl` all
point at `github.com/messeb/logi-copilotcli-plugin`, which does not exist; the repository has never
been pushed and has no remote. Review rejects dead links, and the Developer Agreement separately
requires the EULA to be reachable at a URL.

They cannot be satisfied by a private repository: reviewers and users have to open them. Either
publish the repository, or host `EULA.md` and `PRIVACY.md` somewhere reachable and point the
manifest there.

Nothing else is outstanding. Re-check with:

```sh
for u in $(grep -Eo "https://[^ ]+" src/package/metadata/LoupedeckPackage.yaml); do
  printf '%s %s\n' "$(curl -s -o /dev/null -w '%{http_code}' -L "$u")" "$u"
done
```

## Checklist

- [x] `LoupedeckPackage.yaml` present in `metadata/`.
- [x] `Icon256x256.png` present in `metadata/`, exactly 256×256.
- [x] Claimed OS support matches reality: `pluginFolderMac` only, no `pluginFolderWin`. Session
      focus uses `warp://session` deep links and `osascript`, neither of which has a Windows
      equivalent, so claiming Windows would be false.
- [x] `PluginApi.dll` is **not** shipped. `tools/package.sh` fails the build if it appears.
- [x] `minimumLoupedeckVersion: 6.4` — the assembly targets `net10.0` and will not load on the
      older .NET 8 based services.
- [x] `supportedDevices: [MxCreativeKeypad]` — the plugin is a grid of tiles you read and press.
- [x] `pluginCapabilities: [HasNoApplication]`, matching `Plugin.HasNoApplication`.
- [x] `homePageUrl` and `supportPageUrl` set, so Options+ does not fall back to loupedeck.com.
- [x] EULA supplied naming the developer as licensor (`docs/EULA.md`), linked from
      `licenseUrl`. MIT is the code grant; the Developer Agreement requires the EULA separately.
- [x] Privacy statement (`docs/PRIVACY.md`). No network code, no telemetry.
- [x] Release build ships no debug symbols.

## Things a reviewer may query

**It writes into `~/.copilot/`.** Only two paths, and only one of them without being asked:

- `~/.copilot/keypad/` — the plugin's own directory, created on load. Mode 700, files 600.
- `~/.copilot/hooks/copilot-keypad.json` — written **only** after a confirmed double press on
  the Set up key, never on load. Removing it is a single `rm`, and the plugin offers that as the
  second press too.

Copilot CLI loads each file in `hooks/` independently, so the plugin never reads, edits or
risks corrupting configuration written by the user or by another tool.

**It shells out.** Three commands, no shell interpretation in any of them:

- `/usr/bin/open "warp://session/<uuid>"` — the URL is rebuilt from a UUID validated as 32
  lowercase hex, and re-checked in `WarpFocus` rather than trusted from the caller.
- `git rev-parse` inside the hook script, for the project name and branch.
- `ps` inside the hook script, to find the owning `copilot` process.

**It captures prompt text.** The first 60 characters of the prompt that started the turn, and
only so two sessions in one checkout are distinguishable on a tile. No tool arguments, no tool
output, no conversation. Documented in `docs/PRIVACY.md`.

**Name collision.** GitHub ships `CopilotAppMac`, for the Copilot *desktop* app, over the
websocket endpoints it advertises in `~/.copilot/run/logitech-*.json`. This plugin is for the
*CLI*, uses a different name and different mechanism, and does not touch those endpoints.

## Not affiliated

Not affiliated with, endorsed by or sponsored by GitHub, Warp or Logitech. Stated in
`docs/EULA.md` §4.
