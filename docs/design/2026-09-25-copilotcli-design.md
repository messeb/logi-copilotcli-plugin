# CopilotCLI — design

Live keypad tiles for GitHub Copilot CLI sessions running in Warp panes, on the MX Creative
Keypad. Two assignable buttons: **Active sessions** (the agent is working) and **Waiting
sessions** (your move). Each opens a dynamic folder listing the matching sessions; pressing a
tile jumps to that Warp pane.

Status: approved for implementation, 2026-09-25.

## Why

Running several Copilot CLI sessions across Warp panes means one of two failure modes: either
you sit watching a session that is busy for four minutes, or a session sits blocked on a
permission prompt while you work elsewhere and nobody notices. A keypad is the right surface
for this because it is glanceable without stealing focus — the thing a notification centre
badge cannot do, and the thing a terminal tab bar does badly once there are more than four
tabs.

Success: from a glance at the keypad you know how many sessions need you, and one press puts
you in the right pane.

## Scope

In scope: macOS, Warp, iTerm2, MX Creative Keypad, GitHub Copilot CLI. Marketplace-ready
packaging.

Out of scope: Windows (the focus mechanism does not exist there), terminals that export no session
identifier (Terminal.app, Ghostty), the
GitHub Copilot *desktop* app (already served by GitHub's own `CopilotAppMac` plugin over the
`~/.copilot/run/logitech-*.json` websockets — this plugin must not collide with it), and
sending input to a session.

## Measured facts

Everything in this section was observed on this machine on 2026-09-25, not inferred from
documentation. The probe registered a hook that dumped its environment, stdin and process
tree, then ran one `copilot -p` prompt.

### Copilot CLI hooks

- User-level hooks load from `$COPILOT_HOME/hooks/*.json`, default `~/.copilot/hooks/`. Each
  file is independent, so install is *write one file* and uninstall is *delete one file*. No
  merge into a shared settings file. That matters: where an agent CLI keeps all hooks in one
  shared settings file, a plugin has to do JSON surgery on a file other tools are editing too,
  and unpick exactly its own entries on uninstall.
- Seven of the eight registered events fire in this order for a tool-using turn:
  `userPromptSubmitted`, `sessionStart`, `preToolUse`, `permissionRequest`, `postToolUse`,
  `agentStop`, `sessionEnd`. The eighth, `notification`, fires only when the agent actually
  asks something, which a non-interactive `copilot -p` run never does.
- `userPromptSubmitted` is dispatched *before* `sessionStart` on a new session, and their
  timestamps can be ~70 ms apart. State writes must therefore be ordered by payload
  timestamp, not by arrival.
- The hook process inherits the session's environment, including
  `WARP_TERMINAL_SESSION_UUID`. This is the load-bearing assumption of the whole design and
  it is confirmed.
- Copilot additionally exports `COPILOT_CLI=1`, `COPILOT_CLI_BINARY_VERSION`,
  `COPILOT_PROJECT_DIR` and `COPILOT_CLI_RESOLVED_DIST_DIR`.
- The JSON payload arrives on stdin **without a trailing newline**. Anything reading it
  line-wise will see nothing.
- Payload fields actually observed:

  | Event | Fields beyond `sessionId`, `timestamp`, `cwd` |
  |---|---|
  | `sessionStart` | `source` (`"new"`), `initialPrompt` |
  | `userPromptSubmitted` | `prompt` |
  | `preToolUse` | `toolName`, `toolArgs` |
  | `permissionRequest` | `hookName`, `toolName`, `toolInput`, `permissionSuggestions` |
  | `postToolUse` | `toolName`, `toolArgs`, `toolResult.resultType`, `toolResult.textResultForLlm` |
  | `agentStop` | `transcriptPath`, `stopReason`, `stop_hook_active` |
  | `sessionEnd` | `reason` (`"complete"`) |

- `timestamp` is epoch **milliseconds**, not seconds.

### `permissionRequest` is not a block signal, and no timing trick makes it one

`permissionRequest` fired under `--allow-all-tools`, 78 ms before `postToolUse`. GitHub
documents why: the hook "fires before the permission service runs — before rule checks,
session approvals, auto-allow/auto-deny, and user prompting". It reports that a decision is
being *evaluated*, not that anyone is waiting.

A first design tried to rescue it with a 2-second debounce: treat attention as real only once
it persists. A live run killed that. Driving a session through `sleep 4`, the state sat in
`attention` for the full four seconds, because the gap between `permissionRequest` and
`postToolUse` is **the tool's own duration**, not the user's think time. A blocked session and
a slow tool are indistinguishable by timing, at any threshold.

Resolution: `permissionRequest` maps to `busy`. The real block signal is `notification` with a
`notification_type` of `permission_prompt` ("the agent requests permission to execute a tool")
or `elicitation_dialog` ("the agent requests additional information from the user"). The
hook script narrows `notification` to exactly those two by reading `notification_type` from the
payload.

