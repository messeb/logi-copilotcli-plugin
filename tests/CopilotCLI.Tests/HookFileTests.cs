namespace Loupedeck.CopilotCLIPlugin.Tests
{
    using System;
    using System.Linq;
    using System.Text.Json;

    using Loupedeck.CopilotCLIPlugin.Sessions;

    using Xunit;

    public class HookFileTests
    {
        private const String Script = "/Users/me/.copilot/keypad/keypad-hook.sh";

        private static JsonElement Parse(String path = Script) =>
            JsonDocument.Parse(HookFile.Build(path)).RootElement;

        [Fact]
        public void IsValidJson() => Assert.Equal(1, Parse().GetProperty("version").GetInt32());

        [Fact]
        public void RegistersEveryEventTheTilesNeed()
        {
            var hooks = Parse().GetProperty("hooks");

            foreach (var expected in HookFile.Events)
            {
                Assert.True(hooks.TryGetProperty(expected, out _), $"missing hook: {expected}");
            }
        }

        // The state machine is only complete with all eight. Dropping one is the kind of edit that
        // silently leaves tiles stuck in a state rather than failing loudly.
        [Fact]
        public void RegistersExactlyTheEightDocumentedEvents()
        {
            var hooks = Parse().GetProperty("hooks");

            Assert.Equal(8, hooks.EnumerateObject().Count());
        }

        [Fact]
        public void EachEntryPassesItsOwnEventNameToTheScript()
        {
            var hooks = Parse().GetProperty("hooks");

            foreach (var hook in hooks.EnumerateObject())
            {
                var command = hook.Value[0].GetProperty("command").GetString();

                Assert.Equal("command", hook.Value[0].GetProperty("type").GetString());
                Assert.EndsWith(" " + hook.Name, command);
                Assert.Contains(Script, command);
            }
        }

        // Copilot waits for hooks, so a hook that hangs hangs the session it is watching.
        [Fact]
        public void EveryEntryIsBounded()
        {
            var hooks = Parse().GetProperty("hooks");

            foreach (var hook in hooks.EnumerateObject())
            {
                var timeout = hook.Value[0].GetProperty("timeoutSec").GetInt32();

                Assert.InRange(timeout, 1, 30);
            }
        }

        // A path with a quote in it would otherwise write a broken hooks file into a user's Copilot
        // configuration - and a broken hooks file is not a failure anyone would connect to us.
        [Theory]
        [InlineData("/Users/me/my \"quoted\" dir/keypad-hook.sh")]
        [InlineData("/Users/me/back\\slash/keypad-hook.sh")]
        [InlineData("/Users/me/dollar$sign/keypad-hook.sh")]
        [InlineData("/Users/me/back`tick/keypad-hook.sh")]
        [InlineData("/Users/me/Ordner mit Leerzeichen/keypad-hook.sh")]
        public void SurvivesAwkwardPaths(String path)
        {
            var hooks = Parse(path).GetProperty("hooks");
            var command = hooks.GetProperty("sessionStart")[0].GetProperty("command").GetString();

            // Parses as JSON (the Parse call above would have thrown otherwise), and the shell sees
            // the path as one argument with every expansion character escaped.
            Assert.StartsWith("sh \"", command);
            foreach (var c in new[] { '"', '\\', '$', '`' })
            {
                var occurrences = path.Count(x => x == c);
                Assert.Equal(occurrences, CountEscaped(command, c));
            }
        }

        // Nothing is narrowed by a matcher. notification does need filtering to permission_prompt
        // and elicitation_dialog, but keypad-hook.sh does it on the payload, where it is testable:
        // a matcher whose semantics differ even slightly would silently drop every notification and
        // the alarm would just never fire.
        [Fact]
        public void UsesNoMatchers()
        {
            var hooks = Parse().GetProperty("hooks");

            foreach (var hook in hooks.EnumerateObject())
            {
                Assert.False(
                    hook.Value[0].TryGetProperty("matcher", out _),
                    $"{hook.Name} should not carry a matcher");
            }
        }

        [Fact]
        public void CarriesANoteSayingWhereItCameFrom() =>
            Assert.Contains("CopilotCLI", Parse().GetProperty("//").GetString());

        [Fact]
        public void RefusesAnEmptyScriptPath() =>
            Assert.Throws<ArgumentException>(() => HookFile.Build(""));

        private static Int32 CountEscaped(String command, Char c)
        {
            var count = 0;

            for (var i = 1; i < command.Length; i++)
            {
                if (command[i] == c && command[i - 1] == '\\')
                {
                    count++;
                }
            }

            return count;
        }
    }
}
