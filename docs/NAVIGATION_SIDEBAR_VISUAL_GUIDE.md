# Navigation Sidebar - Visual Quick Reference

## Before vs After

### BEFORE (200px wide)
```
┌─────────────────────┐
│  📋 Plan            │
│  ▶️ Run             │
│  🕒 Runs            │
│ ──────────────────  │
│  ⚙️ Settings        │
└─────────────────────┘
```

### AFTER (56px wide)
```
┌────┐
│ 📋 │ ← Plan tooltip
│ ▶️ │ ← Run tooltip
│ 🕒 │ ← Runs tooltip
│────│
│ ⚙️ │ ← Settings tooltip (bottom)
└────┘
```

## Selection States

### Normal State
```
┌────┐
│    │  ← Transparent background
│ 📋 │  ← Gray icon
│    │
└────┘
```

### Hover State
```
┌────┐
│░░░░│  ← Light gray background
│ 📋 │  ← Darker icon
│░░░░│
└────┘
```

### Selected State (Current Page)
```
█┌───┐
█│▓▓▓│  ← Light blue background
█│ 📋│  ← BOLD blue icon
█│▓▓▓│
█└───┘
└─── 3px blue accent bar
```

## Visual Indicators Summary

| State | Background | Icon Color | Icon Weight | Left Bar |
|-------|-----------|------------|-------------|----------|
| **Normal** | Transparent | Gray (Secondary) | Normal | None |
| **Hover** | Light Gray | Dark Gray (Primary) | Normal | None |
| **Selected** | Light Blue (#E7F1FF) | Brand Blue (#174AD8) | **SemiBold** | 3px Blue |
| **Selected+Hover** | Medium Blue (#D1E7FD) | Brand Blue | **SemiBold** | 3px Blue |

## Layout Structure

```
┌─────────────────────────────┐
│      TITLE BAR              │
├────┬────────────────────────┤
│ 📋 │                        │
│ ▶️ │                        │
│ 🕒 │   CONTENT FRAME        │
│    │   (Pages render here)  │
│    │                        │
│    │                        │
│────│                        │
│ ⚙️ │                        │
├────┴────────────────────────┤
│      STATUS BAR             │
└─────────────────────────────┘
 ^
 56px wide
```

## Tooltips

When hovering over icons:
- 📋 → "Plan"
- ▶️ → "Run"
- 🕒 → "Runs"
- ⚙️ → "Settings"

## Keyboard Navigation

1. **Tab** → Focus moves to first nav button (Plan)
2. **Tab** → Run
3. **Tab** → Runs
4. **Tab** → Settings
5. **Enter/Space** → Navigate to focused page

### Focus Indicator
```
┌────┐
│╔══╗│  ← Blue border (1px)
│║📋║│
│╚══╝│
└────┘
```

## Accessibility Features

### For Screen Readers
- Button role recognized
- Tooltip text read aloud
- Selected state announced

### For Keyboard-Only Users
- Full tab navigation
- Visible focus indicators
- Enter/Space activation

### For Color-Blind Users
Selection visible through:
1. **3px left bar** (structural indicator)
2. **Font weight change** (SemiBold vs Normal)
3. Not reliant on color alone ✓

### For Low Vision Users
- **44px hit targets** (comfortable size)
- High contrast between states
- 20px icons (clear visibility)

## Technical Specs

### Button Dimensions
- **Width**: 48px
- **Height**: 44px
- **Margin**: 4px horizontal, 2px vertical
- **Border Radius**: 6px
- **Icon Size**: 20px (FontSize)

### Spacing
- **Top margin**: 8px
- **Item spacing**: 4px vertical
- **Divider margin**: 12px vertical
- **Bottom margin**: 8px
- **Accent bar width**: 3px

### Colors (Hex)
- **Accent Bar**: #174AD8 (Brand Primary)
- **Selected BG**: #E7F1FF (Light Blue)
- **Selected Hover**: #D1E7FD (Medium Blue)
- **Normal Hover**: #E9ECEF (Light Gray)
- **Icon Normal**: #6C757D (Gray)
- **Icon Selected**: #174AD8 (Brand Blue)

## Animation Notes

Currently no animations, but could add:
- Accent bar slide-in (0.2s ease)
- Background fade (0.15s ease)
- Icon scale on hover (1.05x, 0.1s ease)

---

**Design Goal Achieved**: Modern, accessible, space-efficient navigation that maintains 100% functionality while improving visual clarity and following Fluent UI principles.
