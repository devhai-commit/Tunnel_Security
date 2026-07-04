# Product

## Register

product

## Users

Tunnel infrastructure operators and monitoring engineers working in control rooms in Hanoi, Vietnam. They monitor multiple underground tunnel stations simultaneously, respond to intrusion and environmental alerts, and manage sensor/camera networks. Environment: low-light control rooms, large displays (1920×1080+), high time pressure. Critical status must be scannable in under 2 seconds — operators cannot afford to hunt for information.

## Product Purpose

An IoT-based intrusion detection and environmental monitoring system for underground sewer tunnel infrastructure. Two WinUI 3 desktop apps: **Station** (local monitoring for one tunnel station) and **Center** (centralized view across all stations). Integrates real-time sensor readings, camera feeds, alert management, device health, and historical analytics. Data flows via SignalR from ASP.NET Core 8 backend with PostgreSQL/TimescaleDB time-series storage. Success: operators catch and respond to incidents faster, with full confidence in the data they're seeing.

## Brand Personality

Urgent, clear, reliable. The interface should feel engineered for its job — purpose-built, not designed. It earns trust through consistency and precision, not through visual polish. When something is wrong, you know immediately; when everything is fine, the interface gets out of the way.

## Anti-references

- Consumer apps: playful, rounded, colorful — this is not a mobile product or e-commerce dashboard
- Generic SaaS/Bootstrap admin panels: sidebar + widget grid + donut charts repeated across every screen
- Over-animated or visually noisy UI: no gratuitous transitions, spinning loaders, or decorative motion
- Overly minimal or bare: white backgrounds, gray text, empty-state prototype aesthetic — needs clear depth and hierarchy

## Design Principles

1. **Information over decoration** — Every visual element carries data or status meaning. No chrome without purpose. If a visual detail doesn't communicate something, remove it.
2. **Severity is unambiguous** — Alert states (Critical / High / Medium / Low / Healthy) must be instantly distinguishable at a glance, including in peripheral vision. Color, shape, and size all reinforce status — never rely on color alone.
3. **Density with clarity** — Pack information in, but never at the cost of scannability. Hierarchy within dense layouts matters more here than in consumer products.
4. **Purpose-built, not polished** — The UI should look engineered for this specific job, like SCADA or military HMI. Utility-first aesthetics; ornament only where it aids comprehension.
5. **Consistency signals reliability** — Visual predictability is a feature. Operators learn the system once; the system must not surprise them. Patterns repeat exactly across screens.

## Accessibility & Inclusion

- Dark theme is the correct default and primary mode — operators work in low-light control rooms
- All text must hit ≥4.5:1 contrast against dark backgrounds (WCAG AA minimum)
- Critical status colors must remain distinguishable without relying solely on hue (support color blindness via shape/icon/label reinforcement)
- Layouts must scale well on large monitors (1920×1080+); information density should increase at larger sizes, not just stretch
- No WCAG level formally required, but AA contrast is the floor
