# Troubleshooting

## Working out why a tile is in the wrong list

Set `COPILOT_KEYPAD_DEBUG=1` in the environment of a Copilot session and the hook appends one line
per event to `~/.copilot/keypad/debug.log`, including the events it deliberately ignores:

```
2026-09-25 14:02:11 uuid=8af658b9 event=preToolUse     type=-                 done -> busy
2026-09-25 14:02:14 uuid=8af658b9 event=notification   type=shell_completed   busy -> ignored (not a question for you)
2026-09-25 14:02:19 uuid=8af658b9 event=agentStop      type=-                 busy -> done
```

An event that changed nothing is usually the interesting one when the question is "why did this
session never move to Waiting". Off by default, never rotated — delete the file to reset it.

## Why `permissionRequest` does not mean "waiting"

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


## Two traps worth knowing

Both produce the identical, unhelpful error `Cannot load plugin from '<path>.dll'`:

1. **Shipping `PluginApi.dll`.** The reference must be `<Private>false</Private>`.
2. **Having no `ClientApplication` subclass**, even for a plugin bound to no application. Hence
   `CopilotCLIApplication`, which deliberately claims nothing.

Unrelated and benign: `Cannot load plugin ... because plugin 'X' is already loaded`. The service
enumerates twice and stock plugins log the same pair.

