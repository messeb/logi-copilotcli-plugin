# Marketplace listing copy

Paste-ready text for the Logitech Marketplace submission form. Kept in the repository so the
listing and the plugin cannot drift apart — if the behaviour changes, this changes with it.

## Teaser card (max 120 characters)

```
Your Copilot CLI sessions on three keys: what's working, what's waiting, one press to jump there.
```

97 characters.

## Detail page description

```
Run more than one GitHub Copilot CLI session and the same two things keep happening: you sit
watching a session that has been busy for four minutes, or a session quietly waits for you while
you work somewhere else.

Copilot CLI puts all of them on your MX Creative Keypad, where you can see them without switching
windows or breaking your train of thought.

THREE KEYS

• Active — how many sessions are working right now. Open it for the full list, longest-running
  first, each tile showing the project, the branch and how long it has been at it.

• Waiting — how many sessions actually want you: one is asking permission to run something, or has
  finished its turn and is holding for your next prompt. Blocked sessions sort to the top and the
  key turns red, so a number here always means something needs doing.

• Sessions — everything that is open at all, working or not, when you just want to know how much
  is running.

Press any tile and you land in that terminal pane, in the right window, on the right tab.

HOW IT WORKS

The plugin connects to Copilot CLI through its own hooks file. Press Set up twice and it writes a
single file; press twice again and it removes it. It never edits configuration belonging to you or
to another tool, and it reads nothing until you connect it.

Tiles update as sessions work, so the keypad tracks what is happening rather than what was
happening when you last looked.

REQUIREMENTS

• macOS
• GitHub Copilot CLI
• Logi Options+ 6.4 or newer
• Warp or iTerm2 — sessions in other terminals are simply not shown
• iTerm2 focus needs Automation permission for Logi Plugin Service, which macOS asks for once

PRIVACY

Everything stays on your machine. The plugin has no network code and no telemetry. It records a
session's state, project name, branch and the first 60 characters of your prompt — enough to tell
two sessions apart on a tile — and never the conversation or what your tools returned.
```
