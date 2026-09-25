#!/bin/sh
# keypad-hook.sh <event> — records one GitHub Copilot CLI session's state for the CopilotCLI
# keypad plugin.
#
# Called from Copilot CLI hooks (~/.copilot/hooks/copilot-keypad.json). The event payload arrives
# on stdin as a single JSON object with NO trailing newline, so it is read with `cat`, never
# line-wise.
#
#   sessionStart         -> idle       session is up, nothing asked yet
#   userPromptSubmitted  -> busy       you asked for something
#   preToolUse           -> busy       a tool is about to run
#   postToolUse          -> busy       a tool finished, the turn continues
#   permissionRequest    -> busy       a tool is being evaluated; see below
#   notification         -> attention   the agent is actually asking you something
#   agentStop            -> done        turn finished, your move
#   sessionEnd           -> removed
#
# permissionRequest is NOT a "user is blocked" signal, by design: GitHub documents it as firing
# "before the permission service runs - before rule checks, session approvals, auto-allow/auto-deny,
# and user prompting", and it was measured firing under --allow-all-tools. The gap between it and
# postToolUse is however long the TOOL takes, not how long you take, so no amount of waiting can
# tell the two apart. It therefore means "working".
#
# The real block signal is notification with a notification_type of permission_prompt ("the agent
# requests permission to execute a tool") or elicitation_dialog ("the agent requests additional
# information from the user").
#
# That filtering happens HERE, on the payload, rather than through a matcher in the hook file.
# A matcher is a single untestable string: if Copilot's matcher semantics differ in any way from
# what was assumed, every notification is silently dropped and a blocked session never reaches the
# waiting tile - a failure with no symptom other than the feature quietly not working. Reading
# notification_type in the script is testable, and an unrecognised type leaves the state alone
# instead of guessing.
#
# Always exits 0: a status display must never be able to break the session it is watching.

set -u
umask 077

ROOT="${COPILOT_KEYPAD_ROOT:-${COPILOT_HOME:-$HOME/.copilot}/keypad}"
SESSIONS="$ROOT/sessions"

# Which terminal is this session in, and what identifies the pane inside it?
#
# Warp exports a 32-hex pane UUID. iTerm2 exports ITERM_SESSION_ID as "w0t0p0:GUID", where the
# prefix is the pane's POSITION - it changes when you move a pane between tabs - and the GUID after
# the colon is the stable identity that matches iTerm's own `id of session`. Only the GUID is kept.
#
# Neither: nothing could be focused later, so there is nothing worth reporting.
TERMINAL=""
SESSION_REF=""

if [ -n "${WARP_TERMINAL_SESSION_UUID:-}" ]; then
    TERMINAL=warp
    SESSION_REF="$WARP_TERMINAL_SESSION_UUID"
elif [ -n "${ITERM_SESSION_ID:-}" ]; then
    TERMINAL=iterm
    SESSION_REF="${ITERM_SESSION_ID#*:}"
else
    exit 0
fi

# The filename is derived from the reference, never taken from it: dashes dropped and letters
# lowercased, which turns both terminals' identifiers into the same 32-hex shape. Anything that is
# not exactly that after normalising is not ours, and in particular cannot be a path.
UUID="$(printf '%s' "$SESSION_REF" | tr -d '-' | tr 'A-F' 'a-f')"
case "$UUID" in
    *[!0-9a-f]* | "") exit 0 ;;
