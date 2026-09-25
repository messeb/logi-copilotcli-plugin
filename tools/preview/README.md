# Key preview

Renders every key this plugin can draw to PNG, without a keypad, so the design can be looked at
and iterated on rather than guessed at.

It works because `PluginApi.dll` and its SkiaSharp backend ship with Logi Plugin Service, and
`BitmapImage.SaveToFile` is public. `Program.cs` here is the gallery; point a throwaway console
project at it with these compile items and references:

```xml
<Compile Include="../../src/Sessions/SessionSnapshot.cs" />
<Compile Include="../../src/Sessions/SessionClassifier.cs" />
<Compile Include="../../src/Sessions/SetupAction.cs" />
<Compile Include="../../src/Rendering/TextMetrics.cs" />
<Compile Include="../../src/Rendering/TileRenderer.cs" />

<Reference Include="PluginApi">
  <HintPath>/Applications/Utilities/LogiPluginService.app/Contents/MonoBundle/PluginApi.dll</HintPath>
</Reference>
<Reference Include="SkiaSharp">
  <HintPath>/Applications/Utilities/LogiPluginService.app/Contents/MonoBundle/.xamarin/osx-arm64/SkiaSharp.dll</HintPath>
</Reference>
```

plus `libSkiaSharp.dylib` and `libHarfBuzzSharp.dylib` copied to the output directory.

Two things this turned up that are worth knowing before changing `TileRenderer`:

- **`DrawText` does not clip to the rectangle it is given.** It centres the text and lets it
  overflow the key. Everything must be measured and truncated first — that is what `TextMetrics`
  is for.
- **`fontSize: -1` is a default size, not auto-fit.** Passing it to a big number renders it small.
