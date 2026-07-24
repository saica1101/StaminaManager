# Design System: StaminaManager
**Project ID:** 8017157277609457075

## 1. Visual Theme & Atmosphere

StaminaManager follows a **Precision Utility / Instrument Cluster** direction: a
Windows 11-native utility that makes multiple regeneration timers readable at a
glance without resembling a generic SaaS dashboard. The signature element is the
status instrument on every game card: a circular stamina meter paired with an
explicit state label and completion time.

The interface uses Fluent hierarchy, restrained density, and Windows materials.
Mica is the default backdrop, while Acrylic, Blur, Transparent, and Solid are
user-selectable. Content surfaces retain enough opacity to remain readable over
every backdrop. High Contrast, disabled transparency, unsupported graphics, and
remote sessions always receive a solid fallback.

## 2. Color Palette & Roles

- **Windows Accent Blue** (`#0078D4` fallback): primary actions, selection, focus,
  and active navigation. Use the user's Windows accent when available.
- **Safe Green** (`#107C10` light, `#6CCB5F` dark): stamina below 50%.
- **Attention Orange** (`#CA5010` light, `#FFB900` dark): stamina from 50% through
  80%.
- **Urgent Red** (`#C42B1C` light, `#FF99A4` dark): stamina above 80%, full, or
  over the natural recovery cap.
- **Mica Light Fallback** (`#F3F3F3`): light-mode window surface when material is
  unavailable.
- **Mica Dark Fallback** (`#202020`): dark-mode window surface when material is
  unavailable.
- **Light Content Surface** (`#FFFFFF`): cards and fields over translucent light
  backdrops.
- **Dark Content Surface** (`#2C2C2C`): cards and fields over translucent dark
  backdrops.
- **Light Primary Text** (`#1A1A1A`) and **Dark Primary Text** (`#FFFFFF`): main
  labels and stamina values.
- **Light Secondary Text** (`#5D5D5D`) and **Dark Secondary Text** (`#CFCFCF`):
  recovery times and supporting descriptions.

Status is never communicated by color alone. Every colored ring is paired with
one of: `余裕`, `注意`, `満タン間近`, `満タン`, or `自然回復停止中`.

## 3. Typography Rules

- Use Windows platform typography: **Segoe UI Variable** for Latin text and
  **Yu Gothic UI** as the explicit Japanese fallback.
- Do not use Inter, Arial, Roboto, or an unexplained generic system font.
- Page title: 28 px, semibold, compact line height.
- Card title: 16 px, semibold, one-line ellipsis when necessary.
- Stamina value: 18–28 px depending on view, semibold, tabular numerals.
- Body and settings labels: 14 px, regular.
- Secondary metadata: 12 px, regular, never the sole carrier of essential state.
- Respect Windows text scaling without fixed-height text containers.

## 4. Component Stylings

* **Buttons:** Native WinUI `Button` and `AppBarButton` treatments. Use one accent
  action per context. Icon-only actions require accessible names and tooltips.
* **Cards/Containers:** Subtly rounded 8 px corners, 1 px low-contrast outline, and
  tonal elevation instead of heavy shadows. Game cards are instruments, not
  decorative tiles.
* **Progress Rings:** Circular, 6–8 px status stroke with a neutral track. Center
  content shows `current / maximum`; an adjacent text label explains the state.
* **Inputs/Forms:** Native `TextBox`, integer `NumberBox`, `ComboBox`,
  `ToggleSwitch`, and file pickers. Labels remain visible; validation appears
  directly beneath the affected field.
* **Navigation:** Native `NavigationView` with only Overview and Settings.
* **Dialogs:** Native `ContentDialog` for add/edit and destructive confirmation.
* **Backdrops:** Mica by default; Desktop Acrylic, WinUIEx Blur,
  WinUIEx Transparent, and Solid are live-preview alternatives. Transparent mode
  preserves opaque-enough cards and fields.
* **Motion:** Restrained 120–180 ms state transitions only. No looping or
  decorative startup animation. Reduced Motion removes nonessential transitions.

## 5. Layout Principles

- Use a 4 px spacing base with 8, 12, 16, 20, 24, and 32 px steps.
- Standard window content uses 20 px margins and 12 px card gaps.
- Overview renders three columns at the standard wide window, two columns at a
  medium width, and one column when narrow.
- The add-game card always follows the last registered game and becomes the first
  item in the empty state.
- The standard window starts near 1120 × 760 effective pixels with a practical
  minimum near 720 × 520. Compact mode uses a narrow utility layout near
  420 × 520 and restores the previous normal bounds when exited.
- Compact mode shows one selected game, its large progress ring, recovery time,
  and update/edit actions.
- Avoid double-card nesting. Backdrop provides window depth; cards provide the
  single required content layer.

