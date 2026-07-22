# Station — Resume Notes

## Where things stand

`Views/SensorChartsPage.xaml.cs` lays out sensor charts in a fixed Grid (`ChartsHost`,
`RowsPerLayout` rows × the chosen column preset), same model as `LiveVideoPage`'s camera
grid: each `ChartSlot` has `Row`/`Column`/`RowSpan`/`ColumnSpan`, and a card grows by
spanning whole cells (a ratio of the available space) rather than by raw pixels. This
replaced an earlier free-form `Canvas` layout (arbitrary pixel Width/Height per card, a
manual row-flow-with-gap-fill reflow, and corner-drag resize that redistributed pixel
width across row neighbors) — that approach made it easy to drag a card into a state that
overflowed or skewed the whole board, which a fixed Grid can't do: Star-sized rows/columns
always sum to exactly the available space, so a card can only ever occupy a whole number of
cells. All work in this area is build-verified (`dotnet build Station.csproj -c Debug -p:Platform=x64 -v minimal`):

- **Grid/span model** (`ChartSlot.Row/Column/RowSpan/ColumnSpan/IsHiddenBySpan`,
  `RebuildChartsGridDefinitions()`, `SlotAt()`, `IsPlainSlot()`, `CanExpandRight()`/
  `CanExpandDown()`, `ExpandColumn()`/`CollapseColumn()`/`ExpandRow()`/`CollapseRow()`) —
  ported directly from `LiveVideoViewModel`'s `CameraSlotViewModel` span logic, adapted to
  this page's plain code-behind (no separate ViewModel here). Growing a span into a
  neighboring cell hides that cell (`IsHiddenBySpan`) and unassigns whatever sensor it held;
  every Expand/Collapse call ends by calling `BuildCharts()` (full rebuild) rather than a
  live per-tick reflow — resize is a discrete, one-shot action decided at
  `ManipulationCompleted`, not accumulated pixel deltas.
- **Edge resize grips** (`BuildEdgeGrip()`, replacing the old 4-corner-grip
  `BuildCornerGrip()`): right edge drags the column span, bottom edge drags the row span —
  same two-grip affordance as `LiveVideoPage`'s right/bottom grips. A grip is only built at
  all when that direction is actually usable (`CanExpandRight`/`CanExpandDown`, or already
  spanned so it can be dragged back); dragging past half a cell's size toggles the span by
  exactly one cell.
- **Span-expand live drag-preview dim** (`BuildEdgeGrip()`'s `ManipulationDelta` handler +
  new `NeighborsForExpand()` helper): first tried dimming the absorbed neighbor cell via a
  post-release Storyboard fade before `BuildCharts()` — rejected, since the user wanted the
  feedback tracking the live drag itself, not a fixed animation that plays after mouse-up.
  Now every `ManipulationDelta` tick recomputes, live, whether the current `dragTotal` is
  past the half-cell threshold (`Threshold()`, a local function factored out of the
  duplicated cellSize/threshold calc) and, if so, which neighbor slot(s) would be absorbed
  right now (`NeighborsForExpand(slot, isRight)` — the same enumeration `ExpandColumn()`/
  `ExpandRow()` use to actually mark `IsHiddenBySpan`, but here only used to look up their
  live elements via `_slotElements` and set `Opacity = 0.35`). Dragging back below the
  threshold restores `Opacity = 1` on whatever was dimmed, so the dim strictly tracks
  mouse position, not a committed action — `ManipulationCompleted` still does the actual
  `ExpandColumn()`/`ExpandRow()`/`Collapse*()` call as before (unchanged), and clears any
  leftover dim state first (`ClearDim()`) since the very next thing that happens is usually
  a full `BuildCharts()` rebuild anyway.
- **Column-preset picker** (`RebuildSlotCount()`) now fully rebuilds `_slots` on every
  preset change — captures previous `SensorId` assignments by index, then reassigns them
  onto fresh 1×1 slots at the new preset's row-major positions, same pattern as
  `LiveVideoViewModel.RebuildSlots()`. Spans never carry across a preset change.
