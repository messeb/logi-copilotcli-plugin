# Privacy

CopilotCLI sends nothing anywhere. It has no network code, no telemetry and no analytics.

## What it reads

- `~/.copilot/keypad/sessions/*.json` — the state files its own hook script writes.
- The `WARP_TERMINAL_SESSION_UUID` environment variable of each Copilot CLI session, read
  inside that session by the hook script.

## What it writes

- `~/.copilot/keypad/keypad-hook.sh` — the hook script, extracted from the plugin assembly.
- `~/.copilot/keypad/sessions/<warp-uuid>.state.json` — the session's current state and two
  timestamps.
- `~/.copilot/keypad/sessions/<warp-uuid>.meta.json` — project name, git branch, working
  directory, Copilot session id, process id, and the first 60 characters of the prompt that
  started the turn.
- `~/.copilot/hooks/copilot-keypad.json` — the hook registration, and only when you confirm it
  with a double press on the Set up key.

All of it stays on your machine. The state directory is created with mode 700 and the files
with mode 600.

## What it does not read

Your conversations. The hook records that a tool ran, never what it did or what it returned.
The only text captured from a session is the first 60 characters of your prompt, and only so
two sessions in the same checkout can be told apart on a tile.

## Removing everything

Press Set up twice to disconnect, then:

```sh
rm -f ~/.copilot/hooks/copilot-keypad.json
rm -rf ~/.copilot/keypad
```
