# Product

## Register

product

## Users

Control-room operators working 24/7 shifts, monitoring tunnel infrastructure (cameras, environmental sensors, radar/map, node health) from a fixed workstation. The job to be done: notice, triage, and acknowledge alerts fast, verify sensor/camera state at a glance, and never miss a critical event during a long shift.

## Product Purpose

Station is a WinUI 3 desktop application (`net8.0-windows`, WinUI) for real-time monitoring of tunnel security infrastructure: live camera feeds, environmental/structural sensor data, node health, a map/radar view, and severity-graded alerts. Success looks like an operator being able to scan the screen and immediately know what's normal, what needs attention, and what's critical — with zero ambiguity under sustained, low-light, high-vigilance viewing conditions.

## Brand Personality

Calm, precise, authoritative. The interface should read as industrial and mission-control-grade: composed under pressure, information-dense without feeling chaotic, and trustworthy enough that operators act on what they see without double-checking.

## Anti-references

Not a consumer/marketing app — no decorative gradients, playful motion, or SaaS-dashboard gloss that competes with monitoring data for attention. Not a generic light "productivity tool" aesthetic; the monitoring surfaces are dark by design (see existing `DkBgBrush`/`Dk*` palette in `Styles/Colors.xaml`) and should stay that way regardless of the app's light/dark theme setting. Avoid anything that could delay recognition of a critical alert (low-contrast severity colors, subtle status changes, animation that hides state).

## Design Principles

- Severity grammar is sacred: color meaning (low/medium/high/critical, acknowledged/unacknowledged) must stay consistent everywhere and never be reused for decoration.
- Legibility over aesthetics: this is read continuously under fatigue and low ambient light — contrast and clarity always win over visual polish.
- Dark monitoring surfaces are load-bearing, not a stylistic choice — they exist for control-room glare/eye-strain reasons and must not be "lightened up" for consistency with the rest of the app.
- Calm density: pack real information into the UI, but through spacing and hierarchy, not ornamentation.
- Consistency across theme-aware (Light/Dark ResourceDictionary) and fixed-dark ("Dk*") style families — a control should look like it belongs to the same system in either mode.

## Accessibility & Inclusion

High-stakes clarity bar, above standard WCAG AA: severity/status colors must be distinguishable for colorblind operators (not color-alone encoding), text and controls must hold contrast under dim control-room lighting, and critical alerts must be unmistakable (color + shape/icon + text, not color alone).
