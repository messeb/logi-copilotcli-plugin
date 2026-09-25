# Publishing to the Logitech Marketplace

How a C# Logi Actions plugin gets from this repository to the Marketplace, and the things
that actually get a submission rejected. The plugin-specific checklist is in
[SUBMISSION.md](SUBMISSION.md).

## 1. Get the packaging tool

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

## 2. Get the manifest right

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

## 3. Pack Release, and check what went in

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

## 4. Do not touch the user's configuration at install time

`Plugin.Install()`, `Uninstall()` and `InstallAdmin()` exist and run during package installation.
Do not use them to write anything outside your plugin's own data directory. Installing is a click
on a listing, not consent to edit someone's config, and Logitech QA has rejected a plugin for
exactly this.

Put it behind a key the user placed themselves and pressed — that is real consent, and it comes
with an obvious gesture to reverse it. This plugin's Set up key needs two presses within five
seconds before it writes `~/.copilot/hooks/copilot-keypad.json`, and the same key removes it.

## 5. Supply the legal documents

- **Your own EULA.** The Marketplace Developer Agreement requires one naming you as licensor and
  stating that Logitech is not a party to it. **An MIT licence alone does not satisfy this** —
  MIT is the code grant, the EULA is a separate document. Point `licenseUrl` at it and publish it
  somewhere reachable.
- **A privacy statement** if you touch personal data. Audit rather than assume: grep your source
  for `HttpClient`, `System.Net`, sockets and any external URL, and list every path you read or
  write and every process you launch.

## 6. Check every link resolves

Review rejects dead links, and `homePageUrl`, `supportPageUrl` and `licenseUrl` are all public.

```sh
for u in <homePageUrl> <supportPageUrl> <licenseUrl>; do
  printf '%s %s\n' "$(curl -s -o /dev/null -w '%{http_code}' -L "$u")" "$u"
done
```

A `#fragment` never reaches the server, so a 200 proves nothing about an anchor — check anchors
against the committed file.

## 7. Test the package, not your dev build

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

## 8. Submit

Upload at [marketplace.logitech.com/contribute](https://marketplace.logitech.com/contribute) and
accept the Developer Agreement on your first submission. Review is manual plus automated, with no
guaranteed turnaround — roughly **10 working days** is typical. Every update goes through the same
review, so budget for it.

This plugin's own filled-in version of the above is [docs/SUBMISSION.md](SUBMISSION.md), and
the primary sources are the SDK's
[Distributing the Plugin](https://logitech.github.io/actions-sdk-docs/csharp/plugin-development/distributing-the-plugin/)
page and the
[Marketplace Approval Guidelines](https://logitech.github.io/actions-sdk-docs/marketplace-approval-guidelines/).

