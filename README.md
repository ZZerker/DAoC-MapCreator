# MapCreator
Generate Dark Age of Camelot (DAoC) maps from the game client files.

This is a fork of [Merec/DAoC-MapCreator](https://github.com/Merec/DAoC-MapCreator), which is no longer maintained. All credit for the original tool goes to Merec. This fork brings it up to date and renders the maps for the [TokaZerk UI](https://github.com/tokajer/TokaZerkUI) on the Eden freeshard.

## Changes in this fork
- Runs on .NET 10 (was .NET Framework 4.0) with Magick.NET 14 and SharpDX 4.2
- Buildings and objects are drawn with their real textures (own triangle rasterizer), untextured parts with their material color
- Capital city maps (Camelot, Jordheim, Tir na Nog) built from the city models
- Dungeon maps built from the placed rooms, with one extra map per level for dungeons listed in the client's `areas.dat` (`zNNN_LL`, e.g. the six levels of Darkness Falls)
- City and dungeon maps use the same frame as the client and bestiary maps (`data\MapFrames.csv`), so a position lines up at `(zone coordinate - offset) / width`
- All client zones are available, the ones missing in the curated list come from the client's `zones.dat`
- Rivers and water render again, in the configured color
- Leafless trees (burnt trees, reeds) get their color from the bark texture
- Command line batch mode for unattended rendering, several zones in parallel in one process
- Render script that converts the result to DDS for the UI
- Finds the game folder on first start (Eden and Blackthorn launcher, default install paths)
- Much faster rendering: zones render in parallel inside one process and share their model and texture caches, models are drawn by an own rasterizer instead of one ImageMagick draw call per triangle (fixtures of zone 171: 54 s before, under 5 s now). All 14 New Frontiers zones at 2048 px take about 2 minutes.
- Smaller output files (8 bit PNG)
- `fixtures.xml` only holds overrides now (texture mode, size limits), everything else is read from the models
- Code cleanup: naming rules enforced by `.editorconfig`, no build warnings

## Roadmap
- Dungeons without level data: split into levels by floor height
- Brighter rendering of dark dungeon textures, correct overlap of ramps and bridges within a level
- Names, points of interest and mods on the maps
- Check which zones Eden has patched and need a new render
- Later: towers and keeps, replace the old .NET Framework libraries (Niflib, MPKLib)

## Requirements
- Windows x64
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)
- A DAoC client installation

## Build
```
dotnet build MapCreator.sln -c Release
```
The program is written to `Releases\MapCreator.exe`.

## Usage
Start `MapCreator.exe`, select the zones and click create. Settings (map size, water color, trees, bounds, output folder) are in the main window and the preferences.

Batch mode renders without user input, starts minimized and closes when done:
```
MapCreator.exe --render 163,171 [--size 2048] [--dir nf_2048] [--log render.log] [--parallel 4]
```

`tools\render_nf.ps1` renders all New Frontiers zones in parallel and converts them to DXT1 DDS files (`zNNN.dds`) with `tools\png_to_dds.py` (needs Python with Pillow):
```
.\tools\render_nf.ps1 -Size 2048 -Parallel 4 [-Zones 163,171] [-DdsSize 1024]
```

### Please note
- Rendering is CPU and memory heavy, depending on the map size.
- Use map sizes that are a power of 2: 512, 1024, 2048, 4096. The terrain textures have a native resolution of 4096 pixels.

## Original changelog (Merec)
**2017-10-01**
- Update to Magick.NET 7.0.7.300
- Update to SharpDX 4.0.1
- Replaced my Niflib.NET with https://github.com/dol-leodagan/niflib.net

**2015-08-23**
- Fixed crash when fixtures have wrong coordinates (thanks to HunabKu for reporting!)

**2014-02-16**
- bug fix in image replacement
- added image replacement examples for relic temples
- found some trees that are nor marked as trees (please report if you find more)
- changed all opacity to transparency fields to be more clear
- added option to set tree transparency per map
- reset settings, too many changes to convert them

**Older changes**
- added fixtures rendering
- added tree rendering
- full rewrite of the bound-calculation
- full rewrite of the water-calculation
- UI improvements
- fixed wrong calculations
- optimized default settings
- rewrote bounds generator, now there will be no more flood fills
- optimized river rendering
- add lava and a texture to water

Merec's thanks go to Schaf, Metty and Leodagan who helped a lot.

## License
GPL v2 or later, as the original.
