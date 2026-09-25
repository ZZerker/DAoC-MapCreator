# MapCreator
Renders Dark Age of Camelot (DAoC) maps from the game client files: outdoor zones, capital cities and dungeons, with textured buildings, terrain relief and, in New Frontiers, the keeps and towers.

This is a fork of [Merec/DAoC-MapCreator](https://github.com/Merec/DAoC-MapCreator), which is no longer maintained. All credit for the original tool goes to Merec. This fork brings it up to date and renders the maps for the [TokaZerk UI](https://github.com/tokajer/TokaZerkUI) on the Eden freeshard. The UI can only show images, so everything a map needs has to be baked into it.

## What it renders
**Outdoor zones**
- Terrain textures with relief shading computed from the height map at full map size
- Rivers, lakes and lava in the configured colors, zone bounds
- Trees, colored from their leaf or bark texture, or from their model when the client has no tree texture for them
- Buildings and objects with their real textures, untextured parts in their material color, with shadows

**New Frontiers keeps and towers**
- All 21 keeps and 54 towers as they stand on Eden, fully upgraded and undamaged, in their realm's textures
- Built piece by piece from the client's keep models and texture tables (`frontiers.mpk`)

**Capital cities** (Camelot, Jordheim, Tir na Nog)
- Built from the city models, street and floor textures blended as in the game

**Dungeons**
- Built from the placed rooms, brightened so dark dungeons stay readable
- One extra map per level for the dungeons the client has level maps for (`zNNN_LL`, e.g. the six levels of Darkness Falls), the other levels shown faded underneath

City and dungeon maps use the same frame as the client and bestiary maps, so a position lines up at `(zone coordinate - offset) / width`.

## Changes in this fork
- Runs on .NET 10 (was .NET Framework 4.0) with Magick.NET 14 and SharpDX 4.2
- Own triangle rasterizer with textures, 2x supersampling and a depth buffer, so every pixel shows the top surface
- All client zones are available; the ones missing in the curated list come from the client's `zones.dat`
- Model and texture replacements the client loads (`NIFPROXY.csv`, `TEXPROXY.csv`) are applied
- Command line batch mode for unattended rendering, several zones in parallel in one process
- Render script that converts the result to DDS for the UI
- Finds the game folder on first start (Eden and Blackthorn launcher, default install paths)
- Much faster: zones render in parallel inside one process and share their model and texture caches, and models are drawn by the own rasterizer instead of one ImageMagick draw call per triangle (fixtures of zone 171: 54 s before, under 5 s now). All 14 New Frontiers zones at 2048 px take about 2 minutes.
- `fixtures.xml` only holds overrides (texture mode, size limits), everything else is read from the models
- Code cleanup: naming rules enforced by `.editorconfig`, no build warnings

## Data sources
Everything comes from the game client, except:
- `data\MapFrames.csv`: city and dungeon map frames from the Eden bestiary
- `data\Keeps.csv`: New Frontiers keep and tower positions from the Eden war map, piece layouts from [Dawn-of-Light db-public](https://github.com/Dawn-of-Light/db-public)

## Roadmap
- [x] Zone list from the client's `zones.dat`, including Eden's own zones
- [x] Textured buildings instead of plain white shapes
- [x] City maps
- [x] Dungeon maps, one map per level where the client has level maps
- [x] City and dungeon maps in the same frame as the client maps
- [x] Renderer quality: relief shading, blended city floors, brighter dungeons, depth buffer, tree colors
- [x] New Frontiers keeps and towers
- [ ] Names and points of interest on the maps (zone exits, bind stones, keep names)
- [ ] Maps for all dungeons, including the ones without a client map
- [ ] Export of city and dungeon maps to the UI
- [ ] Later: Old Frontiers keeps, re-render zones Eden has patched, replace the old .NET Framework libraries (Niflib, MPKLib)

## Requirements
- Windows x64
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)
- A DAoC client installation
- Python with Pillow for the DDS conversion

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

`tools\render_nf.ps1` renders all New Frontiers zones and converts them to DXT1 DDS files (`zNNN.dds`) with `tools\png_to_dds.py`:
```
.\tools\render_nf.ps1 -Size 2048 -Parallel 4 [-Zones 163,171] [-DdsSize 512]
```

### Please note
- Rendering is CPU and memory heavy, depending on the map size.
- Use map sizes that are a power of 2: 512, 1024, 2048, 4096. The terrain textures have a native resolution of 4096 pixels.
- The first render converts the models and fills the cache (`data\polys4.mpk`); later renders are faster.

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
