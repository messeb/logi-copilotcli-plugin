#!/bin/sh
# Tests for keypad-hook.sh. No framework: the thing under test is a POSIX shell script, so the
# test harness is one too, and it runs anywhere the hook does.
#
#   sh tests/hook/run-tests.sh
#
# Every test gets a fresh COPILOT_KEYPAD_ROOT under a temp dir, so nothing here can touch a real
# ~/.copilot.

set -u

HOOK="$(cd "$(dirname "$0")/../.." && pwd)/hooks/keypad-hook.sh"
[ -r "$HOOK" ] || { echo "cannot read $HOOK" >&2; exit 1; }

PASS=0
FAIL=0
UUID=abcdef0123456789abcdef0123456789

# Payloads as Copilot CLI actually sends them: one JSON object, NO trailing newline.
# Recorded from the probe on 2026-09-25.
payload() {  # payload <event> <timestamp-ms> [cwd]
    _cwd="${3:-/tmp/proj}"
    case "$1" in
    sessionStart)
        printf '{"sessionId":"s1","timestamp":%s,"cwd":"%s","source":"new","initialPrompt":"do a thing"}' "$2" "$_cwd" ;;
    userPromptSubmitted)
        printf '{"sessionId":"s1","timestamp":%s,"cwd":"%s","prompt":"do a thing"}' "$2" "$_cwd" ;;
    preToolUse)
        printf '{"sessionId":"s1","timestamp":%s,"cwd":"%s","toolName":"bash","toolArgs":{"command":"echo hi"}}' "$2" "$_cwd" ;;
    postToolUse)
        printf '{"sessionId":"s1","timestamp":%s,"cwd":"%s","toolName":"bash","toolResult":{"resultType":"success"}}' "$2" "$_cwd" ;;
    permissionRequest)
        printf '{"hookName":"permissionRequest","sessionId":"s1","timestamp":%s,"cwd":"%s","toolName":"bash","toolInput":{"command":"echo hi"}}' "$2" "$_cwd" ;;
    notification)
        printf '{"sessionId":"s1","timestamp":%s,"cwd":"%s","message":"the agent needs you","notification_type":"%s"}' "$2" "$_cwd" "${NOTIFY_TYPE:-permission_prompt}" ;;
    agentStop)
        printf '{"sessionId":"s1","timestamp":%s,"cwd":"%s","transcriptPath":"/tmp/e.jsonl","stopReason":"end_turn","stop_hook_active":false}' "$2" "$_cwd" ;;
    sessionEnd)
        printf '{"sessionId":"s1","timestamp":%s,"cwd":"%s","reason":"complete"}' "$2" "$_cwd" ;;
    esac
}

fire() {  # fire <event> <timestamp-ms> [uuid-override]   (NOTIFY_TYPE steers notification payloads)
    payload "$1" "$2" | env \
        COPILOT_KEYPAD_ROOT="$ROOT" \
        WARP_TERMINAL_SESSION_UUID="${3-$UUID}" \
        sh "$HOOK" "$1" >/dev/null 2>&1
}

# The hook derives the filename from whichever terminal identified the session, so the iTerm tests
# fire with ITERM_SESSION_ID instead and expect the normalised name.
# -u WARP_TERMINAL_SESSION_UUID matters: these tests may themselves be run from a Warp pane, and
# the hook rightly prefers Warp when both variables are present.
fire_iterm() {  # fire_iterm <event> <timestamp-ms> <ITERM_SESSION_ID value>
    payload "$1" "$2" | env -u WARP_TERMINAL_SESSION_UUID \
        COPILOT_KEYPAD_ROOT="$ROOT" \
        ITERM_SESSION_ID="$3" \
        sh "$HOOK" "$1" >/dev/null 2>&1
}

state_file() { echo "$ROOT/sessions/$UUID.state.json"; }
meta_file()  { echo "$ROOT/sessions/$UUID.meta.json"; }

