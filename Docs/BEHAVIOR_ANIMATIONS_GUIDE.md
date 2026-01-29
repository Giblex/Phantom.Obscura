# Behavior-Based Animation Guide

**Version**: 2.0
**Date**: December 27, 2025
**Package**: Avalonia.Xaml.Interactivity 11.3.0.6

---

## Overview

PhantomVault now uses **Avalonia.Xaml.Interactivity** for robust, reusable animation behaviors. All behaviors automatically respect ReduceMotion accessibility settings.

---

## Available Behaviors

### 1. FadeInBehavior
Fades in a control when it becomes visible.

**Properties**:
- `Duration` (double) - Animation duration in seconds (default: 0.3)
- `Delay` (double) - Delay before animation starts (default: 0.0)

**Usage**:
```xml
<Border>
    <i:Interaction.Behaviors>
        <behaviors:FadeInBehavior Duration="0.3" Delay="0.0" />
    </i:Interaction.Behaviors>
    <TextBlock Text="I fade in smoothly!" />
</Border>
```

---

### 2. SlideInBehavior
Slides in a control from a specified direction with fade.

**Properties**:
- `Direction` (SlideDirection) - Direction to slide from (Left, Right, Top, Bottom)
- `Distance` (double) - Distance to slide in pixels (default: 20)
- `Duration` (double) - Animation duration in seconds (default: 0.4)
- `Delay` (double) - Delay before animation starts (default: 0.0)

**Usage**:
```xml
<!-- Slide from bottom -->
<Border>
    <i:Interaction.Behaviors>
        <behaviors:SlideInBehavior Direction="Bottom" Distance="20" />
    </i:Interaction.Behaviors>
    <TextBlock Text="Sliding up from bottom!" />
</Border>

<!-- Slide from left -->
<Border>
    <i:Interaction.Behaviors>
        <behaviors:SlideInBehavior Direction="Left" Distance="30" Delay="0.1" />
    </i:Interaction.Behaviors>
    <TextBlock Text="Sliding in from left!" />
</Border>
```

---

### 3. ScaleInBehavior
Scales in a control from a smaller size with optional bounce.

**Properties**:
- `FromScale` (double) - Starting scale (default: 0.95)
- `UseBounce` (bool) - Use bounce effect (BackEaseOut) (default: false)
- `Duration` (double) - Animation duration in seconds (default: 0.3)
- `Delay` (double) - Delay before animation starts (default: 0.0)

**Usage**:
```xml
<!-- Gentle scale-up -->
<Border>
    <i:Interaction.Behaviors>
        <behaviors:ScaleInBehavior FromScale="0.95" />
    </i:Interaction.Behaviors>
    <TextBlock Text="Gentle entrance" />
</Border>

<!-- Pop-in with bounce -->
<Border>
    <i:Interaction.Behaviors>
        <behaviors:ScaleInBehavior FromScale="0.8" UseBounce="True" />
    </i:Interaction.Behaviors>
    <TextBlock Text="Bouncy pop-in!" />
</Border>
```

---

### 4. HoverLiftBehavior
Lifts a control upward on hover (great for cards).

**Properties**:
- `LiftDistance` (double) - Distance to lift in pixels (negative = up, default: -2)
- `Duration` (double) - Animation duration in seconds (default: 0.2)

**Usage**:
```xml
<Border Background="#2C2C2C" CornerRadius="8">
    <i:Interaction.Behaviors>
        <behaviors:HoverLiftBehavior LiftDistance="-4" />
    </i:Interaction.Behaviors>
    <StackPanel Padding="16">
        <TextBlock Text="Hover me!" />
    </StackPanel>
</Border>
```

---

### 5. HoverScaleBehavior
Scales up a control slightly on hover (great for buttons/icons).

**Properties**:
- `HoverScale` (double) - Scale factor on hover (default: 1.02 = 102%)
- `Duration` (double) - Animation duration in seconds (default: 0.2)

**Usage**:
```xml
<Button Content="Click Me">
    <i:Interaction.Behaviors>
        <behaviors:HoverScaleBehavior HoverScale="1.05" />
    </i:Interaction.Behaviors>
</Button>

<!-- Icon hover effect -->
<Path Data="M12,2A10,10..." Fill="White">
    <i:Interaction.Behaviors>
        <behaviors:HoverScaleBehavior HoverScale="1.1" Duration="0.15" />
    </i:Interaction.Behaviors>
</Path>
```

---

## Combining Behaviors

You can attach multiple behaviors to a single control:

