# Execution Timeline - Visual Design Guide

## 整体布局

```
┌────────────────────────────────────────────────────────────────────┐
│  Execution Timeline                                                │
├────────────────────────────────────────────────────────────────────┤
│                                                                    │
│   Timeline   Event Content                              Timestamp │
│   ┌──────┐   ┌──────────────────────────────────────┐  ┌────────┐│
│   │      │   │                                      │  │        ││
│   │   ○  │   │  Test Suite Started                 │  │14:20:00││
│   │   │  │   │  Starting test suite: Smoke Tests   │  │  .123  ││
│   │      │   │                                      │  │        ││
│   └──────┘   └──────────────────────────────────────┘  └────────┘│
│                                                                    │
│   ┌──────┐   ┌──────────────────────────────────────┐  ┌────────┐│
│   │      │   │                                      │  │        ││
│   │   ○  │   │  Test Case Started                  │  │14:20:01││
│   │   │  │   │  Executing: CPU_Stress_Test         │  │  .456  ││
│   │      │   │                                      │  │        ││
│   └──────┘   └──────────────────────────────────────┘  └────────┘│
│                                                                    │
│   ┌──────┐   ┌──────────────────────────────────────┐  ┌────────┐│
│   │      │   │                                      │  │        ││
│   │   ●  │   │  Test Case Completed                │  │14:20:05││
│   │   │  │   │  PASS: CPU test passed all checks   │  │  .789  ││
│   │      │   │                                      │  │        ││
│   └──────┘   └──────────────────────────────────────┘  └────────┘│
│                                                                    │
│   ┌──────┐   ┌──────────────────────────────────────┐  ┌────────┐│
│   │      │   │                                      │  │        ││
│   │   ⚠  │   │  Test Case Warning                  │  │14:20:06││
│   │   │  │   │  High temperature detected: 85°C    │  │  .012  ││
│   │      │   │                                      │  │        ││
│   └──────┘   └──────────────────────────────────────┘  └────────┘│
│                                                                    │
└────────────────────────────────────────────────────────────────────┘
```

## 时间线节点类型

### 1. Started Events（开始事件）
```
   ○  <- 空心圆（蓝色边框，2px）
   │     背景：透明
   │     边框：#3B82F6（蓝色）
   │     大小：20x20px
```

### 2. Completed Events（完成事件）
```
   ●  <- 实心圆（绿色）
   │     背景：#22C55E（绿色）
   │     边框：无
   │     大小：20x20px
```

### 3. Warning Events（警告事件）
```
   ⚠  <- 实心圆 + 图标（橙色）
   │     背景：#EAB308（橙色）
   │     图标：\uE7BA（Warning）
   │     大小：20x20px
```

### 4. Error Events（错误事件）
```
   ✕  <- 实心圆 + 图标（红色）
   │     背景：#EF4444（红色）
   │     图标：\uE711（ErrorBadge）
   │     大小：20x20px
```

### 5. Info Events（信息事件）
```
   ●  <- 实心圆（蓝色）
   │     背景：#3B82F6（蓝色）
   │     图标：\uE946（StatusCircleBlock2）
   │     大小：20x20px
```

## 颜色规范

### Dark Theme
```
Timeline Line:       #404040  (深灰色，2px 宽)
Started Node:        #3B82F6  (蓝色边框)
Completed Node:      #22C55E  (绿色实心)
Info Node:           #3B82F6  (蓝色实心)
Warning Node:        #EAB308  (橙色实心)
Error Node:          #EF4444  (红色实心)
Card Background:     #2D2D2D  (默认透明)
Card Hover:          #353535  (浅灰背景)
```

### Light Theme
```
Timeline Line:       #E5E7EB  (浅灰色，2px 宽)
Started Node:        #3B82F6  (蓝色边框)
Completed Node:      #22C55E  (绿色实心)
Info Node:           #3B82F6  (蓝色实心)
Warning Node:        #F59E0B  (橙色实心)
Error Node:          #EF4444  (红色实心)
Card Background:     #FFFFFF  (默认透明)
Card Hover:          #F9FAFB  (浅灰背景)
```