field() {  # field <file> <key>  -- reads a flat JSON string or number value
    sed -n "s/.*\"$2\":\"\{0,1\}\([^,\"}]*\)\"\{0,1\}.*/\1/p" "$1" 2>/dev/null
}

setup() {
    ROOT="$(mktemp -d)"
    export ROOT
}

teardown() { [ -n "${ROOT:-}" ] && rm -rf "$ROOT"; }

check() {  # check <description> <expected> <actual>
    if [ "$2" = "$3" ]; then
        PASS=$((PASS + 1))
        printf '  ok   %s\n' "$1"
    else
        FAIL=$((FAIL + 1))
        printf '  FAIL %s\n       expected: %s\n       actual:   %s\n' "$1" "$2" "$3"
    fi
}

# --------------------------------------------------------------------------------------------

echo "state mapping"
setup
fire sessionStart 1000000000000
check "sessionStart -> idle" "idle" "$(field "$(state_file)" state)"
fire userPromptSubmitted 1000000001000
check "userPromptSubmitted -> busy" "busy" "$(field "$(state_file)" state)"
fire preToolUse 1000000002000
check "preToolUse -> busy" "busy" "$(field "$(state_file)" state)"
# permissionRequest fires before the permission service runs - before auto-allow - so it says a
# tool is being evaluated, not that anyone is blocked. The gap to postToolUse is the TOOL's
# duration: measured at 78 ms for `echo`, and a full 4 s for `sleep 4`.
fire permissionRequest 1000000003000
check "permissionRequest -> busy, not attention" "busy" "$(field "$(state_file)" state)"
fire postToolUse 1000000004000
check "postToolUse -> busy" "busy" "$(field "$(state_file)" state)"
fire agentStop 1000000005000
check "agentStop -> done" "done" "$(field "$(state_file)" state)"
teardown

echo "session lifecycle"
setup
fire sessionStart 1000000000000
check "state file created" "yes" "$([ -r "$(state_file)" ] && echo yes || echo no)"
check "meta file created" "yes" "$([ -r "$(meta_file)" ] && echo yes || echo no)"
fire sessionEnd 1000000009000
check "sessionEnd removes state" "no" "$([ -r "$(state_file)" ] && echo yes || echo no)"
check "sessionEnd removes meta" "no" "$([ -r "$(meta_file)" ] && echo yes || echo no)"
teardown

echo "out-of-order events (Copilot sends userPromptSubmitted BEFORE sessionStart)"
setup
fire userPromptSubmitted 1000000001000
fire sessionStart 1000000000500          # older payload timestamp, arrives later
check "stale sessionStart does not clobber busy" "busy" "$(field "$(state_file)" state)"
teardown

echo "elapsed clock"
setup
fire userPromptSubmitted 1000000000000
SINCE1="$(field "$(state_file)" since)"
sleep 1
fire preToolUse 1000000001000
SINCE2="$(field "$(state_file)" since)"
check "busy->busy preserves the clock" "$SINCE1" "$SINCE2"
fire agentStop 1000000002000
SINCE3="$(field "$(state_file)" since)"
check "state change restarts the clock" "changed" "$([ "$SINCE1" != "$SINCE3" ] && echo changed || echo same)"
teardown

echo "notification is filtered by type, in the script"
setup
fire agentStop 1000000000000            # tile is green: turn is over
fire notification 1000000001000
check "notification on a finished turn is ignored" "done" "$(field "$(state_file)" state)"
teardown
setup
fire preToolUse 1000000000000           # something is in flight
fire notification 1000000001000
check "notification mid-turn escalates to attention" "attention" "$(field "$(state_file)" state)"
teardown
# A permission prompt is answered and the turn carries on: the tile has to go back to working, or
# it would claim you are still blocked for the rest of the session.
setup
fire preToolUse 1000000000000
fire notification 1000000001000
fire postToolUse 1000000002000
check "answering the prompt returns the tile to busy" "busy" "$(field "$(state_file)" state)"
teardown