```xml
<Border CornerRadius="12" Background="#1E1E1E">
    <i:Interaction.Behaviors>
        <!-- Slide in from bottom on load -->
        <behaviors:SlideInBehavior Direction="Bottom" Distance="30" />

        <!-- Lift on hover -->
        <behaviors:HoverLiftBehavior LiftDistance="-6" />
    </i:Interaction.Behaviors>

    <StackPanel Padding="24">
        <TextBlock Text="Feature Card" FontSize="18" />
        <TextBlock Text="Slides in, then lifts on hover" Opacity="0.7" />
    </StackPanel>
</Border>
```

---

## Staggered List Animations

Create cascading entrance effects using `Delay`:

```xml
<StackPanel Spacing="12">
    <Border Classes="card">
        <i:Interaction.Behaviors>
            <behaviors:SlideInBehavior Direction="Left" Delay="0.0" />
        </i:Interaction.Behaviors>
        <TextBlock Text="Item 1" />
    </Border>

    <Border Classes="card">
        <i:Interaction.Behaviors>
            <behaviors:SlideInBehavior Direction="Left" Delay="0.05" />
        </i:Interaction.Behaviors>
        <TextBlock Text="Item 2" />
    </Border>

    <Border Classes="card">
        <i:Interaction.Behaviors>
            <behaviors:SlideInBehavior Direction="Left" Delay="0.1" />
        </i:Interaction.Behaviors>
        <TextBlock Text="Item 3" />
    </Border>
</StackPanel>
```

---

## XAML Namespace Setup

Add these namespaces to your XAML file:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:i="clr-namespace:Avalonia.Xaml.Interactivity;assembly=Avalonia.Xaml.Interactivity"
             xmlns:behaviors="clr-namespace:PhantomVault.UI.Behaviors">

    <!-- Your content here -->

</UserControl>
```

**Namespace Breakdown**:
- `xmlns:i` - Avalonia.Xaml.Interactivity (for `Interaction.Behaviors`)
- `xmlns:behaviors` - PhantomVault.UI.Behaviors (for our custom behaviors)

---

## Real-World Examples

### Modal Dialog
```xml
<Border Background="#CC000000" IsVisible="{Binding ShowModal}">
    <!-- Backdrop fade -->
    <i:Interaction.Behaviors>
        <behaviors:FadeInBehavior Duration="0.2" />
    </i:Interaction.Behaviors>

    <!-- Dialog content -->
    <Border Background="#2C2C2C" CornerRadius="12" Padding="32">
        <i:Interaction.Behaviors>
            <behaviors:ScaleInBehavior FromScale="0.9" Delay="0.1" />
        </i:Interaction.Behaviors>

        <StackPanel Spacing="16">
            <TextBlock Text="Confirm Action" FontSize="20" />
            <TextBlock Text="Are you sure?" Opacity="0.8" />
            <Button Content="Confirm" />
        </StackPanel>
    </Border>
</Border>
```

### Success Notification
```xml
<Border CornerRadius="8" Background="#10B981" Padding="16">
    <i:Interaction.Behaviors>
        <!-- Pop in with bounce from bottom -->
        <behaviors:ScaleInBehavior FromScale="0.8" UseBounce="True" />
        <behaviors:SlideInBehavior Direction="Bottom" Distance="40" />
    </i:Interaction.Behaviors>

    <StackPanel Orientation="Horizontal" Spacing="12">
        <Path Data="M9,16.2L4.8,12..." Fill="White" />
        <TextBlock Text="Saved successfully!" FontWeight="SemiBold" />
    </StackPanel>
</Border>
```

### Card Grid with Hover
```xml
<Grid ColumnDefinitions="*,*,*" RowDefinitions="*,*" Margin="24">
    <!-- Card 1 -->
    <Border Grid.Row="0" Grid.Column="0" Classes="feature-card">
        <i:Interaction.Behaviors>
            <behaviors:ScaleInBehavior Delay="0.0" />
            <behaviors:HoverLiftBehavior LiftDistance="-4" />
            <behaviors:HoverScaleBehavior HoverScale="1.02" />
        </i:Interaction.Behaviors>
        <TextBlock Text="Feature 1" />
    </Border>

    <!-- Card 2 -->
    <Border Grid.Row="0" Grid.Column="1" Classes="feature-card">
        <i:Interaction.Behaviors>
            <behaviors:ScaleInBehavior Delay="0.05" />
            <behaviors:HoverLiftBehavior LiftDistance="-4" />
            <behaviors:HoverScaleBehavior HoverScale="1.02" />
        </i:Interaction.Behaviors>
        <TextBlock Text="Feature 2" />
    </Border>

    <!-- Card 3 -->
    <Border Grid.Row="0" Grid.Column="2" Classes="feature-card">
        <i:Interaction.Behaviors>
            <behaviors:ScaleInBehavior Delay="0.1" />
            <behaviors:HoverLiftBehavior LiftDistance="-4" />
            <behaviors:HoverScaleBehavior HoverScale="1.02" />
        </i:Interaction.Behaviors>
        <TextBlock Text="Feature 3" />
    </Border>