## 间距规范

```
Timeline Column Width:    32px
Node Size:               20x20px (圆形)
Node Top Margin:         6px (对齐文字)
Card Padding:            12px (top/bottom) 8px (left/right)
Card Border Radius:      6px
Card Margin:             8px (left/right), 4px (bottom)
Timestamp Opacity:       0.7
Timestamp Font Size:     10px
```

## 文字层次

```
Level 1: Event Title
  - Font: SemiBold
  - Size: FontSizeS (约 13px)
  - Color: TextPrimaryBrush
  - Example: "Test Case Started"

Level 2: Event Message
  - Font: Regular
  - Size: FontSizeXS (约 11px)
  - Color: TextSecondaryBrush
  - Wrapping: Yes (MaxHeight 80px)
  - Example: "Executing: CPU_Stress_Test"

Level 3: Timestamp
  - Font: Cascadia Code / Consolas (等宽字体)
  - Size: 10px
  - Color: TextTertiaryBrush (70% opacity)
  - Format: HH:mm:ss.fff
  - Example: "14:20:05.789"
```

## 交互状态

### Default（默认）
```
Card Background: Transparent
Border: None
Cursor: Default
```

### Hover（悬停）
```
Card Background: TimelineCardHoverBrush
Border: None
Cursor: Default
Transition: Instant (no animation)
```

### Selected（未实现，预留）
```
Card Background: InteractiveSelectedBrush
Border: 1px solid PrimaryBrush
Cursor: Pointer
```

## 示例事件流

```
Timeline View:

14:20:00.123  ○  Test Suite Started
                 Starting test suite: Smoke Tests

14:20:01.456  ○  Test Case Started
                 Executing: CPU_Stress_Test

14:20:05.789  ●  Test Case Completed
                 PASS: CPU test passed all checks

14:20:06.012  ⚠  Test Case Warning
                 High temperature detected: 85°C

14:20:07.345  ○  Test Case Started
                 Executing: Memory_Leak_Test

14:20:10.678  ✕  Test Case Error
                 FAIL: Memory leak detected (12MB)

14:20:11.901  ●  Test Suite Completed
                 Completed: 2 passed, 1 failed, 1 warning
```

## Code → Title 映射示例

```
Code                          → Title
────────────────────────────────────────────────
TestSuite.Started            → Test Suite Started
TestSuite.Completed          → Test Suite Completed
TestCase.Started             → Test Case Started
TestCase.Completed           → Test Case Completed
TestCase.StepStarted         → Test Case Step Started
PowerShell.OutputReceived    → Power Shell Output Received
Execution.Failed             → Execution Failed
```

## 响应式调整（未来）

### 窄屏（< 800px）
- 时间戳移到卡片内右上角
- 时间线列保持 32px
- 卡片宽度自适应

### 宽屏（> 1200px）
- 时间线列可适当增加到 48px
- 节点大小增加到 24px
- 卡片 Padding 增加

## 可访问性

- ✅ 颜色不是唯一区分方式（图标 + 颜色）
- ✅ 高对比度模式支持（通过 DynamicResource）
- ✅ 键盘导航（ItemsControl 原生支持）
- ✅ 屏幕阅读器友好（文字内容完整）
- ✅ Tooltip 提供完整信息

## 性能优化

- ✅ 虚拟化滚动（VirtualizingPanel）
- ✅ 回收模式（Recycling）
- ✅ 最小化触发器（仅必要的 DataTrigger）
- ✅ 无动画（避免 CPU 消耗）
- ✅ 轻量级模板（无复杂嵌套）

---

**设计理念**：清晰 > 华丽，功能 > 装饰，性能 > 特效
