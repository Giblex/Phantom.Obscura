# Animation Quick Reference Card

**Avalonia.Xaml.Interactivity Behaviors** | PhantomVault v6

---

## Setup XAML Namespaces

```xml
xmlns:i="clr-namespace:Avalonia.Xaml.Interactivity;assembly=Avalonia.Xaml.Interactivity"
xmlns:behaviors="clr-namespace:PhantomVault.UI.Behaviors"
```

---

## 1. FadeInBehavior

**Fade in a control**

```xml
<Border>
    <i:Interaction.Behaviors>
        <behaviors:FadeInBehavior Duration="0.3" Delay="0" />
    </i:Interaction.Behaviors>
</Border>
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| Duration | double | 0.3 | Animation duration (seconds) |
| Delay | double | 0.0 | Delay before start (seconds) |

---

## 2. SlideInBehavior

**Slide in from direction**

```xml
<Border>
    <i:Interaction.Behaviors>
        <behaviors:SlideInBehavior Direction="Bottom" Distance="30" />
    </i:Interaction.Behaviors>
</Border>
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| Direction | SlideDirection | Bottom | Left, Right, Top, Bottom |
| Distance | double | 20 | Distance in pixels |
| Duration | double | 0.4 | Animation duration (seconds) |
| Delay | double | 0.0 | Delay before start (seconds) |

---

## 3. ScaleInBehavior

**Scale from smaller size**

```xml
<!-- Gentle -->
<Border>
    <i:Interaction.Behaviors>
        <behaviors:ScaleInBehavior FromScale="0.95" />
    </i:Interaction.Behaviors>
</Border>

<!-- Bouncy -->
<Border>
    <i:Interaction.Behaviors>
        <behaviors:ScaleInBehavior FromScale="0.8" UseBounce="True" />
    </i:Interaction.Behaviors>
</Border>
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| FromScale | double | 0.95 | Starting scale (0.0 to 1.0) |
| UseBounce | bool | false | Use bounce effect (BackEaseOut) |
| Duration | double | 0.3 | Animation duration (seconds) |
| Delay | double | 0.0 | Delay before start (seconds) |

---

## 4. HoverLiftBehavior

**Lift upward on hover**

```xml
<Border>
    <i:Interaction.Behaviors>
        <behaviors:HoverLiftBehavior LiftDistance="-4" />
    </i:Interaction.Behaviors>
</Border>
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| LiftDistance | double | -2 | Lift distance in px (negative = up) |
| Duration | double | 0.2 | Animation duration (seconds) |

---

## 5. HoverScaleBehavior

**Scale up on hover**

```xml
<Button>
    <i:Interaction.Behaviors>
        <behaviors:HoverScaleBehavior HoverScale="1.05" />
    </i:Interaction.Behaviors>
</Button>
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| HoverScale | double | 1.02 | Scale factor on hover (1.0 = 100%) |
| Duration | double | 0.2 | Animation duration (seconds) |

---

## Common Recipes

### Staggered List (3 items)
```xml
<StackPanel>
    <Border><i:Interaction.Behaviors>
        <behaviors:SlideInBehavior Direction="Left" Delay="0.0" />
    </i:Interaction.Behaviors></Border>

    <Border><i:Interaction.Behaviors>
        <behaviors:SlideInBehavior Direction="Left" Delay="0.05" />
    </i:Interaction.Behaviors></Border>

    <Border><i:Interaction.Behaviors>
        <behaviors:SlideInBehavior Direction="Left" Delay="0.1" />
    </i:Interaction.Behaviors></Border>
</StackPanel>
```

### Modal Dialog
```xml
<!-- Backdrop -->
<Border Background="#CC000000">
    <i:Interaction.Behaviors>
        <behaviors:FadeInBehavior Duration="0.2" />
    </i:Interaction.Behaviors>

    <!-- Content -->
    <Border>
        <i:Interaction.Behaviors>
            <behaviors:ScaleInBehavior FromScale="0.9" Delay="0.1" />
        </i:Interaction.Behaviors>
    </Border>
</Border>
```

### Interactive Card
```xml
<Border>
    <i:Interaction.Behaviors>
        <behaviors:SlideInBehavior Direction="Bottom" />
        <behaviors:HoverLiftBehavior LiftDistance="-6" />
        <behaviors:HoverScaleBehavior HoverScale="1.02" />
    </i:Interaction.Behaviors>
</Border>
```

### Success Toast
```xml
<Border Background="#10B981">
    <i:Interaction.Behaviors>
        <behaviors:ScaleInBehavior FromScale="0.8" UseBounce="True" />
        <behaviors:SlideInBehavior Direction="Bottom" Distance="40" />
    </i:Interaction.Behaviors>
</Border>
```

---

## ReduceMotion Support

All behaviors automatically adjust when `AccessibilityService.Instance.ReduceMotion` is `true`:

| Behavior | Normal | ReduceMotion |
|----------|--------|--------------|
| FadeIn | 300ms SineEaseOut | 100ms Linear |
| SlideIn | 400ms CubicEaseOut | 200ms Linear |
| ScaleIn | 300ms CubicEaseOut | 120ms Linear |
| ScaleIn+Bounce | 500ms BackEaseOut | 120ms Linear (no bounce) |
| HoverLift | Smooth | Instant |
| HoverScale | Smooth | Instant |

---

## Files Reference

| File | Purpose |
|------|---------|
| `BEHAVIOR_ANIMATIONS_GUIDE.md` | Full documentation |
| `BEHAVIOR_ANIMATIONS_SUMMARY.md` | Implementation summary |
| `ANIMATION_QUICK_REFERENCE.md` | This card |
| `src/UI.Desktop/Examples/BehaviorAnimationExample.axaml` | Working examples |

---

**Package**: Avalonia.Xaml.Interactivity 11.3.0.6
**WCAG**: 2.3.3 AAA Compliant ✅
**Build**: 0 Errors ✅
