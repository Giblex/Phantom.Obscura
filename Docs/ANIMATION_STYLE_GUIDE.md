# PhantomVault Animation Style Guide

**Version**: 1.0
**Date**: December 27, 2025
**Status**: Production Ready ✅

---

## Philosophy

PhantomVault animations are designed to be:
- **Smooth & Efficient** - 60fps performance with optimized easing
- **Accessible** - Full ReduceMotion support built-in
- **Modern** - Contemporary timing and physics-based motion
- **Purposeful** - Every animation serves a clear UX function

---

## Animation System Architecture

### 1. Accessibility Layer
- **AccessibilityService** - Global singleton managing ReduceMotion state
- **Automatic Detection** - Reads OS-level preferences (Windows Registry)
- **Runtime Toggle** - Users can override in Accessibility Settings
- **Event System** - Components subscribe to SettingsChanged event

### 2. Animation Components
- **LoadingSpinner** - Dual-mode (rotating arc / pulsing dot)
- **SmoothScrollBehavior** - Scroll timing controller
- **ModernAnimations.axaml** - Reusable animation library

### 3. Integration Points
- **LiquidGlassButton** - Physics-based animations with ReduceMotion check
- **Global Transitions** - All controls have smooth state changes
- **Window/Overlay Fades** - Consistent entrance/exit animations

---

## Animation Durations

### Standard Timing (Normal Mode)
```
Instant:   80ms   - Immediate feedback (hover, press)
Fast:      150ms  - Quick transitions (focus, toggle)
Normal:    250ms  - Standard animations (fade, slide)
Slow:      400ms  - Deliberate motions (panel open, modal)
Very Slow: 600ms  - Complex sequences (multi-stage)
```

### Reduced Motion Timing (50% Faster)
```
Instant:   40ms   - Minimal delay
Fast:      80ms   - Snappy response
Normal:    120ms  - Quick but smooth
Slow:      200ms  - Brief but visible
Very Slow: 300ms  - Shortened sequences
```

**Implementation**:
```xml
<x:Double x:Key="Anim.Duration.Normal">0.25</x:Double>
<x:Double x:Key="Anim.Duration.Normal.Reduced">0.12</x:Double>
```

---

## Easing Functions

### Recommended Easings by Use Case

**Entrances** (objects appearing):
- `CubicEaseOut` - General purpose, smooth deceleration
- `BackEaseOut` - Playful bounce (use sparingly)
- `SineEaseOut` - Gentle, natural motion

**Exits** (objects leaving):
- `CubicEaseIn` - Accelerate out of view
- `SineEaseIn` - Fade away naturally

**Bidirectional** (state changes):
- `CubicEaseInOut` - Balanced acceleration/deceleration
- `SineEaseInOut` - Gentle oscillation (toggles, switches)

**Continuous** (loading, progress):
- `LinearEasing` - Constant speed (spinners, progress bars)

**Special Effects**:
- `ElasticEaseOut` - Spring physics (liquid glass sheen)
- `BounceEaseOut` - Playful bouncing (success confirmations)

### ReduceMotion Override
When ReduceMotion is active:
- Replace all easings with `LinearEasing`
- Disable elastic/bounce effects
- Shorten durations by 50%

---

## Animation Classes

### Fade Animations

#### Fade In
```xml
<Border Classes="fade-in">
    <!-- Content appears smoothly (300ms, SineEaseOut) -->
</Border>
```

#### Fade Out
```xml
<Border Classes="fade-out">
    <!-- Content disappears (200ms, SineEaseIn) -->
</Border>
```

---

### Slide Animations

#### Slide In from Bottom
```xml
<Border Classes="slide-in-bottom">
    <!-- Slides up 20px while fading in (400ms, CubicEaseOut) -->
</Border>
```

#### Slide In from Top
```xml
<Border Classes="slide-in-top">
    <!-- Slides down 20px while fading in (400ms, CubicEaseOut) -->
</Border>
```

#### Slide In from Left
```xml
<Border Classes="slide-in-left">
    <!-- Slides right 20px while fading in (350ms, CubicEaseOut) -->
</Border>
```