esac
[ ${#UUID} -eq 32 ] || exit 0

mkdir -p "$SESSIONS" 2>/dev/null || exit 0
# Refuse a root another local user created before we could.
#
# shellcheck disable=SC3067  # `test -O` is outside POSIX, but this script only ever runs on macOS,
# where /bin/sh provides it. Dropping the check would be worse than the portability note.
[ -O "$ROOT" ] || exit 0
chmod 700 "$ROOT" 2>/dev/null

EVENT="${1:-agentStop}"
STATE_FILE="$SESSIONS/$UUID.state.json"
META_FILE="$SESSIONS/$UUID.meta.json"
NOW="$(date +%s)"

if [ "$EVENT" = "sessionEnd" ]; then
    rm -f "$STATE_FILE" "$META_FILE"
    exit 0
fi

# stdin is a single JSON object with no trailing newline.
PAYLOAD="$(cat 2>/dev/null)"

json_str() {  # json_str <key> -- first string value for key, from $PAYLOAD
    printf '%s' "$PAYLOAD" | grep -o "\"$1\":\"[^\"]*\"" | head -1 | cut -d'"' -f4
}
json_num() {  # json_num <key> -- first numeric value for key, from $PAYLOAD
    printf '%s' "$PAYLOAD" | grep -o "\"$1\":[0-9][0-9]*" | head -1 | cut -d: -f2
}

read_field() {  # read_field <file> <key> -- flat string-or-number value from a file we wrote
    sed -n "s/.*\"$2\":\"\{0,1\}\([^,\"}]*\)\"\{0,1\}.*/\1/p" "$1" 2>/dev/null
}

# Opt-in trace, for working out why a tile ended up in the wrong list.
#
#   COPILOT_KEYPAD_DEBUG=1  in the environment of the Copilot session
#
# One line per event, including the ones that are deliberately ignored - an event that changed
# nothing is exactly what you need to see when the question is "why did this session never move to
# waiting". Off by default and never rotated here, because a log nobody asked for that grows without
# bound on a session's hot path is its own bug. Delete the file to reset it.
trace() {  # trace <resulting-state-or-verb>
    [ -n "${COPILOT_KEYPAD_DEBUG:-}" ] || return 0
    printf '%s uuid=%.8s event=%-20s type=%-24s %s -> %s\n' \
        "$(date '+%Y-%m-%d %H:%M:%S')" "$UUID" "$EVENT" \
        "${NOTIFICATION_TYPE:--}" "${PREV_STATE:-none}" "$1" \
        >> "$ROOT/debug.log" 2>/dev/null
}

# Copilot's timestamp is epoch MILLISECONDS. It is the ordering key, because the CLI dispatches
# userPromptSubmitted before sessionStart on a new session and the two can arrive ~70 ms apart:
# without this, a late sessionStart would demote a live busy tile back to idle.
EVENT_MS="$(json_num timestamp)"
case "$EVENT_MS" in '' | *[!0-9]*) EVENT_MS="$(( NOW * 1000 ))" ;; esac

PREV_STATE=""
PREV_MS=0
if [ -r "$STATE_FILE" ]; then
    PREV_STATE="$(read_field "$STATE_FILE" state)"
    PREV_MS="$(read_field "$STATE_FILE" event_ms)"
    case "$PREV_MS" in '' | *[!0-9]*) PREV_MS=0 ;; esac
fi

# A payload older than the one already recorded describes the past. Drop it.
if [ "$EVENT_MS" -lt "$PREV_MS" ]; then
    trace "ignored (stale payload)"
    exit 0
fi

# notification covers six unrelated things. Only two of them mean the agent is asking you
# something; the rest - a finished background shell, a finished or idle subagent - must not touch
# a tile, or a completed async shell would read as "blocked".
if [ "$EVENT" = "notification" ]; then
    NOTIFICATION_TYPE="$(json_str notification_type)"
    case "$NOTIFICATION_TYPE" in
        permission_prompt | elicitation_dialog) ;;
        *) trace "ignored (not a question for you)"; exit 0 ;;
    esac

    # Only a turn in flight can be blocked. A notification arriving on a finished turn leaves the
    # tile as it is, which already says "your move". Reading state rather than message text means
    # a reworded notification cannot quietly break the alarm either.
    if [ "$PREV_STATE" != "busy" ] && [ "$PREV_STATE" != "attention" ]; then
        trace "ignored (no turn in flight)"
        exit 0
    fi
fi

case "$EVENT" in
    sessionStart)        STATE='idle' ;;
    userPromptSubmitted) STATE='busy' ;;
    preToolUse)          STATE='busy' ;;
    postToolUse)         STATE='busy' ;;
    permissionRequest)   STATE='busy' ;;
    notification)        STATE='attention' ;;
    agentStop)           STATE='done' ;;
    *)                   STATE='done' ;;
esac

# Elapsed time is "how long in THIS state", so the clock only restarts when the state actually
# changes. Without this, every tool call would reset a busy tile's timer to 0:00.
SINCE="$NOW"
if [ "$STATE" = "$PREV_STATE" ] && [ -r "$STATE_FILE" ]; then
    OLD_SINCE="$(read_field "$STATE_FILE" since)"
    case "$OLD_SINCE" in '' | *[!0-9]*) ;; *) SINCE="$OLD_SINCE" ;; esac