That filter deliberately does **not** live in the hook file's `matcher`, though Copilot supports
one there. A matcher is a single untestable string, and if its semantics differ in any way from
what was assumed, every notification is dropped silently — a blocked session would then never
reach the Waiting tile, with no symptom other than the alarm never firing. In the script it is
covered by tests. The filter is needed either way: `notification` also covers `shell_completed`,
`shell_detached_completed`, `agent_completed` and `agent_idle`, and taking them all would turn a
finished background shell into a blocked tile.

Verified after the fix: the same `sleep 4` session stays `busy` throughout, and `since` holds
steady while `ts` advances, which is the clock-preservation rule observed working live.

### Copilot's own state files are not usable for live state

`~/.copilot/session-state/<id>/` holds `workspace.yaml` (cwd, git_root, branch, created_at)
and `events.jsonl`. Rejected as a state source: `events.jsonl` is written lazily — an
observed `session.shutdown` event was written 23 hours after the session ended — and it
carries no busy/idle state, no PID and no Warp UUID. It is not a live signal. The hook
computes the same metadata from `cwd` with one `git` call and no coupling to undocumented
internals.

### The host requires a ClientApplication

A plugin with no `ClientApplication` subclass is refused outright with
`Cannot load plugin from '<path>.dll'` — the same message the shipped-`PluginApi.dll` trap
produces, which makes it easy to misdiagnose. `CopilotCLIApplication` exists for that reason
and claims nothing: empty process and bundle names, `ClientApplicationStatus.Unknown`.

### There is no way to push a repaint of a folder's own button

`PluginDynamicFolder` exposes `ButtonActionNamesChanged` and `CommandImageChanged`, but nothing
that invalidates the folder's own button image. Enumerated from the shipped `PluginApi.dll`;
the class has no base type beyond `Object`, so nothing is inherited either.

Consequence: `GetButtonImage` renders the current counts whenever the host asks, but the plugin
cannot make it redraw on its own. The count is fresh when the host redraws the page, not
continuously live, and the Waiting button's blink only animates while its folder is open. A
live count on a home-page key would need a separate `PluginDynamicCommand` widget, which is out
of scope here.

### Platform

- dotnet 10.0.400 via Homebrew; `DOTNET_ROOT=/opt/homebrew/opt/dotnet/libexec` required.
- `PluginApi.dll` at
  `/Applications/Utilities/LogiPluginService.app/Contents/MonoBundle/PluginApi.dll`, .NET 10 —
  so the plugin must target `net10.0` and `minimumLoupedeckVersion: 6.4`.
- A dynamic folder page holds **8** tiles, not 9: the host reserves one of the nine keys even
  when `GetNavigationArea` returns `None`.
- `GetButtonImage(PluginImageSize)` on a `PluginDynamicFolder` renders the folder's *own*
  button, which is how each top-level button carries its count — subject to the repaint limit
  noted above.

## Architecture

```
copilot CLI ──hook event──> keypad-hook.sh ──atomic write──> $STATE/<warp-uuid>.{state,meta}.json
                                                                       │
                                                         1 s poll + PID liveness
                                                                       ▼
                                                                 SessionStore
                                                                  ╱        ╲
                                                   ActiveSessionsFolder  WaitingSessionsFolder
                                                                  ╲        ╱
                                                           press ──> open warp://session/<uuid>
```

`$STATE` is `${COPILOT_HOME:-$HOME/.copilot}/keypad/sessions`.

Sessions are keyed by `WARP_TERMINAL_SESSION_UUID`, not by Copilot's `sessionId`, because the
Warp UUID is what the focus deep link needs and what stays stable across a `/clear`. The value
is validated as exactly 32 lowercase hex characters before it is ever used as a filename.

Two files per session, split by write frequency: `.state.json` is tiny and rewritten on every
tool call; `.meta.json` holds the expensive-to-compute fields (git toplevel, branch, PID) and
is refreshed only on `sessionStart`, `userPromptSubmitted`, or when missing — which is also how
a session that predates the hook heals itself on its next tool call.

## State model

| Event | State | Bucket |
|---|---|---|
| `sessionStart` | `idle` | Sessions only |
| `userPromptSubmitted` | `busy` | Active |
| `preToolUse`, `postToolUse` | `busy` | Active |
| `permissionRequest` | `busy` | Active |
| `notification` (`permission_prompt`, `elicitation_dialog`) | `attention` | Waiting |
| `agentStop` | `done` | Waiting |
| `sessionEnd` | — | removed |

**Active** = `busy`.
**Waiting** = `attention`, `done` — and only those. A session that is merely open has never been
asked anything and wants nothing; putting it in Waiting pads the one list whose number is supposed
to mean "act now".
**All** = every live session, including `idle` and any state this build does not recognise, ranked
attention → busy → done → idle → unknown. It is the answer to "how many sessions have I got open
at all", which neither of the other two can give once both are narrow.

