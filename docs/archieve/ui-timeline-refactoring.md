# Execution Timeline UI Refactoring

## 概述
将 Run Page 中的 "Structured Events" 从传统 DataGrid 重构为现代化的 **Execution Timeline / Event Feed** 风格。

## 设计目标
- ✅ 专业的执行时间线视觉效果（参考 Azure DevOps / GitHub Actions）
- ✅ 左侧时间线 + 中间事件卡片 + 右侧时间戳
- ✅ Started / Completed 事件的差异化视觉展示
- ✅ Level（info/warning/error）通过颜色和图标表达
- ✅ 扁平化、现代、大量留白的设计风格
- ✅ Hover 交互效果
- ✅ 纯 XAML 实现，无第三方库

## 实现细节

### 1. 新增 Converters（`Converters.cs`）

#### `EventCodeToTitleConverter`
- 将事件 Code（如 `TestCase.Started`）转换为友好标题（`Test Case Started`）
- 自动插入空格，增强可读性

#### `EventCodeIsStartedConverter`
- 判断事件是否为 "Started" 类型
- 用于差异化显示：Started = 空心圆

#### `EventCodeIsCompletedConverter`
- 判断事件是否为 "Completed" 类型
- 用于差异化显示：Completed = 实心圆（绿色）

#### `EventLevelToIconConverter`
- 将 Level 映射到 Segoe MDL2 Assets 图标
- error → `\uE711`（ErrorBadge）
- warning → `\uE7BA`（Warning）
- info → `\uE946`（StatusCircleBlock2）
- debug → `\uE8EC`（DeveloperTools）

### 2. 新增颜色资源

#### Dark Theme (`Colors.Dark.xaml`)
```xml
<Color x:Key="TimelineLineColor">#404040</Color>
<Color x:Key="TimelineNodeStartedColor">#3B82F6</Color>
<Color x:Key="TimelineNodeCompletedColor">#22C55E</Color>
<Color x:Key="TimelineNodeInfoColor">#3B82F6</Color>
<Color x:Key="TimelineNodeWarningColor">#EAB308</Color>
<Color x:Key="TimelineNodeErrorColor">#EF4444</Color>
<Color x:Key="TimelineCardBackgroundColor">#2D2D2D</Color>
<Color x:Key="TimelineCardHoverColor">#353535</Color>
```

#### Light Theme (`Colors.Light.xaml`)
```xml
<Color x:Key="TimelineLineColor">#E5E7EB</Color>
<Color x:Key="TimelineNodeStartedColor">#3B82F6</Color>
<Color x:Key="TimelineNodeCompletedColor">#22C55E</Color>
<Color x:Key="TimelineNodeInfoColor">#3B82F6</Color>
<Color x:Key="TimelineNodeWarningColor">#F59E0B</Color>
<Color x:Key="TimelineNodeErrorColor">#EF4444</Color>
<Color x:Key="TimelineCardBackgroundColor">#FFFFFF</Color>
<Color x:Key="TimelineCardHoverColor">#F9FAFB</Color>
```

### 3. UI 结构（`RunPage.xaml`）

#### 布局
```
┌─────────────────────────────────────────────┐
│ Execution Timeline                          │
├─────┬────────────────────────┬──────────────┤
│  │  │ Test Case Started      │  14:23:45.123│
│  ○  │ Running test case...   │              │
├─────┼────────────────────────┼──────────────┤
│  │  │ Test Case Completed    │  14:23:47.456│
│  ●  │ Test passed            │              │
└─────┴────────────────────────┴──────────────┘
```

#### 关键特性
1. **时间线列（32px）**
   - 垂直连续线（2px，灰色）
   - 圆形节点（20px）
   - Started：空心圆（蓝色边框）
   - Completed：实心圆（绿色）
   - Error：实心圆（红色）
   - Warning：实心圆（橙色）

2. **内容列（弹性宽度）**
   - 透明背景，Hover 时显示浅色背景
   - 圆角 6px，无边框（简洁）
   - 第一行：事件标题（由 Code 派生，SemiBold）
   - 第二行：Message（可换行，最大高度 80px，超出显示省略号）

3. **时间戳列（自适应宽度）**
   - 等宽字体（Cascadia Code / Consolas）
   - 弱化显示（70% 透明度）
   - 小字号（10px）
   - 格式：`HH:mm:ss.fff`

#### DataTrigger 逻辑
```xaml
<!-- Started 事件：空心圆 -->
<DataTrigger Binding="{Binding Code, Converter={StaticResource EventCodeIsStartedConverter}}" Value="True">
    <Setter Property="Background" Value="Transparent"/>
    <Setter Property="BorderBrush" Value="{DynamicResource TimelineNodeStartedBrush}"/>
    <Setter Property="BorderThickness" Value="2"/>
</DataTrigger>

<!-- Completed 事件：实心绿圆 -->
<DataTrigger Binding="{Binding Code, Converter={StaticResource EventCodeIsCompletedConverter}}" Value="True">
    <Setter Property="Background" Value="{DynamicResource TimelineNodeCompletedBrush}"/>
</DataTrigger>

<!-- Error：实心红圆 -->
<DataTrigger Binding="{Binding Level}" Value="error">
    <Setter Property="Background" Value="{DynamicResource TimelineNodeErrorBrush}"/>
</DataTrigger>
```