</Grid>
```

---

## Accessibility Compliance

**All behaviors automatically respect ReduceMotion settings:**

| Setting | Normal Mode | ReduceMotion Mode |
|---------|-------------|-------------------|
| **FadeIn** | 300ms SineEaseOut | 100ms LinearEasing |
| **SlideIn** | 400ms CubicEaseOut | 200ms LinearEasing |
| **ScaleIn** | 300ms CubicEaseOut | 120ms LinearEasing |
| **ScaleIn (Bounce)** | 500ms BackEaseOut | 120ms LinearEasing (no bounce) |
| **HoverLift** | Smooth 200ms | Instant transition |
| **HoverScale** | Smooth 200ms | Instant transition |

**WCAG 2.1 Success Criterion 2.3.3 (AAA)**: ✅ **FULLY COMPLIANT**

---

## Performance Notes

### DO ✅
- Use behaviors on individual controls (not entire grids)
- Combine entrance behaviors with hover behaviors
- Use staggered delays for list animations
- Test with ReduceMotion enabled

### DON'T ❌
- Attach behaviors to thousands of list items (use virtualization)
- Chain more than 3 behaviors on a single control
- Use bounce effects on large elements (>500px)
- Forget to test hover behaviors on touch devices

---

## Migration from CSS-Style Classes

### Before (ModernAnimations.axaml classes):
```xml
<Border Classes="fade-in slide-in-bottom hoverable">
    <TextBlock Text="Content" />
</Border>
```

### After (Interactivity behaviors):
```xml
<Border>
    <i:Interaction.Behaviors>
        <behaviors:FadeInBehavior />
        <behaviors:SlideInBehavior Direction="Bottom" />
        <behaviors:HoverLiftBehavior />
    </i:Interaction.Behaviors>
    <TextBlock Text="Content" />
</Border>
```

**Advantages of Behaviors**:
- More explicit and discoverable
- Intellisense support for properties
- Easier to customize per-instance
- Better performance (no style selector overhead)
- Type-safe in C# code-behind

---

## Troubleshooting

### Animations not working?
1. **Check namespace imports** - Ensure `xmlns:i` and `xmlns:behaviors` are defined
2. **Verify package installed** - `Avalonia.Xaml.Interactivity` 11.3.0.6+
3. **Check control visibility** - Behaviors trigger on `AttachedToVisualTree`
4. **Test ReduceMotion** - Hover effects become instant, entrance delays are halved

### Choppy animations?
1. **Use hardware-accelerated properties** - Opacity, TranslateTransform, ScaleTransform
2. **Avoid animating** - Width, Height, Margin (causes layout thrashing)
3. **Reduce concurrent animations** - Stagger instead of running all at once

---

## Advanced: Custom Behaviors

Create your own behaviors:

```csharp
using Avalonia.Controls;
using Avalonia.Xaml.Interactivity;

namespace PhantomVault.UI.Behaviors
{
    public class MyCustomBehavior : Behavior<Control>
    {
        protected override void OnAttached()
        {
            base.OnAttached();
            // Setup logic when behavior is attached
        }

        protected override void OnDetaching()
        {
            base.OnDetaching();
            // Cleanup logic when behavior is removed
        }
    }
}
```

---

## Summary

**Behavior-based animations provide**:
- ✅ Type-safe, reusable animation components
- ✅ Automatic ReduceMotion support
- ✅ Intellisense and design-time support
- ✅ Better performance than CSS-style selectors
- ✅ Easy to test and maintain
- ✅ WCAG 2.3.3 AAA compliance

**Use behaviors for**:
- Card entrances and hover effects
- Modal dialogs and notifications
- Staggered list animations
- Button and icon interactions

---

**Last Updated**: December 27, 2025
**Package Version**: Avalonia.Xaml.Interactivity 11.3.0.6
**Maintained By**: PhantomVault UI/UX Team