- **Fullscreen chart expand**, mirroring `LiveVideoPage`'s camera-focus mode (kept from
  before, simplified by the Grid move): each card's header has an `expandBtn` (hover-reveal,
  same `E740` glyph as LiveVideoPage's "Phóng to") next to the unassign button
  (`ExpandCard()`/`CollapseExpandedCard()`/`FullscreenClose_Click()`). Expanding re-parents
  the exact same card `Border` from `ChartsHost` into `FullscreenCardHost` inside
  `FullscreenOverlay` (`Grid.RowSpan="3"` over the whole charts area) — no
  chart/series objects are recreated, so live tick updates keep landing on the same
  `CartesianChart`/`ObservableCollection` untouched while it's shown fullscreen. Since cards
  never have an explicit Width/Height now (they fill their Grid cell via default Stretch),
  expand/collapse needs no size bookkeeping at all — collapsing just re-applies
  `Grid.SetRow/Column/RowSpan/ColumnSpan` from the slot and adds the card back.
  `BuildCharts()` calls `CollapseExpandedCard()` defensively (instant, no animation) before
  every full rebuild so the detached card is never orphaned in `FullscreenCardHost`, and
  each edge grip's `ManipulationCompleted` no-ops when `slot == _expandedSlot` so dragging a
  grip on the fullscreen-displayed card can't trigger a confusing auto-collapse-and-resize.
- **Fullscreen open/close animation** (`PlayFullscreenTransition()`): `FullscreenOverlay`'s
  background is the translucent `DkScrimBrush` (`Styles/Colors.xaml`, `#CC0B1020`) instead of
  an opaque brush, so the dimmed chart grid is still visible underneath rather than hard-cut
  away — this is the "làm mờ nền" (dim the background) behavior. The expand/collapse motion
  itself is a FLIP transform, not a flat opacity/scale pop (a flat pop from ~94%→100% scale
  read as barely-there, since the card already looked "full size" the instant it was
  re-parented) — the card now visibly grows from its exact original grid-cell position/size
  up to the fullscreen area, and shrinks back down to that same rect on close.
  `ComputeFullscreenFlipTransform()` runs in `ExpandCard()` **before** the card is removed
  from `ChartsHost` (its on-screen position is only knowable while it's still there), using
  `card.TransformToVisual(ChartsAreaRoot)` — `ChartsAreaRoot` is the newly-named "CHARTS AREA"
  Grid (always visible/laid out, unlike `FullscreenOverlay` which is `Collapsed` until this
  moment and so can't be trusted for measurement yet). The result (`_fsScaleX/Y`,
  `_fsTranslateX/Y`) is a corner-anchored scale+translate pair applied to
  `FullscreenCardHost`'s `CompositeTransform` (`FullscreenCardTransform`, replacing the old
  plain `ScaleTransform`) — `CompositeTransform.CenterX/Y` default to `(0,0)` (the element's
  own top-left corner), which is exactly the anchor point the FLIP math assumes; do not add
  `RenderTransformOrigin` back onto `FullscreenCardHost`, it would shift that anchor and break
  the math. `PlayFullscreenTransition(opening, onCompleted)` animates `FullscreenOverlay.Opacity`
  plus all four `CompositeTransform` properties over 320ms (`CubicEase`, `EaseOut` opening /
  `EaseIn` closing) — from the captured FLIP values to identity `(1,1,0,0)` when opening, or
  the reverse when closing. Closing via the header's close button calls
  `CollapseExpandedCardAnimated()`, which reuses the same `_fs*` values captured at expand time
  (layout can't change while a card is expanded — resize grips no-op on the expanded slot, and
  a rebuild force-collapses instantly first) and only performs the actual
  re-parenting/`Visibility = Collapsed` in `CollapseExpandedCard()` once the Storyboard's
  `Completed` fires — so the card is still on screen shrinking back to its origin rather than
  snapping away. The defensive rebuild path in `BuildCharts()` still calls the plain, instant
  `CollapseExpandedCard()` directly (no animation), since that's a background rebuild, not a
  user-driven close.
- Tried naming the old `ScrollViewer` wrapper (to explicitly hide it while a card was
  fullscreen) but WinUI's XamlCompiler silently dropped that one field from the generated
  partial class for reasons unclear. Moot now — the chart area is a bare `Grid` (no
  `ScrollViewer`) inside a `Border Padding="14"`, since a fixed Grid fills its allocated row
  without needing to scroll.

## LiveVideoPage: same live drag-preview dim, ported from SensorChartsPage

`LiveVideoPage`'s camera-grid resize grips (`RightGrip_Manipulation*`/`BottomGrip_Manipulation*`
in `Views/LiveVideoPage.xaml.cs`) got the same live-drag dim feedback described above for
`SensorChartsPage`, since this page is proper MVVM (bound `ItemsControl` over
`LiveVideoViewModel.Slots`) rather than plain code-behind, the mechanism is wired through
bound view-model state instead of direct `FrameworkElement.Opacity` sets:

