# MapCreator
Renders Dark Age of Camelot (DAoC) maps from the game client files: outdoor zones, capital cities and dungeons, with textured buildings, terrain relief and, in New Frontiers, the keeps and towers.

This is a fork of [Merec/DAoC-MapCreator](https://github.com/Merec/DAoC-MapCreator), which is no longer maintained. All credit for the original tool goes to Merec. This fork brings it up to date and renders the maps for the [TokaZerk UI](https://github.com/tokajer/TokaZerkUI) on the Eden freeshard. The UI can only show images, so everything a map needs has to be baked into it.

## Download
Get the latest build from [Releases](https://github.com/ZZerker/DAoC-MapCreator/releases). Unzip it anywhere and start `MapCreatorNext.exe`. It needs the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (x64) and a DAoC client; the game folder is found on first start.

## What it renders
**Outdoor zones**
- Terrain textures with relief shading computed from the height map at full map size
- Rivers, lakes and lava; shallow water lets the ground show through, so shores fade into the water
- Trees drawn from their models with their own textures, leaves cut out by the texture's alpha
- Buildings and objects with their real textures, their baked lighting (dark maps) and a soft drop shadow; parts below the ground stay hidden like in the game
- Buildings seen from the south in 3D: walls drawn upward, the footprint stays in place (option, on by default); models that bring their own ground stay flat
- Contact shadows (ambient occlusion) around and between buildings and keeps (option, on by default)
- Trees in 3D as an option (off by default)
- Zone bounds; the area outside is darkened, 3D buildings reaching into it stay clear

**New Frontiers keeps and towers**
- All 21 keeps and 54 towers as they stand on Eden, fully upgraded and undamaged, in their realm's textures
- Built piece by piece from the client's keep models and texture tables (`frontiers.mpk`)
- Drawn in 3D seen from the south, like the launcher's keep images, with relic temples, relic keeps, mile gates and bridges
- Optional, e.g. for maps that draw their own keep icons

**Capital cities** (Camelot, Jordheim, Tir na Nog)
- Built from the city models; ground layers (grass, soil, cobble, rock) blended per vertex as in the game

**Dungeons**
- Built from the placed rooms with their baked lighting, brightened so dark dungeons stay readable
- One extra map per level for the dungeons the client has level maps for (`zNNN_LL`, e.g. the six levels of Darkness Falls), the other levels shown faded underneath
- Instanced zones (`skycity` data, e.g. Underground Forest, Dream) included

City and dungeon maps use the same frame as the client and bestiary maps, so a position lines up at `(zone coordinate - offset) / width`. Each of them gets a `zNNN.frame.json` with its offset and width.

**Labels**
- Keep and tower names, bosses, places and docks, towns, dungeon entrances, ToA artifact encounters, trainers and services in the capitals, neighbor zone names along the map border
- Written as `zNNN.labels.json` next to each map and drawn at the final size in the TokaZerk UI font, so the text stays sharp

**UI map set** (TokaZerk UI)
- Every zone, dungeon level and region map from one full render, at 512 and 256 px (other sizes on request)
- Region maps stitched from the zone renders, laid out by the client's `zones.dat` and `regions.dat`
- War maps for New Frontiers and the old frontiers
- The UI's `areas.dat` and `regions.dat` written with the same frames, so the position marker lines up

## Changes in this fork
- Runs on .NET 10 (was .NET Framework 4.0) with Magick.NET 14 and `System.Numerics`
- Niflib built from source: [ZZerker/niflib.net](https://github.com/ZZerker/niflib.net), a .NET 10 fork without SharpDX, included as a git submodule
- Own triangle rasterizer with textures, trilinear filtering, 2x supersampling and a depth buffer, so every pixel shows the top surface
- Models use the texture layers and vertex lighting they name, not just their fallback texture
- All client zones are available; the ones missing in the curated list come from the client's `zones.dat`
- Model and texture replacements the client loads (`NIFPROXY.csv`, `TEXPROXY.csv`) are applied
- Command line batch mode for unattended rendering, several zones in parallel in one process, with a live console view; every option is also in the window
- Every model is drawn from its geometry; the hand-made images for relic temples, boats and piers are gone
- Scripts to convert the result to DDS for the UI and to render everything overnight
- Finds the game folder on first start (Eden and Blackthorn launcher, default install paths)
- Much faster: zones share their model and texture caches, and models are drawn by the own rasterizer instead of one ImageMagick draw call per triangle (fixtures of zone 171: 54 s before, under 5 s now). All 14 New Frontiers zones at 2048 px take about 2 minutes.
- `fixtures.xml` only holds overrides (texture mode, size limits, shadows), everything else is read from the models
- Code cleanup: naming rules enforced by `.editorconfig`, no build warnings

## Data sources
Everything comes from the game client, except:
- `data\MapFrames.csv`: city and dungeon map frames from the Eden bestiary
- `data\Keeps.csv`: New Frontiers keep and tower positions from the Eden war map, piece layouts from [Dawn-of-Light db-public](https://github.com/Dawn-of-Light/db-public)
- `data\Landmarks.csv`: places, docks and bosses for the labels, bosses from the Eden bestiary
- `data\WarMaps.csv`: which zones the UI's war map textures show and where
- `data\ZoomAreas.csv`: room maps cut from a zone map where the client has no level data

## Roadmap
- [x] Zone list from the client's `zones.dat`, including Eden's own zones
- [x] Textured buildings instead of plain white shapes
- [x] City maps
- [x] Dungeon maps, one map per level where the client has level maps
- [x] City and dungeon maps in the same frame as the client maps
- [x] Renderer quality: relief shading, blended ground layers, vertex lighting, depth shaded water, shadows
- [x] New Frontiers keeps and towers
- [x] Names and points of interest on the New Frontiers maps
- [x] Maps for all playable dungeons, also the 16 that have no map in the game
- [x] Niflib on .NET 10, built from source
- [x] New window (Avalonia): zone browser with group ticks, presets, live render progress, map viewer
- [x] Own reader for the game archives (MPK)
- [x] Buildings, keeps and trees in 3D, contact shadows
- [x] Complete UI map set from our renders: zone, level, region and war maps
- [ ] Later: Old Frontiers keeps, re-render zones Eden has patched

## Requirements
- Windows x64
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)
- A DAoC client installation
- For the DDS conversion: Python with Pillow, optionally `texconv.exe` from [DirectXTex](https://github.com/microsoft/DirectXTex/releases) in `tools\bin` for better DXT1 quality

## Build
Niflib is a git submodule, so clone with `--recursive` (or run `git submodule update --init` in an existing clone):
```
git clone --recursive https://github.com/ZZerker/DAoC-MapCreator.git
dotnet build MapCreator.sln -c Release
```
The program is written to `Releases\MapCreatorNext.exe`. Pushing a tag `v*` builds and publishes a GitHub release.

## Usage
Start `MapCreatorNext.exe`. Tick zones in the list on the left (search by name or id, or tick whole groups such as a realm, an expansion or a zone type), set the options on the right or pick a preset, and click Render. "Showing maps from" in the top bar picks the render folder the window looks at (every folder in the output folder that holds zone maps, with its date and zone count); a render switches it to the folder it writes into. Each zone in the list shows its state in that folder in color: rendered with the date, queued, rendering, failed, not rendered, or archive newer (the game files changed after the render). The Map tab shows the selected zone's map with its labels, zoomable, and the file it comes from; the Activity tab shows every running zone, the finished ones and the log. Each render folder gets its own `render.log`. Game folder, output folder, cache cleanup and licenses are under Settings. Settings are saved in `%LOCALAPPDATA%\MapCreator\settings.json`, presets in `presets.json` next to it.

Batch mode renders without user input and without the main window:
```
MapCreatorNext.exe --render <zones> [--size 2048] [--dir nf_2048] [--log render.log] [--parallel 12] [--no-keeps] [--no-depth-water] [--labels-only] [--no-console]
```
A console window shows the run live: zones done, failed and left, the estimated time left, memory, one row per zone being rendered with its current step and progress, and the latest log lines. It stays open 30 seconds after the batch (or until a key is pressed), so unattended runs still end. `--no-console` runs without it. Closing the console window stops the render.

The log file is written as the render goes, one line at a time, so it keeps everything up to a crash. Every line carries its zone id. A zone that logged errors (for example a model that could not be drawn) ends with "Finished with N errors" and is counted in the summary. Other options come from the settings saved in the window; the exit code is 1 if a zone failed.

### Zone groups
`<zones>` is a comma separated list of zone ids and groups. The groups come from the same data as the zone selection in the window.

| Group | Examples |
|---|---|
| Everything | `all` |
| Realm | `alb`, `mid`, `hib` (or `albion`, `midgard`, `hibernia`) |
| Expansion | `nf`, `of`, `si`, `toa`, `cata`, `dr`, `lotm`, `classic`, `foundations`, `tutorial` (or the full name, e.g. `new-frontiers`) |
| Zone type | `outdoor`, `city`, `dungeon`, `instance`, `bg`, `indoor` |
| Client zones not in the curated list | `client` |
| Preset | the name of a preset saved in the zone selection |

`+` intersects groups, a comma adds them up. Case, spaces and dashes don't matter.

| Example | Renders |
|---|---|
| `--render nf+outdoor` | the 14 New Frontiers zones, without the battlegrounds |
| `--render alb+dungeon` | all Albion dungeons |
| `--render city` | Camelot, Jordheim, Tir na Nog |
| `--render 163,nf+bg` | zone 163 and the New Frontiers battlegrounds |
| `--render all` | every zone |

Unknown names are logged as a warning and skipped.

`tools\render_nf.ps1` renders all New Frontiers zones and converts them to DXT1 DDS files (`zNNN.dds`) with the labels drawn in:
```
.\tools\render_nf.ps1 -Size 2048 -Parallel 4 [-Zones 163,171] [-DdsSize 512]
```

`tools\render_all_shutdown.ps1` builds, renders every zone (or `-Zones`), converts New Frontiers to DDS and shuts the computer down (`-NoShutdown` to keep it running).

`--ui-maps` builds the complete TokaZerk UI map set from a full render, without rendering again (about 2 minutes):
```
MapCreatorNext.exe --ui-maps <render folder> --dir <set> [--game <client folder>] [--ui-sizes 512,256]
```
It writes the labeled zone, level and region maps, `areas.dat`, `regions.dat` and the war map textures into `<set>\512` and `<set>\256`. `tools\deploy_ui_maps.ps1 -Source <set> [-Repo <UI repo>]` converts them to DXT1 DDS and puts them into `Maps`, `Maps_large` (512), `Maps_small` (256) and `warmap`; old maps the set does not contain are removed. `-Folder <name> -Size <px>` writes a single test folder instead.

`tools\deploy_launcher_maps.ps1 -Source <render folder>` (run as administrator) copies the maps into the Eden launcher as `zoneNNN.jpg` and writes the city and dungeon frames into its `zones.json`, so the position marker lines up. The launcher's own files are saved once to `Output\launcher_backup_all`; `-Restore` puts them back.

### Please note
- Rendering is CPU and memory heavy, depending on the map size and the number of parallel zones.
- Use map sizes that are a power of 2: 512, 1024, 2048, 4096. The terrain textures have a native resolution of 4096 pixels.
- The first render converts the models and fills the cache (`data\polys8`, one file per model); later renders are faster. "Clear model cache" under Settings deletes it.

## Changelog
**3.0.0** (2026-10-02)
- New window built with Avalonia (dark): zone list with search and group ticks, render options with presets, render bar with cancel and an activity panel (running zones, filtered log), map viewer with zoom and label overlay, settings flyout. The old WinForms windows are gone; the program is now `MapCreatorNext.exe` and keeps its settings in `%LOCALAPPDATA%\MapCreator\settings.json` (imported once from the old settings)
- Own reader for the game archives instead of MPKLib
- Grass in cities and on the New Frontiers relic grounds no longer shows the tile pattern
- 3D view from the south for New Frontiers keeps, relic temples, mile gates and bridges, and for all outdoor buildings; 3D trees as an option. Keeps, buildings and contact shadows are on by default
- Contact shadows (ambient occlusion) for keeps, outdoor buildings, cities and dungeons
- `--ui-maps` builds the whole TokaZerk UI map set (zone, level, region and war maps, `areas.dat`, `regions.dat`) from one render; `tools\deploy_ui_maps.ps1` puts it into the UI
- Region maps stitched from the zone renders, without the old frontier zones on the realm maps
- The darkened area outside the zone bounds no longer covers 3D buildings (Aegirhamn)
- 3D buildings: cities and dungeons stay flat, models under water stay flat, single sheet walls are seen from both sides
- Window: render folder picker, colored zone state (queued, rendering, rendered, failed), map and activity as tabs; the zone list and the map always show the same folder, also during a render started from the command line with `--ui`
- Model parts below the water surface are hidden and trees standing under water are left out (dead oaks in Lough Gur); leafless dead trees keep their branches instead of turning into dark blobs (Folley Lake, Cursed Forest)
- 12 zones in parallel by default
- Releases run the tests before they are published

**2.2.0** (2026-09-29)
- Map labels drawn by MapCreator in the TokaZerk UI font (`--labels <dir> [--label-size N]`, `--labels-only` relabels existing renders): keeps, towers (GT, WT, OP, SP), neighbor zones, towns, channelers, dungeon entrances, bosses, ToA artifact encounters, and trainers and services in the capitals. Cities and dungeons are labeled in their frame. The point data files are not part of the release yet
- Battleground keeps and towers
- Walls built as bare vertical sheets (Avalon Isle's city wall) show their top edge
- Billboard trees that lose their leaves when seen from above are filled with their color
- Much faster model cache: one file per model, loaded outside the global lock (a full run went from 3.5 hours to 23 minutes)
- Trees are drawn with their textures instead of one average color
- Live console view for batch runs; the log is written directly and every line carries its zone id
- Errors are no longer swallowed: a model that cannot be drawn is logged with the reason, and the zone ends "Finished with N errors"
- Baked lighting for Darkness Falls, Veil Island and other dungeons that keep it in the detail slot
- Glows (additive meshes) add their light instead of being left out
- Dungeon brightness from the median, with a soft highlight curve instead of clipping (evenly lit dungeons are no longer washed out)
- Relic temples, boats, logs, piers and rock barriers are drawn from their models instead of old hand-made images
- City and dungeon maps write a frame file; new script to put the maps into the Eden launcher
- Tree clusters and trees only `fixtures.xml` knows get their tree color; a warning is logged when a model falls back to its category color
- Passage of Conflict entrances labeled on the Irish Sea maps
- Less memory: models and textures only one zone uses are released after it, image leaks closed. The model cache is saved safely, so an interrupted save no longer breaks it
- Fixes: seams between tiled ground models, the Agramon arena mountaintop, water proxies nested below their node (Galladoria), models missing in the client are skipped, battleground portal keep positions, overlapping lakes no longer fade each other (bog of zone 269), no bright circles around Mag Mell (Eden's Lough Derg copies), models with their own ground no longer show it as a square (wreck in zone 077), dungeon water only over its basin (zone 332), room maps win over the dark slot gradient, a `fixtures.xml` entry without category no longer stops every render, the bounds color field works
- The model cache moves to the `data\polys8` folder; an existing `polys8.mpk` is split into it once

**2.1.0** (2026-09-26)
- Baked lighting (dark maps) on buildings and city floors
- Zone groups in batch mode (`all`, realms, expansions, zone types), so every playable dungeon renders in one run
- Niflib built from source as a .NET 10 fork, SharpDX removed
- Zoomed area maps (`zNNN_AA.dds`) and PNG previews in the DDS script
- Fixes: no guessed fill for bound pieces inside the map, Dun Crimthain heading, old NIF 3.03 models fall back to their newer copy
- Trees are always drawn from their models; the prerendered tree images are gone

**2.0.0** (2026-09-26): first release of the fork, everything listed above.

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
