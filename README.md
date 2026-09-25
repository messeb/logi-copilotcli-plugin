# CopilotCLI — GitHub Copilot CLI on the MX Creative Keypad

Three keys for your GitHub Copilot CLI sessions, running in **Warp or iTerm2**:

- **Active** — how many sessions are working right now. Opens a folder listing them.
- **Waiting** — how many genuinely want something from you. Opens a folder listing them, blocked
  ones first.
- **Sessions** — how many sessions are open at all, working or not. Opens a folder listing every
  one of them.

Press any tile to jump straight to that Warp pane.

Active and Waiting are deliberately narrow, so that a number on either key always means something
needs doing. Everything else — a session that is open but has never been asked anything, or one in
a state this build does not recognise — appears under Sessions.

macOS only. Requires GitHub Copilot CLI, Logi Options+ 6.4 or newer, and Warp or iTerm2.

## Why

Run three or four Copilot CLI sessions across Warp panes and you get one of two failure modes:
you sit watching a session that is busy for four minutes, or a session sits waiting for you
while you work elsewhere and nobody notices. A keypad fixes both, because it is glanceable
without stealing focus.

## Setup

1. Build and install the plugin (see [Building](#building)), or install it from the Marketplace.
2. In Logi Options+ the plugin appears as **Copilot CLI**. Drag **Active sessions**,
   **Waiting sessions** and **All sessions** onto three keys — all three live under its
   *Sessions* group.
3. Open either folder and press **Set up** twice. The second press writes
   `~/.copilot/hooks/copilot-keypad.json`.
4. Start a Copilot CLI session in a Warp pane. A tile appears.

To disconnect, press **Set up** twice again, or just delete that one file. The plugin never
touches anything else in your Copilot configuration.

## How it works

Copilot CLI loads every `*.json` under `~/.copilot/hooks/` independently, so the plugin owns
exactly one file and never edits one you or another tool wrote. That file runs
`keypad-hook.sh` on eight session events; the script records each session's state under
`~/.copilot/keypad/sessions/`, and the plugin polls that directory once a second.

Sessions are keyed by whichever terminal they are in:

| | Identity | Focus |
|---|---|---|
| **Warp** | `WARP_TERMINAL_SESSION_UUID` — a 32-hex pane UUID | `open warp://session/<uuid>` |
| **iTerm2** | the GUID half of `ITERM_SESSION_ID` (`w0t0p0:GUID`) | AppleScript: find the session with that `id`, select it |

Both reduce to the same 32-hex filename once dashes are dropped and letters lowercased, so one
state directory serves both. The identifier is never used as a filename directly — the name is
*derived* from it, which is what stops a hostile value becoming a path.

For iTerm2, only the GUID identifies the session. The `w0t0p0` prefix is the pane's **position**
and changes the moment you drag a pane to another tab; keying on it would split one session into
two tiles. Warp sets no variable in iTerm and vice versa, but if both are somehow present Warp
wins, since a Warp pane is a Warp pane whatever else is exported into it.

| Hook event | Tile says | Folder |
|---|---|---|
| `sessionStart` | idle | Sessions only |
| `userPromptSubmitted` | working | Active, Sessions |
| `preToolUse`, `postToolUse` | working | Active, Sessions |
| `permissionRequest` | working | Active, Sessions |
| `notification` (permission prompt / elicitation) | ASKING YOU | Waiting, Sessions |
| `agentStop` | your turn | Waiting, Sessions |
| `sessionEnd` | tile removed | — |

Only `attention` and `done` count as waiting: one means the agent is asking you a question, the
other that a turn ended and nothing moves until you reply. An idle session wants nothing, so it
would only pad the one list that is supposed to mean "act now".

### Working out why a tile is in the wrong list

Set `COPILOT_KEYPAD_DEBUG=1` in the environment of a Copilot session and the hook appends one line
per event to `~/.copilot/keypad/debug.log`, including the events it deliberately ignores:

```
2026-09-25 14:02:11 uuid=8af658b9 event=preToolUse     type=-                 done -> busy
2026-09-25 14:02:14 uuid=8af658b9 event=notification   type=shell_completed   busy -> ignored (not a question for you)
2026-09-25 14:02:19 uuid=8af658b9 event=agentStop      type=-                 busy -> done
```

An event that changed nothing is usually the interesting one when the question is "why did this
session never move to Waiting". Off by default, never rotated — delete the file to reset it.

### Why `permissionRequest` does not mean "waiting"

It looks like the obvious signal and it is the wrong one. GitHub documents it as firing
"before the permission service runs — before rule checks, session approvals,
auto-allow/auto-deny, and user prompting", and it was measured firing under
`--allow-all-tools`.

An earlier version of this plugin tried to rescue it by waiting two seconds before believing
it. That failed against a real session: running `sleep 4` held the tile in "blocked" for the
full four seconds, because the gap between `permissionRequest` and `postToolUse` is the
*tool's* duration, not yours. No threshold separates a slow tool from a blocked prompt.

So `permissionRequest` means working, and the real alarm is `notification` with a
`notification_type` of `permission_prompt` or `elicitation_dialog`.

That filtering happens in `keypad-hook.sh`, on the payload, rather than through a `matcher` in the
hook file. A matcher is one untestable string: if Copilot's matcher semantics differ in any way
from what was assumed, every notification is dropped silently and a blocked session never reaches
the Waiting tile — a failure whose only symptom is the alarm quietly never firing. The filter
matters either way, because `notification` also covers `shell_completed`,
`shell_detached_completed`, `agent_completed` and `agent_idle`, and taking those would turn a
finished background shell into a blocked tile.

## If the buttons only appear for some applications

They are on the keypad's **Default General Profile**, and Options+ swaps to an application's own
profile whenever that application has one. So the buttons show for every app *without* a
profile, and vanish for the ones that have one — which reads as "it only works in Warp" if Warp
happens to be profile-less on your machine.

The plugin is already universal (`HasNoApplication`); nothing in it can override profile
switching. Two ways to fix it, depending on whether you want per-app profiles at all:

**Keep per-app profiles** — add the buttons to each application profile that exists. Quit Options+
and run:

```sh
python3 tools/add-buttons-to-profiles.py          # shows what it would do
python3 tools/add-buttons-to-profiles.py --apply  # writes, backing up each file first
```

It adds only the buttons a profile is missing, prefers free keys over new pages, and is safe to
re-run. Do so after creating any new application profile.

**Drop per-app switching for the keypad** — turn off "follow active application", so the
keypad always shows the default profile. In
`~/Library/Application Support/Logi/LogiPluginService/LoupedeckSettings.ini` that is:

```ini
CurrentApplication/FollowActiveApplication_Loupedeck70=False
```

Quit Logi Plugin Service before editing it — the file says so itself, and Options+ restarts the
service from a launchd agent, so quit Options+ first or it will respawn.

To check where your buttons currently live:

```sh
cd ~/Library/Application\ Support/Logi/LogiPluginService/Applications/Loupedeck70
grep -rl CopilotCLI . 
```

## Known limits

- **Warp and iTerm2 only.** A session in Terminal.app, Ghostty or a plain SSH shell exports
  neither identifier, so the hook ignores it. Nothing breaks; the session simply gets no tile.
- **iTerm2 needs an Automation permission.** Focusing a session there goes through AppleScript,
  and macOS gates that: the first press raises a prompt, or silently fails if it was denied once.
  Allow Logi Plugin Service under System Settings → Privacy & Security → Automation. The plugin
  reports this as an error with a fix link rather than leaving the tile looking broken. Warp's
  deep link needs no permission at all.
- **The folder buttons' counts can lag.** `PluginDynamicFolder` has no `ButtonImageChanged`, so
  the plugin cannot directly order a repaint of the key that opens a folder. It asks the host
  three ways — it stays subscribed to session changes for the plugin's whole life rather than only
  while the folder is open, it bakes each session's state into the action names so the list the
  host sees actually changes, and it calls the plugin-level `OnActionImageChanged` for the folder's
  own action. If a count still looks stale, leaving the folder and coming back redraws it.
- **A session killed with SIGKILL** never delivers `sessionEnd`. Its tile disappears on the next
  poll anyway, because the plugin checks the recorded PID.

## Building

Needs .NET 10 and Logi Options+ installed.

```sh
export DOTNET_ROOT=/opt/homebrew/opt/dotnet/libexec   # Homebrew's location is non-standard
dotnet build src/CopilotCLIPlugin.csproj -c Debug
```

The build writes a `.link` file into the plugin directory and asks the service to reload, so
the plugin is live without installing anything. Check it loaded:

```sh
tail ~/Library/Application\ Support/Logi/LogiPluginService/Logs/plugin_logs/CopilotCLI.log
```

### Packaging

```sh
./tools/package.sh          # produces bin/CopilotCLI_1_0_0.lplug4
```

## Publishing a C# plugin to the Marketplace

A `.lplug4` is a zip with a fixed shape — `bin/` beside `metadata/`, with `LoupedeckPackage.yaml`
and `Icon256x256.png` in `metadata/`. Users install one by double-clicking it. This is what it
takes to get one accepted.

### 1. Get the packaging tool

```sh
dotnet tool install --global LogiPluginTool
```

It lands in `~/.dotnet/tools`, which is **not** on `PATH` by default — `which logiplugintool` then
reports nothing and it looks uninstalled. Add that directory to `PATH`, or call it by full path.
On a machine with only .NET 10 it also needs roll-forward, because the tool targets .NET 8:

```sh
export PATH="$HOME/.dotnet/tools:$PATH"
export DOTNET_ROLL_FORWARD=Major
logiplugintool --help          # generate | pack | verify | metadata
```

### 2. Get the manifest right

`metadata/LoupedeckPackage.yaml`. Mandatory: `type`, `name`, `displayName`, `version`, `author`,
`supportPageUrl`, `license`, `licenseUrl`. Four fields bite:

- **`name` is a permanent, immutable ID.** `[a-zA-Z0-9_-]+`, and it **must not end in "Plugin"**.
  It cannot be changed after publication, so choose it as carefully as a package name. The
  assembly may be `CopilotCLIPlugin.dll`; the plugin `name` may not be `CopilotCLIPlugin`.
- **`version` must increase on every submission** — `major.minor[.build]`, all decimal.
- **`license` must be MIT or Apache 2.0.** GPL2/GPL3 are rejected outright.
- **Claimed OS support must match reality.** Ship `pluginFolderMac` only for a macOS-only plugin
  and omit `pluginFolderWin` entirely; review checks the claim against what the plugin actually
  does.

### 3. Pack Release, and check what went in

```sh
dotnet build src/YourPlugin.csproj -c Release
logiplugintool pack ./bin/Release/ ./YourPlugin_1_0_0.lplug4
logiplugintool verify ./YourPlugin_1_0_0.lplug4
```

Name the file `pluginName_version.lplug4`. Then look inside, because the most common rejection is
**shipping the host's own assemblies**:

```sh
unzip -l YourPlugin_1_0_0.lplug4 | grep -Ei 'PluginApi|Newtonsoft|YamlDotNet|Svg|ExCSS|\.pdb|\.DS_Store'
```

That must return nothing. `<Private>false</Private>` on the `PluginApi` reference keeps the DLL
*and its dependency closure* out; `DebugType=none` for Release keeps `.pdb` files out, which are
dead weight and leak your local source paths. A stale `.DS_Store` in `bin/` survives rebuilds, so
check the packaged tree rather than the source tree. `tools/package.sh` in this repo fails the
build rather than let any of that ship.

### 4. Do not touch the user's configuration at install time

`Plugin.Install()`, `Uninstall()` and `InstallAdmin()` exist and run during package installation.
Do not use them to write anything outside your plugin's own data directory. Installing is a click
on a listing, not consent to edit someone's config, and Logitech QA has rejected a plugin for
exactly this.

Put it behind a key the user placed themselves and pressed — that is real consent, and it comes
with an obvious gesture to reverse it. This plugin's Set up key needs two presses within five
seconds before it writes `~/.copilot/hooks/copilot-keypad.json`, and the same key removes it.

### 5. Supply the legal documents

- **Your own EULA.** The Marketplace Developer Agreement requires one naming you as licensor and
  stating that Logitech is not a party to it. **An MIT licence alone does not satisfy this** —
  MIT is the code grant, the EULA is a separate document. Point `licenseUrl` at it and publish it
  somewhere reachable.
- **A privacy statement** if you touch personal data. Audit rather than assume: grep your source
  for `HttpClient`, `System.Net`, sockets and any external URL, and list every path you read or
  write and every process you launch.

### 6. Check every link resolves

Review rejects dead links, and `homePageUrl`, `supportPageUrl` and `licenseUrl` are all public.

```sh
for u in <homePageUrl> <supportPageUrl> <licenseUrl>; do
  printf '%s %s\n' "$(curl -s -o /dev/null -w '%{http_code}' -L "$u")" "$u"
done
```

A `#fragment` never reaches the server, so a 200 proves nothing about an anchor — check anchors
against the committed file.

### 7. Test the package, not your dev build

A `.link` file in the Plugins directory shadows an installed package, so a working dev build
proves nothing about what you are shipping. Delete the `.link`, double-click the `.lplug4`, and
confirm from the log that it loaded from the Plugins directory:

```sh
rm -f ~/Library/Application\ Support/Logi/LogiPluginService/Plugins/YourPlugin.link
grep "loaded from" ~/Library/Application\ Support/Logi/LogiPluginService/Logs/plugin_logs/YourPlugin.log
```

Then exercise it on the real hardware, including first-run setup — a Marketplace user never has
your repository, so anything your README tells *you* to run has to be reachable from the device.

Enable and disable the plugin a few times while watching the host's CPU and thread count. Statics
live per `AssemblyLoadContext`, not per process, so a timer or watcher you forget to dispose in
`Unload` roots its whole dead context and the host degrades reload by reload — a reviewer toggling
your plugin will see it.

### 8. Submit

Upload at [marketplace.logitech.com/contribute](https://marketplace.logitech.com/contribute) and
accept the Developer Agreement on your first submission. Review is manual plus automated, with no
guaranteed turnaround — roughly **10 working days** is typical. Every update goes through the same
review, so budget for it.

This plugin's own filled-in version of the above is [docs/SUBMISSION.md](docs/SUBMISSION.md), and
the primary sources are the SDK's
[Distributing the Plugin](https://logitech.github.io/actions-sdk-docs/csharp/plugin-development/distributing-the-plugin/)
page and the
[Marketplace Approval Guidelines](https://logitech.github.io/actions-sdk-docs/marketplace-approval-guidelines/).

## CI

| Workflow | Runs on | Does |
|---|---|---|
| `ci.yml` | pull request, push to `main` | unit tests (ubuntu), hook tests (macOS), shellcheck + `compileall`, and a manifest check for the rules the Marketplace enforces |
| `release.yml` | manual (`workflow_dispatch`) | re-runs the gate, tags `main` from the manifest's version and opens a draft release |

Dependabot watches NuGet and the workflows' actions: minor and patch updates arrive grouped as one
pull request a week, majors individually.

**CI cannot build the plugin.** `PluginApi.dll` ships inside Logi Plugin Service, is not
redistributable, and is on no hosted runner — so `dotnet build src/CopilotCLIPlugin.csproj` and
`./tools/package.sh` only work on a machine with Options+ installed. That is why the test project
links the PluginApi-free source files instead of referencing the plugin: everything carrying logic
stays testable anywhere, and a PluginApi type creeping into one of those files breaks the test
build immediately, which is the intended signal.

For the same reason the release workflow creates the release as a **draft** and does not attach the
`.lplug4`. Build and attach it from a machine that can:

```sh
./tools/package.sh
gh release upload v1.0.0 bin/CopilotCLI_1_0_0.lplug4
```

## Tests

```sh
sh tests/hook/run-tests.sh                                   # the shell hook, 40 assertions
dotnet test tests/CopilotCLI.Tests/CopilotCLI.Tests.csproj # the pure logic, 64 tests
```

The C# tests link the PluginApi-free source files rather than referencing the plugin project,
because `PluginApi.dll` cannot be loaded in a test host. That also keeps the separation honest:
putting a PluginApi type into one of those files breaks the test build immediately.

## Two traps worth knowing

Both produce the identical, unhelpful error `Cannot load plugin from '<path>.dll'`:

1. **Shipping `PluginApi.dll`.** The reference must be `<Private>false</Private>`.
2. **Having no `ClientApplication` subclass**, even for a plugin bound to no application. Hence
   `CopilotCLIApplication`, which deliberately claims nothing.

Unrelated and benign: `Cannot load plugin ... because plugin 'X' is already loaded`. The service
enumerates twice and stock plugins log the same pair.

## Licence

MIT — see [LICENSE](LICENSE) and [docs/EULA.md](docs/EULA.md).
No data leaves your machine; see [docs/PRIVACY.md](docs/PRIVACY.md).
