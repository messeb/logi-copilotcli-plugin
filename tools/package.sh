#!/bin/sh
# Builds bin/CopilotCLI_<version>.lplug4 from a Release build.
#
# A .lplug4 is a zip with a fixed structure - bin/ beside metadata/, with LoupedeckPackage.yaml in
# metadata/ - so this builds it directly rather than depending on logiplugintool being installed.
# When the tool IS available it is used to verify the result, which is the part worth having.
set -eu

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

: "${DOTNET_ROOT:=/opt/homebrew/opt/dotnet/libexec}"
export DOTNET_ROOT

STAGE="$ROOT/bin/Release"
META="$STAGE/metadata/LoupedeckPackage.yaml"

echo "==> building Release"
dotnet build src/CopilotCLIPlugin.csproj -c Release --nologo

[ -f "$META" ] || { echo "missing $META" >&2; exit 1; }

VERSION="$(sed -n 's/^version:[[:space:]]*//p' "$META" | tr -d '\r' | tr '.' '_')"
[ -n "$VERSION" ] || { echo "no version in $META" >&2; exit 1; }

OUT="$ROOT/bin/CopilotCLI_${VERSION}.lplug4"
rm -f "$OUT"

# Shipping the host's own assemblies is the most common Marketplace rejection, and it makes the
# service refuse the plugin with a bare "Cannot load plugin from '<path>.dll'". PluginApi brings a
# dependency closure with it, so the whole family is checked, not just the one file.
#
# Checked here rather than trusted, because the failure appears at install time on someone else's
# machine. .pdb is weight and leaks local source paths; .DS_Store is junk a reviewer will see, and
# a stale one in bin/ survives rebuilds, so the staged tree is what gets checked.
FORBIDDEN="$(find "$STAGE" \( \
    -name 'PluginApi.dll' -o -name 'Newtonsoft*.dll' -o -name 'YamlDotNet*.dll' \
    -o -name 'Svg*.dll' -o -name 'ExCSS*.dll' -o -name 'SkiaSharp*.dll' \
    -o -name '*.pdb' -o -name '.DS_Store' \) 2>/dev/null)"

if [ -n "$FORBIDDEN" ]; then
    echo "refusing to package — these must not ship:" >&2
    echo "$FORBIDDEN" | sed 's/^/  /' >&2
    exit 1
fi

[ -f "$STAGE/metadata/Icon256x256.png" ] || { echo "missing metadata/Icon256x256.png" >&2; exit 1; }

echo "==> packing $(basename "$OUT")"
# -X drops the resource forks and .DS_Store extras macOS would otherwise smuggle in.
( cd "$STAGE" && zip -q -r -X "$OUT" bin metadata )

if command -v logiplugintool >/dev/null 2>&1; then
    echo "==> verifying"
    # The tool targets net8.0; on a net10-only machine it needs roll-forward to run at all.
    DOTNET_ROLL_FORWARD=Major logiplugintool verify "$OUT"
else
    echo "==> logiplugintool not installed; skipping verify"
    echo "    contents:"
    unzip -l "$OUT" | sed 's/^/    /'
fi

echo
echo "built $OUT"