#### Slide In from Right
```xml
<Border Classes="slide-in-right">
    <!-- Slides left 20px while fading in (350ms, CubicEaseOut) -->
</Border>
```

---

### Scale Animations

#### Scale Up (Gentle)
```xml
<Border Classes="scale-up">
    <!-- Scales from 95% to 100% with fade (300ms, CubicEaseOut) -->
    <!-- Ideal for: Cards, modals, tooltips -->
</Border>
```

#### Pop In (Bouncy)
```xml
<Border Classes="pop-in">
    <!-- Scales from 80% to 100% with bounce (500ms, BackEaseOut) -->
    <!-- Ideal for: Notifications, success messages, achievements -->
</Border>
```

---

### Staggered Lists

Create cascading entrance animations for list items:

```xml
<StackPanel>
    <Border Classes="stagger-1">Item 1</Border>  <!-- Delay: 50ms -->
    <Border Classes="stagger-2">Item 2</Border>  <!-- Delay: 100ms -->
    <Border Classes="stagger-3">Item 3</Border>  <!-- Delay: 150ms -->
</StackPanel>
```

**Effect**: Items appear sequentially with 50ms between each.

---

### Hover Effects

#### Hoverable (Lift on Hover)
```xml
<Border Classes="hoverable">
    <!-- Lifts 2px up on hover (200ms, CubicEaseOut) -->
    <!-- Ideal for: Cards, buttons, clickable items -->
</Border>
```

#### Scalable (Grow on Hover)
```xml
<Border Classes="scalable">
    <!-- Scales to 102% on hover (200ms, CubicEaseOut) -->
    <!-- Ideal for: Icons, images, interactive elements -->
</Border>
```

---

## Component Usage

### LoadingSpinner

Modern loading indicator with automatic ReduceMotion support.

```xml
<controls:LoadingSpinner />
```

**Behavior**:
- **Normal Mode**: Smooth rotating arc (1.2s rotation)
- **ReduceMotion Mode**: Gentle pulsing dot (1.5s pulse)

**Auto-switches** based on AccessibilityService.Instance.ReduceMotion

---

### Window Entrance

Add smooth fade-in to windows:

```xml
<Window Classes="animated-window">
    <!-- Window fades in over 350ms with custom bezier easing -->
</Window>
```

---

### Overlay Fade

For modal overlays and backgrounds:

```xml
<Border Classes="overlay-fade" IsVisible="{Binding ShowOverlay}">
    <!-- Fades in 200ms when IsVisible becomes True -->
</Border>
```

---

### Quick Fade Toggle

For lightweight controls that need simple visibility animation:

```xml
<Control Classes="fade-toggle" IsVisible="{Binding ShowControl}">
    <!-- Fast 160ms fade when toggled -->
</Control>
```

---

## Liquid Glass Physics

The signature liquid glass effect uses advanced spring-damper physics.

### How It Works

1. **Spring System** (60fps update loop):
   - Damping coefficient: 0.78
   - Max displacement: ±24px (sheen)
   - Elastic easing: EaseOutElastic

2. **Three Animation Layers**:
   - **Sheen Inertia**: 24px overshoot with wobble
   - **Scale Animation**: 97% → 103% on press
   - **Micro-wobble**: 0.8% scale oscillation

3. **ReduceMotion Override**:
   ```csharp
   private static bool ShouldReduceMotion()
   {
       return AccessibilityService.Instance.ReduceMotion;
   }
   ```
   - When true: Disables physics, uses instant transitions

### Usage

```xml
<Button Classes="liquid-glass" Content="Click Me"/>
```

**States**:
- Normal: Glass gradient with subtle sheen
- Hover: Sheen follows mouse (spring physics)
- Press: Scale down with wobble
- Release: Spring back with overshoot

---

## Performance Guidelines

### DO ✅
- Use `CubicEaseOut` for most animations (hardware accelerated)
- Animate `Opacity`, `TranslateTransform` (cheap properties)
- Keep animations under 600ms
- Test with ReduceMotion enabled
- Use `AllowAutoHide` on ScrollViewers

### DON'T ❌
- Animate `Width`/`Height` directly (causes layout thrashing)
- Chain more than 3 animations sequentially
- Use `ElasticEaseOut` on large elements (performance cost)
- Forget to test on low-end hardware
- Ignore accessibility settings