- `CameraSlotViewModel` gained `IsResizePreviewDimmed` (bool, `[ObservableProperty]`) and a
  computed `ResizePreviewOpacity` (`0.35` when dimmed, `1.0` otherwise) — same
  property/computed-property pairing already used for `IsDragHighlighted`/
  `DragHighlightOpacity` on this same class. The card's root `Grid` in
  `LiveVideoPage.xaml`'s `DataTemplate` (`Grid Margin="0,0,12,12"`, the one wrapping both the
  empty-slot and filled-slot visuals) binds `Opacity="{x:Bind ResizePreviewOpacity,
  Mode=OneWay}"` — deliberately the card visual only, not the sibling resize-grip `Grid`s
  further down the same template, so a grip never dims itself.
- `LiveVideoViewModel` gained `GetExpandNeighbors(CameraSlotViewModel slot, bool isRight)`,
  factored out of `ExpandColumn()`/`ExpandRow()` (which now both call it instead of duplicating
  the `SlotAt` loop) — the same `NeighborsForExpand` extraction already done on
  `SensorChartsPage`, so both pages compute "which cell(s) would this absorb" the same way.
  This is what lets the view ask, mid-drag, which slot(s) are about to be absorbed without
  committing to the expand.
- `LiveVideoPage.xaml.cs`'s `RightGrip_ManipulationDelta`/`BottomGrip_ManipulationDelta` now
  call a new `UpdateResizeDim(slot, isRight, wantsExpand)` on every tick — `wantsExpand` is the
  same `dragTotal > threshold && CanExpandRight/Down` check `ManipulationCompleted` already
  used, just evaluated live instead of only at release. It sets `IsResizePreviewDimmed = true`
  on the current target neighbor(s) and restores `false` on whatever was previously dimmed but
  no longer is, tracked via a `_resizeDimmedSlots` field. `ManipulationStarted` and
  `ManipulationCompleted` both call `ClearResizeDim()` (start: so a fresh drag doesn't inherit
  stale dim state; completed: before actually calling `ExpandColumn`/`ExpandRow`/`Collapse*`,
  same ordering as `SensorChartsPage`'s `ClearDim()` call). The actual expand/collapse decision
  in `ManipulationCompleted` is unchanged — this only adds a live preview, it doesn't change
  when the resize itself commits.
- Build-verified (`dotnet build Station.csproj -c Debug -p:Platform=x64 -v minimal`, 0 errors);
  not yet interactively confirmed in the running app.

## LiveVideoPage: resize-grip hover cursor (`views:CursorGrid`)

`SensorChartsPage`'s edge grips already changed the mouse cursor to a resize arrow
(`InputSystemCursor` `SizeWestEast`/`SizeNorthSouth`) on hover, via a private `CursorGrid : Grid`
subclass that exposes the otherwise-`protected` `UIElement.ProtectedCursor`. `LiveVideoPage`'s
right/bottom grips only dimmed/undimmed their bar on hover (`ResizeGrip_PointerEntered/Exited`)
with no cursor change, since they were plain `<Grid>` elements declared in XAML rather than
built in code — a private nested class can't be referenced from XAML. Fixed by making
`CursorGrid` a **public** top-level class in `Station.Views` (`Views/LiveVideoPage.xaml.cs`,
outside the page's partial class) and declaring the two grips in `LiveVideoPage.xaml` as
`<views:CursorGrid>` (new `xmlns:views="using:Station.Views"`) instead of `<Grid>`. Their
`PointerEntered` handlers are now direction-specific (`RightGrip_PointerEntered`/
`BottomGrip_PointerEntered`, each forwarding to a shared `ResizeGripPointerEntered(sender,
cursor)`) so the right grip sets `_horizontalResizeCursor` and the bottom grip sets
`_verticalResizeCursor` on `grip.HoverCursor` in addition to the existing bar-opacity change;
`PointerExited` still uses one shared handler that clears `HoverCursor` back to `null`. Build
verified, 0 errors.

## Possible follow-ups (not requested yet)

- Keyboard-driven resize (arrow keys on a focused grip), matching `LiveVideoPage`'s
  `RightGrip_KeyDown`/`BottomGrip_KeyDown` — skipped for now to keep this change scoped to
  the Canvas→Grid migration.

## Build command

```
dotnet build Station.csproj -c Debug -p:Platform=x64 -v minimal
```

Default `dotnet build` (AnyCPU) fails — always pass `-p:Platform=x64`.