# The filter lives in the script rather than in a hook-file matcher, so it is testable. Only the
# two types that mean "the agent is asking you something" may raise the alarm.
for t in permission_prompt elicitation_dialog; do
    setup
    fire preToolUse 1000000000000
    NOTIFY_TYPE=$t fire notification 1000000001000
    check "notification_type=$t raises attention" "attention" "$(field "$(state_file)" state)"
    teardown
done

# A finished background shell or subagent must not make a working session look blocked.
for t in shell_completed shell_detached_completed agent_completed agent_idle wat; do
    setup
    fire preToolUse 1000000000000
    NOTIFY_TYPE=$t fire notification 1000000001000
    check "notification_type=$t leaves the tile working" "busy" "$(field "$(state_file)" state)"
    teardown
done

# A payload with no type at all must not be guessed into an alarm.
setup
fire preToolUse 1000000000000
printf '{"sessionId":"s1","timestamp":1000000001000,"cwd":"/tmp/proj","message":"hi"}' \
    | env COPILOT_KEYPAD_ROOT="$ROOT" WARP_TERMINAL_SESSION_UUID="$UUID" sh "$HOOK" notification >/dev/null 2>&1
check "notification with no type leaves the tile working" "busy" "$(field "$(state_file)" state)"
teardown

echo "debug trace"
setup
fire sessionStart 1000000000000
check "off by default: no log file" "no" "$([ -e "$ROOT/debug.log" ] && echo yes || echo no)"
teardown
setup
payload preToolUse 1000000000000 | env COPILOT_KEYPAD_ROOT="$ROOT" \
    WARP_TERMINAL_SESSION_UUID="$UUID" COPILOT_KEYPAD_DEBUG=1 sh "$HOOK" preToolUse >/dev/null 2>&1
check "COPILOT_KEYPAD_DEBUG=1 writes a trace line" "1" "$(wc -l < "$ROOT/debug.log" 2>/dev/null | tr -d ' ')"
check "trace names the event" "yes" "$(grep -q 'event=preToolUse' "$ROOT/debug.log" && echo yes || echo no)"
check "trace records the transition" "yes" "$(grep -q 'none -> busy' "$ROOT/debug.log" && echo yes || echo no)"
teardown
# The type is the whole point of tracing a notification: it says which of the six kinds arrived.
setup
payload preToolUse 1000000000000 | env COPILOT_KEYPAD_ROOT="$ROOT" \
    WARP_TERMINAL_SESSION_UUID="$UUID" COPILOT_KEYPAD_DEBUG=1 sh "$HOOK" preToolUse >/dev/null 2>&1
NOTIFY_TYPE=shell_completed payload notification 1000000001000 | env COPILOT_KEYPAD_ROOT="$ROOT" \
    WARP_TERMINAL_SESSION_UUID="$UUID" COPILOT_KEYPAD_DEBUG=1 sh "$HOOK" notification >/dev/null 2>&1
check "trace records an ignored notification's type" "yes" "$(grep -q 'type=shell_completed' "$ROOT/debug.log" && echo yes || echo no)"
teardown

echo "iTerm2 sessions"
# Recorded live: ITERM_SESSION_ID is "w0t0p0:GUID". The prefix is the pane's POSITION and changes
# when a pane moves between tabs, so only the GUID may identify the session.
ITERM_GUID=35EF942E-C843-46A5-A2D1-A567234BBE47
ITERM_KEY=35ef942ec84346a5a2d1a567234bbe47
setup
fire_iterm sessionStart 1000000000000 "w0t0p0:$ITERM_GUID"
check "records the session under its normalised id" "yes" \
    "$([ -r "$ROOT/sessions/$ITERM_KEY.state.json" ] && echo yes || echo no)"
check "records the terminal" "iterm" "$(field "$ROOT/sessions/$ITERM_KEY.meta.json" terminal)"
check "keeps iTerm's own spelling of the id" "$ITERM_GUID" \
    "$(field "$ROOT/sessions/$ITERM_KEY.meta.json" session_ref)"
teardown

