# Settings Page Visual Guide

## Design Overview

The Settings page has been transformed from a legacy WPF form style to a modern card-based design that matches the visual language of the Runs and Plan pages.

---

## Key Visual Changes

### Before → After

#### **1. Page Structure**

**Before:**
- White background (no distinction from cards)
- `CardExpander` controls (expandable/collapsible)
- Equal visual weight for all sections
- Controls embedded directly in expanders

**After:**
- Subtle neutral background (`SurfaceBackgroundBrush` - #F5F7FA)
- Fixed `Border` cards with elevation
- Clear visual hierarchy with cards floating on background
- Professional card-based layout with consistent spacing

---

#### **2. Card Design**

**Before:**
```
┌─────────────────────────────────────────┐
│ ▼ Path Configuration                   │  ← Expander header
├─────────────────────────────────────────┤
│ Assets Root:   [TextBox]  [Browse]     │
│ Runs Directory: [TextBox]  [Browse]    │
└─────────────────────────────────────────┘
```

**After:**
```
  Subtle background (#F5F7FA)
  ┌─────────────────────────────────────┐
  │ Path Configuration               │  ← Section title (16px, SemiBold)
  │                                     │
  │ Assets Root      [TextBox]    [📁]  │  ← Field label (13px, muted)
  │ Runs Directory   [TextBox]    [📁]  │
  │ Runner Executable [TextBox]   [📁]  │
  └─────────────────────────────────────┘
   ↑ Card shadow (subtle elevation)
```

**Card Specifications:**
- Background: White (`CardBackgroundBrush`)
- Corner radius: 10px
- Padding: 20px
- Border: 1px subtle gray
- Shadow: 8px blur, 2px depth, 30% opacity
- Margin between cards: 16px

---

#### **3. Typography Hierarchy**

**Before:**
- Page title: Standard style
- Section headers: Equal to labels
- Labels: Bold, same size as values
- Flat visual hierarchy

**After:**
- **Page title**: 24px, SemiBold (`PageTitleStyle`)
- **Card headers**: 16px, SemiBold, 20px bottom margin
- **Field labels**: 13px, regular, muted gray (secondary)
- **Subsection headers**: 11px, SemiBold, uppercase-style, tertiary color
- **Values/inputs**: Prominent, standard text color

Clear visual hierarchy: Title > Card Header > Input > Label

---

#### **4. Grid Alignment**

**Before:**
```
Assets Root:        [TextBox]  [Browse]
Runs Directory:     [TextBox]  [Browse]
Runner Executable:  [TextBox]  [Browse]
```
- Label width: 150px
- Inconsistent label lengths created ragged left edge

**After:**
```
Assets Root         [TextBox]        [📁]
Runs Directory      [TextBox]        [📁]
Runner Executable   [TextBox]        [📁]
Default Timeout     [NumberBox]
Max Retry Count     [NumberBox]
```
- Label width: 180px (consistent across all cards)
- All inputs align vertically
- Clean, professional grid layout
- Labels are muted (less visual weight than inputs)

---

#### **5. Browse Buttons**

**Before:**
```
[TextBox]  [ Browse ]
           ↑ Full button with text and icon
```

**After:**
```
[TextBox]      [📁]
               ↑ Icon-only button, Secondary appearance
                 Tooltip: "Browse..."
```

**Improvements:**
- Icon-only (folder icon)
- Secondary appearance (less dominant)
- Tooltip for clarity
- Compact, doesn't dominate the row
- Visual weight shifted to the input field

---

#### **6. Checkbox Grouping**

**Before:**
```
┌─────────────────────────────────────┐
│ ▼ Appearance                        │
├─────────────────────────────────────┤
│ Theme: [ComboBox]                   │
│ ☐ Show console window during exec  │
└─────────────────────────────────────┘
```

**After:**
```
┌─────────────────────────────────────┐
│ Appearance                          │
│                                     │
│ Theme               [ComboBox]      │
│                                     │
│ CONSOLE                             │ ← Subsection header
│ ☐ Show console window during exec  │
└─────────────────────────────────────┘
```

```
┌─────────────────────────────────────┐
│ Execution                           │
│                                     │
│ Default Timeout     [100] seconds   │
│ Max Retry Count     [3]             │
│                                     │
│ AUTOMATION                          │ ← Subsection header
│ ☐ Auto-refresh run history         │
│ ☐ Show debug output in console     │
└─────────────────────────────────────┘
```

**Improvements:**
- Related checkboxes grouped under subsection headers
- Clear visual separation
- 8px spacing between checkboxes
- No scattered controls

---

#### **7. Spacing Strategy**

**Vertical Rhythm:**
- Page title → First card: 24px
- Between cards: 16px
- Card header → First field: 20px
- Between fields: 16px
- Between checkboxes: 8px
- Last card → Save button: 24px

**Within Fields:**
- Label → Input: 0px (in same grid cell)
- Input → Browse button: 8px

**Card Interior:**
- All sides: 20px padding
- Creates breathing room
- Content never touches edges

---

#### **8. Page Layout**

**Before:**
```
┌─────────────────────────────────────────┐ ← Full width
│ Settings                                │
│                                         │
│ [Expander 1]                            │
│ [Expander 2]                            │
│ [Expander 3]                            │
│                                         │
│              [Save Changes]             │
└─────────────────────────────────────────┘
```

**After:**
```
  Background: #F5F7FA
  ┌───────────────────────────────────┐ ← Max 900px, left-aligned
  │ Settings                          │   Page margins: 16px
  │                                   │
  │ ┌─────────────────────────────┐   │
  │ │ Path Configuration          │   │ ← Card 1
  │ └─────────────────────────────┘   │
  │                                   │
  │ ┌─────────────────────────────┐   │
  │ │ Appearance                  │   │ ← Card 2
  │ └─────────────────────────────┘   │
  │                                   │
  │ ┌─────────────────────────────┐   │
  │ │ Execution                   │   │ ← Card 3
  │ └─────────────────────────────┘   │
  │                                   │
  │ ┌─────────────────────────────┐   │
  │ │ Import/Export Settings      │   │ ← Card 4
  │ └─────────────────────────────┘   │
  │                                   │
  │ ⚠ Unsaved changes  [Save Changes] │
  └───────────────────────────────────┘
```

**Improvements:**
- Max-width: 900px (optimal readability)
- Left-aligned (follows modern design patterns)
- Cards float on neutral background
- Clear visual separation from page background

---

## Color Palette Applied

| Element | Color | Purpose |
|---------|-------|---------|
| **Page background** | `#F5F7FA` | Subtle neutral, provides contrast for cards |
| **Card background** | `#FFFFFF` | Pure white, clean and professional |
| **Card border** | `#CED4DA` | Subtle gray, defines card edges |
| **Page title** | `#212529` | Near-black, maximum contrast |
| **Card headers** | `#212529` | Primary text color |
| **Field labels** | `#343A40` | Secondary text, muted |
| **Subsection headers** | `#6C757D` | Tertiary text, subtle |
| **Warning text** | `#DC2626` | Red, for unsaved changes |
| **Primary button** | `#2563EB` | Brand blue |

---

## Responsive Considerations

- **Max-width**: 900px prevents overly wide forms on large screens
- **Label column**: 180px accommodates longest label without wrapping
- **Inputs**: Flexible width, expands to fill available space
- **Cards**: Stack vertically, natural scrolling behavior
- **Browse buttons**: Fixed width (icon-only), won't wrap

---

## Accessibility Improvements

1. **Clear hierarchy**: Screen readers can distinguish between titles, headers, and labels
2. **Consistent structure**: Predictable layout aids navigation
3. **Tooltips**: Icon-only buttons have descriptive tooltips
4. **Keyboard navigation**: Maintained from original design
5. **Visual grouping**: Related controls are visually grouped
6. **Color contrast**: Maintains WCAG AA standards

---

## Design System Consistency

The redesigned Settings page now matches:

✅ **Runs page**: Same card style, elevation, spacing  
✅ **Plan page**: Same typography hierarchy, layout patterns  
✅ **Navigation**: Same background color as page surface  
✅ **Design tokens**: Uses shared styles (`PageTitleStyle`, `SectionTitleStyle`, `CardBackgroundBrush`, etc.)

**Result**: A unified, professional application design across all pages.