Within the waiting folder, sort `attention` → `done` → `idle`, then by longest-waiting first,
so a blocked session is always the first tile.

The hook additionally refuses to escalate a session that is not already `busy`. That is a
backstop on top of the type filter, and it survives a notification being reworded, because it
reads state rather than message text.

Elapsed time is *time in the current state*: the clock is preserved when a `busy` write follows
a `busy` state, otherwise every tool call would reset a busy tile to 0:00.

## Components

| File | Responsibility |
|---|---|
| `hooks/keypad-hook.sh` | POSIX sh; one state write per event; always exits 0 |
| `src/CopilotCLIPlugin.cs` | Plugin entry; `HasNoApplication` |
| `src/Sessions/SessionSnapshot.cs` | Pure record types; no PluginApi |
| `src/Sessions/SessionClassifier.cs` | Pure bucket/sort/elapsed logic; no PluginApi |
| `src/Sessions/SessionStore.cs` | Poll, parse, PID liveness, GC |
| `src/Sessions/HookFile.cs` | **Pure** hook-JSON generation and shell quoting |
| `src/Sessions/HookWiring.cs` | Extract script, write/remove hook JSON, report wired state |
| `src/Actions/ActiveSessionsFolder.cs` | Busy sessions, count badge on its own button |
| `src/Actions/WaitingSessionsFolder.cs` | Waiting sessions, same |
| `src/Actions/SessionsFolderBase.cs` | Shared paging, tile rendering, press handling |
| `src/Warp/WarpFocus.cs` | `open warp://session/<uuid>` |
| `src/Rendering/TileRenderer.cs` | Project · branch · state · elapsed, colour by state |
| `src/Helpers/PluginLog.cs`, `PluginResources.cs` | Logging, embedded resources |

`SessionSnapshot`, `SessionClassifier`, `SessionFiles` and `HookFile` are deliberately free of
`PluginApi` types so the classification, parsing, hook-file generation and elapsed-formatting
rules are unit-testable in a plain xunit project — `PluginApi.dll` cannot be loaded in a test host. Folders and renderer stay thin
over them.

## Error handling

The hook must never break the session it watches: `exit 0` on every path, `umask 077`, atomic
temp-and-rename writes, and it refuses a state root it does not own (`[ -O "$ROOT" ]`) so
another local user cannot pre-create it.

The plugin's `Load()` does no blocking work. The host gives `Load` a 10 second budget and
silently unregisters the plugin if it overruns; building the session store synchronously is
what did that to the reference plugin. The store warms on a background task.

An empty folder renders one "No active sessions" tile rather than a blank page. Unwired hooks
surface as a plugin status message with a link to the fix.

## Testing

TDD. Shell tests drive `keypad-hook.sh` with recorded payloads from the probe and assert the
resulting JSON — including the clock-preservation rule, the notification guard, the
no-trailing-newline stdin, and rejection of a malformed UUID. xunit covers `SessionClassifier` (buckets, sort order,
liveness, elapsed formatting), `SessionFiles` (parsing, malformed and truncated input, forged
focus URLs, root resolution) and `HookFile` (valid JSON, all eight events, the notification
absence of matchers, and shell quoting for paths containing `"`, `\`, `$` or a backtick).
The hook suite covers the notification type filter directly: both types that mean "the agent is
asking you something" raise the alarm, and `shell_completed`, `shell_detached_completed`,
`agent_completed`, `agent_idle`, an unknown type and a missing type all leave a working tile
alone.

Beyond the suites, the pipeline is verified end to end against a real `copilot` process:
install the generated hook file, run a session, and watch the state files transition.

Two things automation cannot reach, verified by hand on the device: the page-of-8 layout, and
that `warp://session/<uuid>` focuses the right pane.

## Packaging

`net10.0`; `minimumLoupedeckVersion: 6.4`; `supportedDevices: [MxCreativeKeypad]`;
`pluginCapabilities: [HasNoApplication]`; `pluginFolderMac: bin` only, because the design is
macOS-only and Marketplace approval checks the claimed OS support against reality.

`PluginApi.dll` is referenced with `<Private>false</Private>`. Shipping it is the failure mode
that produces a bare `Cannot load plugin from '<path>.dll'`. Conversely,
`Cannot load plugin ... because plugin 'X' is already loaded` is benign — the service
enumerates twice and stock plugins log the identical pair.

The hook script is an `EmbeddedResource` rather than a file beside the DLL: a Marketplace user
never has this repository, and the package directory is removed on uninstall, so the assembly
is the only place it can live and still be extractable.

Ships with `.lplug4` packaging, EULA, PRIVACY and SUBMISSION docs, and a 256×256 icon.
