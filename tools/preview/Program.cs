using System;
using System.IO;
using Loupedeck;
using Loupedeck.CopilotCLIPlugin.Rendering;
using Loupedeck.CopilotCLIPlugin.Sessions;

class Program
{
    static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    static string OutDir = Environment.GetEnvironmentVariable("OUT") ?? "/tmp/tiles";

    static SessionSnapshot S(SessionActivity a, string project, string branch, string prompt, double secs) =>
        new() { WarpUuid = new string('a', 32), Activity = a, Project = project, Branch = branch,
                Prompt = prompt, Since = Now.AddSeconds(-secs), Ts = Now };

    static void Save(BitmapImage img, string name) => img.SaveToFile(Path.Combine(OutDir, name + ".png"));

    static void Main()
    {
        Directory.CreateDirectory(OutDir);
        // Width116 is what the keypad actually asks for on every GetCommandImage call.
        var sz = PluginImageSize.Width116;

        Save(TileRenderer.Session(S(SessionActivity.Busy, "copilot-warp", "main", "add the all sessions key", 187), Now, sz, 3), "01_busy");
        Save(TileRenderer.Session(S(SessionActivity.Attention, "acme-api", "feat/payments", "run the migration", 42), Now, sz, 0), "02_attention");
        Save(TileRenderer.Session(S(SessionActivity.Done, "website", "main", "fix the nav", 9), Now, sz, 0), "03_done");
        Save(TileRenderer.Session(S(SessionActivity.Idle, "notes", "", "", 3600), Now, sz, 0), "04_idle");
        Save(TileRenderer.Session(S(SessionActivity.Busy, "a-very-long-project-name-here", "feature/some-long-branch", "x", 5), Now, sz, 5), "05_long");
        Save(TileRenderer.Session(S(SessionActivity.Done, "api", "", "why is the build failing on main again", 45296), Now, sz, 0), "06_prompt");

        Save(TileRenderer.ActiveButton(3, sz), "10_active");
        Save(TileRenderer.ActiveButton(0, sz), "11_active_zero");
        Save(TileRenderer.ActiveButton(12, sz), "12_active_12");
        Save(TileRenderer.WaitingButton(2, 1, sz, 0), "13_waiting_blocked");
        Save(TileRenderer.WaitingButton(2, 1, sz, 3), "14_waiting_blink");
        Save(TileRenderer.WaitingButton(2, 0, sz, 0), "15_waiting_done");
        Save(TileRenderer.WaitingButton(0, 0, sz, 0), "16_waiting_zero");
        Save(TileRenderer.AllButton(5, sz), "17_all");
        Save(TileRenderer.AllButton(0, sz), "18_all_zero");

        Save(TileRenderer.Setup(SetupAction.None, sz), "20_setup");
        Save(TileRenderer.Setup(SetupAction.Enable, sz), "21_setup_armed");
        Save(TileRenderer.Setup(SetupAction.Disable, sz), "22_setup_disarm");
        Save(TileRenderer.Nothing("Nothing waiting", sz), "23_empty");
        Console.WriteLine("ok");
    }
}
