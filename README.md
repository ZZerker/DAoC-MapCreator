# MapCreator
Renders Dark Age of Camelot (DAoC) maps straight from the game client files: outdoor zones, capital cities and dungeons, with textured buildings, terrain relief, baked lighting and, in New Frontiers, every keep and tower.

<p align="center">
  <a href="https://zzerker.github.io/daoc-ui-maps/"><img src="https://zzerker.github.io/daoc-ui-maps/data/eden/viewer/z026.webp" alt="City of Camelot rendered by MapCreator" width="720"></a>
  <br>
  <sub>City of Camelot (zone 026), rendered from the client's city models at 2048 px</sub>
</p>

<p align="center">
  <a href="https://zzerker.github.io/daoc-ui-maps/"><b>Browse all maps</b></a> ·
  <a href="https://github.com/ZZerker/DAoC-MapCreator/releases"><b>Download MapCreator</b></a> ·
  <a href="https://github.com/ZZerker/daoc-ui-maps"><b>UI map packs</b></a>
</p>

This is a fork of [Merec/DAoC-MapCreator](https://github.com/Merec/DAoC-MapCreator), which is no longer maintained. All credit for the original tool goes to Merec. The fork brings it up to date. Its maps work with any DAoC UI that uses the client's map files, including the default UI; it started out for the [TokaZerk UI](https://github.com/tokajer/TokaZerkUI). For the Eden freeshard, labels (keeps, towns, bosses, dungeon entrances and more) can optionally be baked into the maps, since a UI can only show images.

## Contents
- [Quick start](#quick-start)
- [What it renders](#what-it-renders)
- [Using the window](#using-the-window)
- [Command line](#command-line)
- [Building from source](#building-from-source)
- [Data sources](#data-sources)
- [Changes in this fork](#changes-in-this-fork)
- [Changelog](#changelog)
- [License](#license)

## Quick start
1. Install the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (x64).
2. Download the latest build from [Releases](https://github.com/ZZerker/DAoC-MapCreator/releases) and unzip it anywhere.
3. Start `MapCreatorNext.exe`. The game folder (Eden or Blackthorn launcher, default install paths) is found on first start.
4. Tick a few zones, pick a preset and click Render.

Requires Windows x64 and a DAoC client installation. Only want the maps? The [map site](https://zzerker.github.io/daoc-ui-maps/) shows every zone and has UI ready packs for download.

## What it renders
**Outdoor zones**
- Terrain textures with relief shading from the height map at full map size
- Rivers, lakes and lava; shallow water lets the ground show through, so shores fade into the water
- Trees and buildings drawn from their models with their own textures, baked lighting (dark maps) and a soft drop shadow
- 3D view from the south: walls and trees drawn upward, footprints stay in place (on by default)
- Contact shadows (ambient occlusion) around and between buildings and keeps
- Zone bounds; the area outside is darkened, 3D buildings reaching into it stay clear

**New Frontiers keeps and towers**
- All 21 keeps and 54 towers as they stand on Eden, fully upgraded, in their realm's textures
- Built piece by piece from the client's keep models (`frontiers.mpk`), with relic temples, relic keeps, mile gates and bridges
- Optional, for maps that draw their own keep icons

**Capital cities** (Camelot, Jordheim, Tir na Nog)
- Built from the city models; ground layers (grass, soil, cobble, rock) blended per vertex as in the game, without visible tiling

**Dungeons**
- Built from the placed rooms with their baked lighting, brightened so dark dungeons stay readable without turning garish
- Lit like the client: the lights of `lights.csv` with the colors and radii of `zones\textures\lightdef.csv`
- One map per level where the client has level maps (`zNNN_LL`, e.g. the six levels of Darkness Falls), other levels faded underneath
- Instanced zones (Underground Forest, Dream and others) included

City and dungeon maps use the same frame as the client and bestiary maps, so a position lines up at `(zone coordinate - offset) / width`. Each one gets a `zNNN.frame.json` with its offset and width.

**Labels**
- Keeps and towers, bosses, places and docks, towns, dungeon entrances, ToA artifact encounters, trainers and services in the capitals, neighbor zones along the border
- Written as `zNNN.labels.json` next to each map and drawn at the final size in the TokaZerk UI font, so text stays sharp

**UI map set** (TokaZerk UI)
- Every zone, dungeon level and region map from one full render, at 512 and 256 px or any other size, with or without labels
- Region maps stitched from the zone renders, war maps for New Frontiers and the old frontiers
- The UI's `areas.dat` and `regions.dat` written with the same frames, so the position marker lines up

## Using the window
Tick zones in the list on the left (search by name or id, or tick whole groups such as a realm, an expansion or a zone type), set the options on the right or pick a preset, and click Render.

- **Showing maps from** (top bar) picks the render folder the window looks at. A render switches it to the folder it writes into.
- **Zone states** are shown in color: rendered (with date), queued, rendering, failed, not rendered, or archive newer (the game files changed after the render).
- **Map** tab: the selected zone's map with its labels, zoomable.
- **Activity** tab: running and finished zones and the log. Each render folder gets its own `render.log`.
- **Settings**: game folder, output folder, model cache cleanup and licenses. Saved in `%LOCALAPPDATA%\MapCreator\settings.json`, presets in `presets.json` next to it.

Good to know:
- Rendering is CPU and memory heavy, depending on map size and the number of parallel zones (12 by default).
- Use map sizes that are a power of 2 (512 to 4096). The terrain textures top out at 4096 px.
- The first render converts the models into a cache (`data\polys8`); later renders are much faster.

## Command line
`MapCreatorNext.exe` has four command line modes. Without any of them it opens the window. Folder arguments (`--dir`, `--labels`, the source of `--ui-maps` and `--site`) are relative to the output folder set in the window. Options not given on the command line come from the settings saved in the window; nothing is saved back.

### Render
```
MapCreatorNext.exe --render <zones> [--size 2048] [--dir <folder>] [--log render.log] [--parallel 12]
                   [--no-keeps] [--no-depth-water] [--labels <folder>] [--label-size 1024] [--labels-only]
                   [--no-console] [--ui]
```
| Parameter | Meaning |
|---|---|
| `--render <zones>` | Zones to render: ids and groups, see [Zone groups](#zone-groups). Can be given more than once |
| `--size <px>` | Map size in pixels (power of 2, 512 to 4096). Default: the window setting |
| `--dir <folder>` | Render folder under the output folder. Default: the window setting |
| `--log <file>` | Log file in the output folder. Default `render.log`. Ignored with `--ui`, where each render folder gets its own `render.log` |
| `--parallel <n>` | Zones rendered at the same time. Default: the window setting (12) |
| `--no-keeps` | Leave out the New Frontiers and battleground keeps and towers |
| `--no-depth-water` | Flat water color instead of depth shaded water |
| `--labels <folder>` | Also write labeled copies of the maps into this folder |
| `--label-size <px>` | Size of the labeled copies. Default: the map size |
| `--labels-only` | Do not render; label the existing maps in `--dir` into `--labels` (all zones in under a minute) |
| `--no-console` | Run fully headless, without the live console |
| `--ui` | Open the window and render there instead of in the console |

Without `--ui`, a console shows the run live (zones done, failed and left, time left, memory, one row per running zone, latest log lines) and stays open 30 seconds afterwards. Closing it stops the render. The log is written line by line with the zone id on every line. The exit code is 1 if a zone failed.

### Zone groups
`<zones>` is a comma separated list of zone ids and groups. `+` intersects groups, a comma adds them up. Case, spaces and dashes don't matter; unknown names are skipped with a warning.

| Group | Examples |
|---|---|
| Everything | `all` |
| Realm | `alb`, `mid`, `hib` (or `albion`, `midgard`, `hibernia`) |
| Expansion | `nf`, `of`, `si`, `toa`, `cata`, `dr`, `lotm`, `classic`, `foundations`, `tutorial` (or the full name, e.g. `new-frontiers`) |
| Zone type | `outdoor`, `city`, `dungeon`, `instance`, `bg`, `indoor` |
| Client zones not in the curated list | `client` |
| Preset | the name of a preset saved in the zone selection |

| Example | Renders |
|---|---|
| `--render nf+outdoor` | the 14 New Frontiers zones, without the battlegrounds |
| `--render alb+dungeon` | all Albion dungeons |
| `--render city` | Camelot, Jordheim, Tir na Nog |
| `--render 163,nf+bg` | zone 163 and the New Frontiers battlegrounds |
| `--render all --dir all_maps --labels all_maps_labeled` | every zone, plus labeled copies |
| `--render all --dir all_maps --labels all_maps_labeled --labels-only` | labels an existing render again |

### UI map set
```
MapCreatorNext.exe --ui-maps <render folder> [--dir <set>] [--game <client folder>] [--ui-sizes 512,256] [--no-labels]
```
| Parameter | Meaning |
|---|---|
| `--ui-maps <render folder>` | Full render to build the set from (no rendering, about 2 minutes) |
| `--dir <set>` | Target folder. Default `<render folder>_ui` |
| `--game <client folder>` | Client to read `zones.dat`, `regions.dat` and `areas.dat` from (e.g. Blackthorn). Default: the window setting |
| `--ui-sizes <list>` | Map sizes, one subfolder each. Default `512,256` |
| `--no-labels` | Unlabeled maps |

Writes the zone, dungeon level and region maps, `areas.dat`, `regions.dat` and the war map textures into `<set>\<size>`. Log: `ui_maps.log`.

### Map site export
```
MapCreatorNext.exe --site <render folder> [--dir <folder>] [--client eden] [--game <client folder>]
```
| Parameter | Meaning |
|---|---|
| `--site <render folder>` | Render to export (WebP viewer images, thumbnails, labels, `zones.json`) |
| `--dir <folder>` | Target folder. Default `<render folder>_site` |
| `--client <name>` | Client name used on the site (letters, digits, hyphens). Default `eden` |
| `--game <client folder>` | Client to read the zone list from. Default: the window setting |

Log: `site.log`.

### Scripts (`tools\`)
| Script and parameters | Does |
|---|---|
| `render_nf.ps1 [-Zones nf+outdoor] [-Size 2048] [-DdsSize <px>] [-Parallel 4] [-Areas <areas.dat>] [-AreaSize 256] [-Png]` | Renders zones and converts them to DXT1 `zNNN.dds` with labels. `-Areas` also writes the zoomed area maps `zNNN_AA.dds`, `-Png` a PNG next to every DDS |
| `render_all_shutdown.ps1 [-Zones all] [-Size 2048] [-Parallel 4] [-Dir all_maps] [-ShutdownDelay 300] [-NoShutdown]` | Builds, renders every zone, converts New Frontiers to DDS and shuts the computer down (cancel with `shutdown /a`) |
| `deploy_ui_maps.ps1 -Source <set> [-Repo <UI repo>] [-Folder <name> -Size <px>]` | Converts a UI map set to DXT1 and puts it into `Maps`, `Maps_large` (512), `Maps_small` (256) and `warmap` of a UI repo; old maps are removed. `-Folder -Size` writes one test folder only |
| `build_map_packs.ps1 -Target <folder> -Labeled <set> -Unlabeled <set> [-Sizes 256,512,1024] [-Zip] [-Client eden] [-Render <folder>] [-LabeledPng <folder>] [-Site <folder>] [-Tag <tag>] [-Repo <owner/name>]` | Builds the release files of the map site for one client: DDS packs per size and label variant, PNG packs and single zone PNGs, `downloads.json` |
| `publish_site.ps1 -Eden <site export> -Blackthorn <site export> [-Repo <folder>] [-Site <folder>] [-Assets <folder> -Tag <tag>] [-NoPush]` | Publishes the map site to GitHub Pages (`gh-pages`); `-NoPush` only builds the commit |
| `deploy_launcher_maps.ps1 -Source <render folder> [-Zones <launcher zones folder>] [-Restore]` | (admin) Puts the maps and the city and dungeon frames into the Eden launcher; originals are backed up once, `-Restore` puts them back |
| `copy_mpk_fixtures.ps1 [-GamePath <client folder>]` | Copies the game archives the tests use into `Mpk.Tests\TestData`; finds the Eden client itself without `-GamePath` |
| `GameFolder.ps1` | Dot-sourced by the scripts: finds the Eden and Blackthorn clients from their launchers and the default install folders |

The DDS conversion needs Python with Pillow; `texconv.exe` from [DirectXTex](https://github.com/microsoft/DirectXTex/releases) in `tools\bin` gives better DXT1 quality.

## Building from source
Niflib is a git submodule, so clone with `--recursive` (or run `git submodule update --init` in an existing clone):
```
git clone --recursive https://github.com/ZZerker/DAoC-MapCreator.git
dotnet build MapCreator.sln -c Release
dotnet test --solution MapCreator.sln -c Release
```
The program is written to `Releases\MapCreatorNext.exe`. Pushing a tag `v*` runs the tests, builds and publishes a GitHub release.

## Data sources
Everything comes from the game client, except:
- `data\MapFrames.csv`: city and dungeon map frames from the Eden bestiary
- `data\Keeps.csv`: New Frontiers keep and tower positions from the Eden war map, piece layouts from [Dawn-of-Light db-public](https://github.com/Dawn-of-Light/db-public)
- `data\Landmarks.csv`: places, docks and bosses for the labels
- `data\WarMaps.csv`: which zones the UI's war map textures show and where
- `data\ZoomAreas.csv`: room maps cut from a zone map where the client has no level data

## Changes in this fork
- .NET 10 (was .NET Framework 4.0), Magick.NET 14, `System.Numerics`; new Avalonia window
- Niflib built from source: [ZZerker/niflib.net](https://github.com/ZZerker/niflib.net), a .NET 10 fork without SharpDX
- Own reader for the game archives (MPK) instead of MPKLib
- Own triangle rasterizer with textures, trilinear filtering, 2x supersampling and a depth buffer; models use their texture layers and vertex lighting
- Every model drawn from its geometry; the hand-made images for relic temples, boats and piers are gone
- All client zones available, model and texture replacements (`NIFPROXY.csv`, `TEXPROXY.csv`) applied
- Batch mode with zone groups, several zones in parallel, live console view
- Much faster: shared model and texture caches, and the own rasterizer instead of one ImageMagick call per triangle (zone 171 fixtures: 54 s before, under 5 s now; all 14 New Frontiers zones at 2048 px in about 2 minutes)
- `fixtures.xml` only holds overrides, everything else is read from the models
- Naming rules enforced by `.editorconfig`, no build warnings

### Roadmap
- [x] Zone list from the client's `zones.dat`, including Eden's own zones
- [x] Textured buildings, city maps, dungeon maps per level, in the client's frames
- [x] Relief shading, blended ground layers, vertex lighting, depth shaded water, shadows
- [x] New Frontiers keeps and towers, names and points of interest
- [x] Maps for all playable dungeons, also the 16 that have no map in the game
- [x] Buildings, keeps and trees in 3D, contact shadows, dungeon lights
- [x] New window, own MPK reader, Niflib on .NET 10
- [x] Complete UI map set: zone, level, region and war maps
- [x] Public map site with download packs
- [ ] Old Frontiers keeps
- [x] Detect zones Eden has patched since the last render (window marks them "archive newer")
- [ ] Re-render only the patched zones in one step

## Changelog
**2.4.0** (2026-10-27)
- Blackthorn: height and water maps that end without their palette are read (Jamtland Mountains and two more zones failed before)
- The model cache is kept per game client: Blackthorn ships different models under Eden's names, and Blackthorn renders drew Eden's Camelot and dungeon models. The first render after updating converts the models once again
- The Blackthorn launcher's game path is found even when it mixes `/` and `\`
- Dungeon lights: `lights.csv` with the light types of `lightdef.csv` (color, radius), soft colored pools on a darker base
- Vertex colors that carry no baked lighting (one dark placeholder value on most of a mesh) are ignored: Kobold Undercity, Underground Forest, the cavern of zone 093 and the side rooms of the Inconnu halls are no longer black
- Brightened dungeons keep calmer colors: the dark olive moss of Fomor no longer turns lime, the many colored lights of Darkness Falls mix less garishly
- Moss floors get the same anti-tiling as grass (Fomor, Underground Forest)
- Trees in 3D by default; the small card evergreens that vanished when drawn flat are back
- Empty tree placeholders (Mountain top Tree) take the model of a tree with the same SpeedTree
- Dark maps at 1.4 instead of 1.5
- The log names why a model is not drawn instead of "too small to get drawn"
- `--ui-maps --no-labels` for unlabeled UI map sets, `tools\build_map_packs.ps1` for the download packs
- `--site` exports viewer images and `zones.json` for the map site, `tools\publish_site.ps1` publishes it
- Zones with an empty archive fall back to `datNNN.mpk`

<details>
<summary><b>2.3.0</b> (2026-10-05)</summary>

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
</details>

<details>
<summary><b>2.2.0</b> (2026-09-29)</summary>

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
</details>

<details>
<summary><b>2.1.0</b> (2026-09-26)</summary>

- Baked lighting (dark maps) on buildings and city floors
- Zone groups in batch mode (`all`, realms, expansions, zone types), so every playable dungeon renders in one run
- Niflib built from source as a .NET 10 fork, SharpDX removed
- Zoomed area maps (`zNNN_AA.dds`) and PNG previews in the DDS script
- Fixes: no guessed fill for bound pieces inside the map, Dun Crimthain heading, old NIF 3.03 models fall back to their newer copy
- Trees are always drawn from their models; the prerendered tree images are gone
</details>

**2.0.0** (2026-09-26): first release of the fork.

<details>
<summary><b>Original changelog (Merec)</b></summary>

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
</details>

## License
GPL v2 or later, as the original.