fi

write_atomic() {  # write_atomic <file> <content>
    _t="$1.tmp.$$"

    # Written whole, then renamed over the target, so a reader can never catch a half-written file.
    # Spelled as if/then rather than `A && B || C` so the cleanup path is unambiguous: the temp file
    # is removed whenever either step failed, and never after a successful rename.
    if printf '%s\n' "$2" > "$_t" 2>/dev/null && mv -f "$_t" "$1" 2>/dev/null; then
        return 0
    fi

    rm -f "$_t" 2>/dev/null
}

write_atomic "$STATE_FILE" "{\"state\":\"$STATE\",\"since\":$SINCE,\"ts\":$NOW,\"event_ms\":$EVENT_MS}"

trace "$STATE"

# Metadata is comparatively expensive (a git call, a walk up the process tree), so it is refreshed
# only on the events that can change it - plus whenever it is simply missing, which is how a
# session that started before this hook existed heals itself on its next tool call.
if [ "$EVENT" = "sessionStart" ] || [ "$EVENT" = "userPromptSubmitted" ] || [ ! -r "$META_FILE" ]; then
    SESSION_ID="$(json_str sessionId)"

    CWD="$(json_str cwd)"
    [ -n "$CWD" ] || CWD="${COPILOT_PROJECT_DIR:-$PWD}"

    # In a repo the top level is the project and never drifts. Outside one, cwd follows the session
    # into subdirectories, so a tile would rename itself to things like "src" mid-session - keep the
    # name first recorded instead of letting it wander.
    TOPLEVEL="$(git -C "$CWD" rev-parse --show-toplevel 2>/dev/null)"
    if [ -n "$TOPLEVEL" ]; then
        PROJECT="$(basename "$TOPLEVEL")"
    else
        PROJECT=""
        [ -r "$META_FILE" ] && PROJECT="$(read_field "$META_FILE" project)"
        [ -n "$PROJECT" ] || PROJECT="$(basename "$CWD")"
    fi
    BRANCH="$(git -C "$CWD" rev-parse --abbrev-ref HEAD 2>/dev/null)"
    [ "$BRANCH" = "HEAD" ] && BRANCH="$(git -C "$CWD" rev-parse --short HEAD 2>/dev/null)"

    # Copilot CLI has no session slug, so the prompt is the only thing that tells two sessions in
    # the same checkout apart. Kept short: it is a tile label, not a transcript.
    PROMPT="$(json_str prompt)"
    [ -n "$PROMPT" ] || PROMPT="$(json_str initialPrompt)"
    if [ -z "$PROMPT" ] && [ -r "$META_FILE" ]; then
        PROMPT="$(read_field "$META_FILE" prompt)"
    fi
    PROMPT="$(printf '%s' "$PROMPT" | cut -c1-60)"

    # Find the copilot process this hook descends from, so the plugin can drop tiles for sessions
    # whose terminal was closed without a clean sessionEnd.
    PID=""
    p=$$
    n=0
    while [ "$n" -lt 10 ]; do
        c="$(ps -o comm= -p "$p" 2>/dev/null | sed 's|.*/||')"
        [ "$c" = "copilot" ] && { PID="$p"; break; }
        p="$(ps -o ppid= -p "$p" 2>/dev/null | tr -d ' ')"
        { [ -z "$p" ] || [ "$p" -le 1 ]; } && break
        n=$((n + 1))
    done

    esc() { printf '%s' "$1" | sed 's/\\/\\\\/g; s/"/\\"/g'; }

    STARTED=""
    [ -r "$META_FILE" ] && STARTED="$(read_field "$META_FILE" started)"
    case "$STARTED" in '' | *[!0-9]*) STARTED="$NOW" ;; esac

    write_atomic "$META_FILE" "{\"schema\":2,\"terminal\":\"$TERMINAL\",\"session_ref\":\"$(esc "$SESSION_REF")\",\"warp_uuid\":\"$UUID\",\"session_id\":\"$(esc "$SESSION_ID")\",\"pid\":${PID:-0},\"cwd\":\"$(esc "$CWD")\",\"project\":\"$(esc "$PROJECT")\",\"branch\":\"$(esc "$BRANCH")\",\"prompt\":\"$(esc "$PROMPT")\",\"started\":$STARTED}"
fi

exit 0
