# Sensor Category Mapping Fix (Station app)

Context doc for future sessions — explains a bug and a UI rework in the Station
(WinUI 3) app's sensor-monitoring pages. Read this before touching sensor
category/type mapping code in `Station/`.

## Background

Backend sensor types (`BackendV2.Models.SensorType`, int enum):

```
0=Radar, 1=Vibration, 2=SmokeFire, 3=Temperature, 4=Humidity,
5=Gas, 6=Pressure, 7=WaterLevel, 8=Motion, 9=Light
```

The Station client has its own classification enum, `Station.Models.AlertCategory`,
used across Alerts, Devices, MonitoringDashboard, DataPage, and SensorChartsPage:

```csharp
enum AlertCategory
{
    Temperature, Humidity, Radar, Infrared, Light,
    Accelerometer, WaterLevel, Intrusion, Equipment, Connection, Other
}
```

(`WaterLevel` was added as part of this fix, inserted after `Accelerometer`.)

`Station/ServicesV2/DataService.cs` → `MapCategory(int sensorType)` is the
**single translation point** from backend `SensorType` to client `AlertCategory`.

Only 5 real sensors are actually seeded/simulated (`BackendV2/Data/TopologySeeder.cs`):
Light, WaterLevel, Temperature, Humidity, Radar. Vibration and Infrared/Motion
have no real data source — they aren't bugs, they're just absent from the
simulator.

## Root-cause bugs found and fixed

1. **`DataService.MapCategory` was missing cases for `7` (WaterLevel) and `9`
   (Light)** — both silently fell through to `AlertCategory.Other`, making
   Light and WaterLevel sensors invisible to every UI filter checking for a
   specific type string. Fixed by adding explicit cases for all backend values,
   with `SmokeFire`/`Gas`/`Pressure` (no 1:1 client equivalent) mapped to `Other`.

2. **`DataPage.xaml.cs` → `LoadChartsForAllNodes`** explicitly excluded
   `Type == "radar"` from ever rendering, even though `CreateChartCard`
   already had working radar-rendering logic (`CreateRadarChart`). Fixed by
   removing the exclusion.

3. **`SensorChartsPage.xaml.cs` duplicates the same category→type-string
   mapping logic as `DataPage`, independently** (`SensorTypeString`,
   `ChartColor`, `AccentColor`, `SensorLabel`, `SensorUnit`). It had **no**
   Radar or WaterLevel cases at all before this fix (worse than DataPage,
   which at least partially had Radar wired). Both pages needed the fix
   applied separately since the mapping code isn't shared.

## UI rework

Per user request, both `DataPage` and `SensorChartsPage` had their selectable
sensor-type options changed from `{ Temperature, Humidity, Light, Vibration,
Infrared }` to `{ Temperature, Humidity, Light, WaterLevel, Radar }` —
Vibration/Infrared removed (no real data), WaterLevel/Radar added (real,
previously broken/hidden).

- **DataPage**: multi-select via `HashSet<string> _selectedTypes` +
  checkboxes. Removed `ChkVibration`/`ChkInfrared`, added `ChkWaterLevel`
  (accent `#50A0FF`), promoted the previously hidden `ChkRadar` to visible
  (accent `#00FF88`).
- **SensorChartsPage**: single-select via `string _selectedType` + tappable
  sidebar `Border` items. Removed `BtnInfrared`/`BtnVibration`, added
  `BtnWaterLevel`/`BtnRadar` with matching colors/labels.

Both pages' `MapCategoryToType`/`SensorTypeString` helpers gained
`AlertCategory.WaterLevel => "waterlevel"` (and Radar, for SensorChartsPage)
cases. Old `"infrared"`/`"vibration"` branches were left in place in helper
switch statements (harmless dead code, no UI element produces those strings
anymore) rather than removed, to minimize diff.

## Key files

| File | Role |
|---|---|
| `Station/Models/Alert.cs` | `AlertCategory` enum definition |
| `Station/ServicesV2/DataService.cs` | `MapCategory` — backend int → `AlertCategory` (the actual bug) |
| `Station/Views/DataPage.xaml(.cs)` | Multi-select checkbox chart grid |
| `Station/Views/SensorChartsPage.xaml(.cs)` | Single-select sidebar chart view |
| `BackendV2/Data/TopologySeeder.cs` | Confirms which sensor types actually have seeded data |

## Verification

`dotnet build Station/Station.csproj -v quiet -p:Platform=x64` — must show 0
`): error` lines (use `-p:Platform=x64` explicitly; `AnyCPU` fails to build
the packaged app host regardless of these changes — pre-existing, unrelated).

## Status

Both DataPage and SensorChartsPage fixes are implemented and build-verified.
Not yet manually verified in the running UI (no `dotnet run` test done in
this session — see the "test UI in browser/app" guidance in CLAUDE.md).