# The same pane after being dragged to another tab: different prefix, same session, same file.
setup
fire_iterm sessionStart 1000000000000 "w0t0p0:$ITERM_GUID"
fire_iterm agentStop   1000000001000 "w0t1p0:$ITERM_GUID"
check "a moved pane stays one session" "1" \
    "$(find "$ROOT/sessions" -name '*.state.json' 2>/dev/null | wc -l | tr -d ' ')"
check "and keeps its state" "done" "$(field "$ROOT/sessions/$ITERM_KEY.state.json" state)"
teardown

# Warp wins when both are set: a Warp pane running an iTerm-aware shell is still a Warp pane.
setup
payload sessionStart 1000000000000 | env COPILOT_KEYPAD_ROOT="$ROOT" \
    WARP_TERMINAL_SESSION_UUID="$UUID" ITERM_SESSION_ID="w0t0p0:$ITERM_GUID" \
    sh "$HOOK" sessionStart >/dev/null 2>&1
check "Warp takes precedence" "warp" "$(field "$(meta_file)" terminal)"
teardown

for bad in "w0t0p0:" "w0t0p0:not-a-guid" "w0t0p0:../../etc/passwd" "no-colon-at-all"; do
    setup
    fire_iterm sessionStart 1000000000000 "$bad"
    check "rejects ITERM_SESSION_ID '$bad'" "0" "$(find "$ROOT/sessions" -type f 2>/dev/null | wc -l | tr -d ' ')"
    teardown
done

echo "identity guards"
setup
fire sessionStart 1000000000000 ""
check "no Warp UUID: writes nothing" "0" "$(find "$ROOT/sessions" -type f 2>/dev/null | wc -l | tr -d ' ')"
teardown
setup
fire sessionStart 1000000000000 "../../etc/passwd"
check "path traversal in UUID rejected" "0" "$(find "$ROOT/sessions" -type f 2>/dev/null | wc -l | tr -d ' ')"
teardown
# Case is normalised rather than rejected: iTerm spells its GUIDs in uppercase, and one rule has
# to serve both terminals. What the guard actually protects is the FILENAME, which is derived from
# the identifier and must still be exactly 32 hex characters.
setup
fire sessionStart 1000000000000 "ABCDEF0123456789ABCDEF0123456789"
check "uppercase is folded, not rejected" "yes" \
    "$([ -r "$ROOT/sessions/abcdef0123456789abcdef0123456789.state.json" ] && echo yes || echo no)"
teardown
setup
fire sessionStart 1000000000000 "abc"
check "short UUID rejected" "0" "$(find "$ROOT/sessions" -type f 2>/dev/null | wc -l | tr -d ' ')"
teardown

echo "metadata"
setup
fire sessionStart 1000000000000 "$UUID"
check "records the terminal" "warp" "$(field "$(meta_file)" terminal)"
check "records the session reference" "$UUID" "$(field "$(meta_file)" session_ref)"
check "records the Copilot session id" "s1" "$(field "$(meta_file)" session_id)"
check "records the project from cwd" "proj" "$(field "$(meta_file)" project)"
teardown

echo "resilience"
setup
printf 'not json at all' | env COPILOT_KEYPAD_ROOT="$ROOT" WARP_TERMINAL_SESSION_UUID="$UUID" \
    sh "$HOOK" sessionStart >/dev/null 2>&1
check "garbage stdin still exits 0" "0" "$?"
check "garbage stdin still records a state" "idle" "$(field "$(state_file)" state)"
teardown
setup
fire sessionEnd 1000000000000
check "sessionEnd on an unknown session exits 0" "0" "$?"
teardown

echo "permissions"
setup
fire sessionStart 1000000000000
check "state dir is private" "700" "$(stat -f '%Lp' "$ROOT" 2>/dev/null || stat -c '%a' "$ROOT" 2>/dev/null)"
teardown

# --------------------------------------------------------------------------------------------

echo
echo "passed: $PASS   failed: $FAIL"
[ "$FAIL" -eq 0 ]