## 设计原则遵循

### ✅ 不改数据结构
- 仍然使用 `ObservableCollection<StructuredEventViewModel>`
- 仍然包含：`Timestamp` / `Level` / `Code` / `Message`
- 不引入新字段，不改后端

### ✅ 不改 ViewModel
- 无需修改 `RunViewModel.cs`
- 无需修改 `StructuredEventViewModel`
- 完全通过 XAML 和 Converters 实现

### ✅ 视觉现代化
- 扁平化设计（无粗边框、无表格线）
- 圆角 6px（卡片）、10px（节点）
- 微弱 Hover 效果（不过度）
- 克制的颜色使用

### ✅ 语义清晰
- Started → 空心圆（开始）
- Completed → 实心圆（完成）
- Error/Warning → 颜色强调
- 时间线连续性强（适合执行流程可视化）

## 交互细节

### Hover 效果
- 事件卡片：`Transparent` → `TimelineCardHoverBrush`
- 无阴影、无动画（克制）
- 视觉反馈明确但不喧宾夺主

### 长 Message 处理
- `TextWrapping="Wrap"`（支持多行）
- `MaxHeight="80"`（最多显示约 4-5 行）
- `TextTrimming="CharacterEllipsis"`（超出显示省略号）
- `ToolTip="{Binding Message}"`（完整内容通过 Tooltip 查看）

### 虚拟化
- `VirtualizingPanel.IsVirtualizing="True"`
- `VirtualizingPanel.VirtualizationMode="Recycling"`
- 大量事件时性能优化

## 可扩展性

### 未来可选增强（不在本次实现）
1. **Level 过滤**
   - 顶部添加 ToggleButton 组：All / Info / Warning / Error
   - 通过 CollectionViewSource 过滤

2. **事件展开**
   - 长 Message 默认折叠（2 行）
   - 点击展开完整内容（使用 Expander）

3. **Code 类型图标**
   - TestSuite.Started → Suite 图标
   - TestCase.Started → Case 图标
   - 增强视觉区分

4. **时间线分组**
   - 按 TestSuite / TestCase 分段
   - 段落之间增加视觉分隔

## 文件清单

### 修改的文件
1. **`src/PcTest.Ui/Resources/Converters.cs`**
   - 新增 4 个 Converters

2. **`src/PcTest.Ui/Views/Pages/RunPage.xaml`**
   - 替换 DataGrid 为 ItemsControl + Timeline 模板
   - 新增 `xmlns:res` 命名空间

3. **`src/PcTest.Ui/Themes/Dark/Colors.Dark.xaml`**
   - 新增 8 个 Timeline 颜色定义
   - 新增 8 个 Timeline Brush 资源

4. **`src/PcTest.Ui/Themes/Light/Colors.Light.xaml`**
   - 新增 8 个 Timeline 颜色定义（Light 主题）
   - 新增 8 个 Timeline Brush 资源

## 测试建议

1. **视觉测试**
   - 运行测试，观察 Execution Timeline 显示
   - 验证 Started / Completed / Error / Warning 事件的视觉差异
   - 测试 Dark / Light 主题切换

2. **交互测试**
   - Hover 事件卡片，观察背景变化
   - 长 Message 的 Tooltip 显示
   - 滚动性能（大量事件时）

3. **边界情况**
   - 无事件时（空列表）
   - 超长 Message（多行换行）
   - 快速事件流（实时更新）

## 视觉效果说明

### Before（DataGrid）
```
┌──────────┬────────┬──────────────────┬─────────────────┐
│ Timestamp│ Level  │ Code             │ Message         │
├──────────┼────────┼──────────────────┼─────────────────┤
│14:23:45  │ info   │ TestCase.Started │ Running test... │
│14:23:47  │ info   │TestCase.Completed│ Test passed     │
└──────────┴────────┴──────────────────┴─────────────────┘
```
- 表格式，有列头
- 信息密集，缺乏视觉层次
- Level 显示为文字

### After（Timeline）
```
Execution Timeline
─────────────────────────────────────────────────
  │    Test Case Started               14:23:45.123
  ○    Running test case...
  │
  │    Test Case Completed             14:23:47.456
  ●    Test passed
```
- 时间线视觉（垂直线 + 节点）
- Started = 空心圆，Completed = 实心圆
- 事件标题 + 详细信息（两行结构）
- 时间戳弱化显示（右侧）
- Hover 高亮整条事件

## 结论

✅ **纯 XAML 实现**，无第三方库  
✅ **不改数据结构和 ViewModel**  
✅ **现代化专业风格**（Azure DevOps / GitHub Actions）  
✅ **MVVM 友好**，可维护性强  
✅ **主题适配**（Dark / Light）  
✅ **性能优化**（虚拟化）  

这是一次**纯 UI 层重构**，通过 XAML 的强大表现力和少量 Converters，将普通表格转换为专业的执行时间线视图，大幅提升用户体验。