---

## Accessibility Compliance

### WCAG 2.1 Success Criterion 2.3.3
**Animation from Interactions** - Level AAA

✅ **Compliant**: All animations respect ReduceMotion setting

**Implementation**:
1. Global `AccessibilityService.Instance.ReduceMotion` property
2. OS-level detection (Windows Registry, macOS defaults, Linux gsettings)
3. User override in Accessibility Settings
4. All animated components subscribe to SettingsChanged event

**Reduced Motion Behavior**:
- Durations cut by 50%
- Easing changed to LinearEasing
- Physics/bounce disabled
- Focus shifts to instant feedback

---

## Migration Guide

### Converting Hardcoded Animations

**Before**:
```xml
<Style.Animations>
    <Animation Duration="0:0:0.3">
        <KeyFrame Cue="0%">
            <Setter Property="Opacity" Value="0"/>
        </KeyFrame>
        <KeyFrame Cue="100%">
            <Setter Property="Opacity" Value="1"/>
        </KeyFrame>
    </Animation>
</Style.Animations>
```

**After**:
```xml
<!-- Use built-in animation class -->
<Border Classes="fade-in">
    <!-- Content -->
</Border>

<!-- Or use duration resource -->
<Animation Duration="{StaticResource Anim.Duration.Normal}">
    ...
</Animation>
```

---

## Examples

### Card Entrance (Staggered Grid)

```xml
<Grid>
    <Border Classes="scale-up stagger-1">Card 1</Border>
    <Border Classes="scale-up stagger-2">Card 2</Border>
    <Border Classes="scale-up stagger-3">Card 3</Border>
</Grid>
```

**Result**: Cards appear sequentially with gentle scale-up

---

### Modal Dialog

```xml
<!-- Overlay backdrop -->
<Border Classes="overlay-fade" IsVisible="{Binding ShowModal}">
    <!-- Dialog content -->
    <Border Classes="scale-up">
        <TextBlock Text="Confirm action?"/>
    </Border>
</Border>
```

**Result**: Backdrop fades in, dialog scales up from 95%

---

### Success Notification

```xml
<Border Classes="pop-in slide-in-bottom">
    <TextBlock Text="✓ Saved successfully!"/>
</Border>
```

**Result**: Pops in with bounce + slides up from bottom

---

### List Items

```xml
<ItemsControl>
    <ItemsControl.ItemTemplate>
        <DataTemplate>
            <Border Classes="slide-in-left hoverable">
                <!-- List item content -->
            </Border>
        </DataTemplate>
    </ItemsControl.ItemTemplate>
</ItemsControl>
```

**Result**: Items slide in from left, lift on hover

---

## Testing Checklist

### Before Committing Animations

- [ ] Tested with ReduceMotion enabled
- [ ] Tested on 60Hz and 144Hz monitors
- [ ] Verified performance (no frame drops)
- [ ] Checked on low-end hardware (if available)
- [ ] Ensured accessibility compliance (WCAG 2.3.3)
- [ ] Verified visual smoothness at all easing curves
- [ ] Tested entrance/exit timing feels natural
- [ ] Confirmed no animation conflicts (multiple triggers)

---

## Future Enhancements

### Planned Features
1. **Gesture Animations** - Touch/swipe response
2. **Page Transitions** - View navigation effects
3. **Skeleton Loaders** - Content loading placeholders
4. **Micro-interactions** - Button press feedback, ripple effects
5. **Parallax Scrolling** - Depth-based motion (respect ReduceMotion)

### Under Consideration
- Spring physics library integration
- Custom timeline editor
- Animation performance profiler
- Visual animation preview tool

---

## Support

For questions or issues with animations:
1. Check this guide first
2. Review `ModernAnimations.axaml` source
3. Test with ReduceMotion enabled
4. Verify easing curves in UI_UX_IMPROVEMENTS.md

**Remember**: Every animation should enhance usability, not hinder it. When in doubt, test with real users—especially those using accessibility features.

---

**Last Updated**: December 27, 2025
**Maintained By**: PhantomVault UI/UX Team
**Version**: 1.0.0
