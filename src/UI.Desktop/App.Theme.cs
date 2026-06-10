using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace PhantomVault.UI
{
    // Theme-orchestration partial for App. Behaviour-identical to the original SetTheme
    // block in App.axaml.cs; extracted so the lifecycle/composition files stay readable.
    // Keeps full access to the App's _currentTheme, _themeAwareWindows and static URI fields
    // via the partial-class relationship.
    public sealed partial class App
    {
        public void SetTheme(string theme)
        {
            _currentTheme = string.IsNullOrWhiteSpace(theme) ? "dark" : theme.Trim().ToLowerInvariant();
            var themePath = _currentTheme == "light"
                ? "avares://PhantomVault.UI/Assets/Themes/PhantomTheme.Light.axaml"
                : "avares://PhantomVault.UI/Assets/Themes/PhantomTheme.Dark.axaml";

            TraceStyles("App.SetTheme entry", this.Styles);

            try
            {
                var toRemove = new System.Collections.Generic.List<StyleInclude>();
                for (int i = 0; i < this.Styles.Count; i++)
                {
                    if (this.Styles[i] is StyleInclude si && si.Source != null && si.Source.OriginalString.Contains("Assets/Themes/PhantomTheme"))
                    {
                        Debug.WriteLine($"[SetTheme] App.Styles removing include {si.Source.OriginalString}");
#if DEBUG
                        Console.WriteLine($"[SetTheme] App.Styles removing include {si.Source.OriginalString}");
#endif
                        toRemove.Add(si);
                    }
                }

                foreach (var rem in toRemove)
                {
                    this.Styles.Remove(rem);
                }

                var newInclude = new StyleInclude(new Uri("avares://PhantomVault.UI/")) { Source = new Uri(themePath) };
                this.Styles.Add(newInclude);
                Debug.WriteLine($"[SetTheme] App.Styles added include {themePath}");
#if DEBUG
                Console.WriteLine($"[SetTheme] App.Styles added include {themePath}");
#endif
                TraceStyles("App.SetTheme after App.Styles update", this.Styles);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SetTheme] Error updating App.Styles: {ex.Message}");
#if DEBUG
                Console.WriteLine($"[SetTheme] Error updating App.Styles: {ex.Message}");
#endif
            }

            try
            {
                var desktop = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
                if (Application.Current != null)
                {
                    Application.Current.RequestedThemeVariant = _currentTheme == "light" ? Avalonia.Styling.ThemeVariant.Light : Avalonia.Styling.ThemeVariant.Dark;
                }

                if (desktop != null)
                {
                    foreach (var win in desktop.Windows)
                    {
                        Debug.WriteLine($"[SetTheme] Processing window: '{win.Title ?? win.GetType().Name}'");
#if DEBUG
                        Console.WriteLine($"[SetTheme] Processing window: '{win.Title ?? win.GetType().Name}'");
#endif

                        UpdateStylesCollection(win.Styles, themePath);

                        try
                        {
                            if (win.Resources?.MergedDictionaries is { } merged)
                            {
                                for (int mdIdx = 0; mdIdx < merged.Count; mdIdx++)
                                {
                                    var md = merged[mdIdx];
                                    if (md is StyleInclude mdsi && mdsi.Source != null && mdsi.Source.OriginalString.Contains("Assets/Themes/PhantomTheme"))
                                    {
                                        Debug.WriteLine($"[SetTheme] Window '{win.Title ?? win.GetType().Name}' merged dict[{mdIdx}] updating from {mdsi.Source.OriginalString} to {themePath}");
#if DEBUG
                                        Console.WriteLine($"[SetTheme] Window '{win.Title ?? win.GetType().Name}' merged dict[{mdIdx}] updating from {mdsi.Source.OriginalString} to {themePath}");
#endif
                                        mdsi.Source = new Uri(themePath);
                                    }
                                    else if (md is Avalonia.Styling.Styles nestedStyles)
                                    {
                                        UpdateStylesCollection(nestedStyles, themePath);
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[SetTheme] Error updating window resources for '{win.Title ?? win.GetType().Name}': {ex.Message}");
#if DEBUG
                            Console.WriteLine($"[SetTheme] Error updating window resources for '{win.Title ?? win.GetType().Name}': {ex.Message}");
#endif
                        }
                    }
                }
            }
            catch
            {

            }

            try
            {
                if (Application.Current?.Resources?.MergedDictionaries is { } appMerged)
                {
                    TraceMergedDictionaries("App.SetTheme before resource update", appMerged);
                    for (int mdIdx = 0; mdIdx < appMerged.Count; mdIdx++)
                    {
                        var md = appMerged[mdIdx];
                        if (md is StyleInclude mdsi && mdsi.Source != null && mdsi.Source.OriginalString.Contains("Assets/Themes/PhantomTheme"))
                        {
                            Debug.WriteLine($"[SetTheme] App.Resources.MergedDictionaries[{mdIdx}] updating from {mdsi.Source.OriginalString} to {themePath}");
#if DEBUG
                            Console.WriteLine($"[SetTheme] App.Resources.MergedDictionaries[{mdIdx}] updating from {mdsi.Source.OriginalString} to {themePath}");
#endif
                            mdsi.Source = new Uri(themePath);
                        }
                        else if (md is Avalonia.Styling.Styles nestedStyles)
                        {
                            UpdateStylesCollection(nestedStyles, themePath);
                        }
                    }
                    TraceMergedDictionaries("App.SetTheme after resource update", appMerged);
                }
            }
            catch
            {

            }

            CleanupThemeAwareWindows();

            foreach (var reference in _themeAwareWindows)
            {
                if (reference.TryGetTarget(out var window))
                {
                    ApplyThemeToWindow(window);
                }
            }

            try
            {
                var desktop2 = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;

                if (Application.Current?.Styles is Styles appStyles)
                {
                    UpdateStylesCollection(appStyles, themePath);
                }

                if (Application.Current?.Resources?.MergedDictionaries is { } appMerged2)
                {
                    for (int mdIdx = 0; mdIdx < appMerged2.Count; mdIdx++)
                    {
                        var md = appMerged2[mdIdx];
                        if (md is StyleInclude mdsi && mdsi.Source != null && mdsi.Source.OriginalString.Contains("Assets/Themes/PhantomTheme"))
                        {
                            mdsi.Source = new Uri(themePath);
                        }
                        else if (md is Styles nested)
                        {
                            UpdateStylesCollection(nested, themePath);
                        }
                    }
                }

                if (desktop2 != null)
                {
                    foreach (var win in desktop2.Windows)
                    {
                        UpdateStylesCollection(win.Styles, themePath);

                        if (win.Resources?.MergedDictionaries is { } merged2)
                        {
                            for (int mdIdx = 0; mdIdx < merged2.Count; mdIdx++)
                            {
                                var md = merged2[mdIdx];
                                if (md is StyleInclude mdsi && mdsi.Source != null && mdsi.Source.OriginalString.Contains("Assets/Themes/PhantomTheme"))
                                {
                                    mdsi.Source = new Uri(themePath);
                                }
                                else if (md is Styles nested)
                                {
                                    UpdateStylesCollection(nested, themePath);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SetTheme] FinalizeThemeIncludes error: {ex.Message}");
#if DEBUG
                Console.WriteLine($"[SetTheme] FinalizeThemeIncludes error: {ex.Message}");
#endif
            }
        }

        public void RegisterThemeAwareWindow(Window window)
        {
            if (window == null)
            {
                return;
            }

            CleanupThemeAwareWindows();

            if (!IsWindowTracked(window))
            {
                _themeAwareWindows.Add(new WeakReference<Window>(window));
            }

            ApplyThemeToWindow(window);
        }

        public void UnregisterThemeAwareWindow(Window window)
        {
            if (window == null)
            {
                return;
            }

            for (int i = _themeAwareWindows.Count - 1; i >= 0; i--)
            {
                if (!_themeAwareWindows[i].TryGetTarget(out var existing) || ReferenceEquals(existing, window))
                {
                    _themeAwareWindows.RemoveAt(i);
                }
            }
        }

        private bool IsWindowTracked(Window window)
        {
            foreach (var reference in _themeAwareWindows)
            {
                if (reference.TryGetTarget(out var existing) && ReferenceEquals(existing, window))
                {
                    return true;
                }
            }

            return false;
        }

        private void CleanupThemeAwareWindows()
        {
            for (int i = _themeAwareWindows.Count - 1; i >= 0; i--)
            {
                if (!_themeAwareWindows[i].TryGetTarget(out _))
                {
                    _themeAwareWindows.RemoveAt(i);
                }
            }
        }

        private void ApplyThemeToWindow(Window window)
        {
            if (window == null)
            {
                return;
            }

            RemoveThemeIncludes(window);

            if (_currentTheme == "light")
            {
                window.Styles.Add(new StyleInclude(ThemeBaseUri) { Source = LightThemeUri });
                window.RequestedThemeVariant = ThemeVariant.Light;
            }
            else
            {

                window.Styles.Add(new StyleInclude(ThemeBaseUri) { Source = DarkThemeUri });
                window.RequestedThemeVariant = ThemeVariant.Dark;
            }
        }

        private static void RemoveThemeIncludes(Window window)
        {

            for (int i = window.Styles.Count - 1; i >= 0; i--)
            {
                if (window.Styles[i] is StyleInclude include && include.Source != null)
                {
                    var src = include.Source.OriginalString ?? string.Empty;
                    if (src.Contains("Assets/Themes/PhantomTheme"))
                    {
                        window.Styles.RemoveAt(i);
                    }
                }
            }
        }

        private static void UpdateStylesCollection(Styles? styles, string themePath)
        {
            if (styles == null || styles.Count == 0)
            {
                return;
            }

            TraceStyles("UpdateStylesCollection before", styles);

            for (int i = 0; i < styles.Count; i++)
            {
                if (styles[i] is StyleInclude include && include.Source != null && include.Source.OriginalString.Contains("Assets/Themes/PhantomTheme"))
                {
                    Debug.WriteLine($"[UpdateStylesCollection] Updating style include from {include.Source.OriginalString} to {themePath}");
#if DEBUG
                    Console.WriteLine($"[UpdateStylesCollection] Updating style include from {include.Source.OriginalString} to {themePath}");
#endif
                    include.Source = new Uri(themePath);
                }
            }

            TraceStyles("UpdateStylesCollection after", styles);
        }

        private static void TraceStyles(string context, Styles? styles)
        {
            if (styles == null)
            {
                Debug.WriteLine($"[TraceStyles] {context}: styles=null");
#if DEBUG
                Console.WriteLine($"[TraceStyles] {context}: styles=null");
#endif
                return;
            }

            Debug.WriteLine($"[TraceStyles] {context}: count={styles.Count}");
#if DEBUG
            Console.WriteLine($"[TraceStyles] {context}: count={styles.Count}");
#endif

            for (int i = 0; i < styles.Count; i++)
            {
                if (styles[i] is StyleInclude include)
                {
                    var source = include.Source?.OriginalString ?? "<null>";
                    Debug.WriteLine($"[TraceStyles]   [{i}] StyleInclude -> {source}");
#if DEBUG
                    Console.WriteLine($"[TraceStyles]   [{i}] StyleInclude -> {source}");
#endif
                }
                else if (styles[i] is Styles nested)
                {
                    Debug.WriteLine($"[TraceStyles]   [{i}] Styles (nested) count={nested.Count}");
#if DEBUG
                    Console.WriteLine($"[TraceStyles]   [{i}] Styles (nested) count={nested.Count}");
#endif
                }
                else if (styles[i] != null)
                {
                    Debug.WriteLine($"[TraceStyles]   [{i}] {styles[i].GetType().Name}");
#if DEBUG
                    Console.WriteLine($"[TraceStyles]   [{i}] {styles[i].GetType().Name}");
#endif
                }
                else
                {
                    Debug.WriteLine($"[TraceStyles]   [{i}] <null entry>");
#if DEBUG
                    Console.WriteLine($"[TraceStyles]   [{i}] <null entry>");
#endif
                }
            }
        }

        private static void TraceMergedDictionaries(string context, IList<IResourceProvider>? dictionaries)
        {
            if (dictionaries == null)
            {
                Debug.WriteLine($"[TraceMergedDictionaries] {context}: dictionaries=null");
#if DEBUG
                Console.WriteLine($"[TraceMergedDictionaries] {context}: dictionaries=null");
#endif
                return;
            }

            Debug.WriteLine($"[TraceMergedDictionaries] {context}: count={dictionaries.Count}");
#if DEBUG
            Console.WriteLine($"[TraceMergedDictionaries] {context}: count={dictionaries.Count}");
#endif

            for (int i = 0; i < dictionaries.Count; i++)
            {
                if (dictionaries[i] is StyleInclude include)
                {
                    var source = include.Source?.OriginalString ?? "<null>";
                    Debug.WriteLine($"[TraceMergedDictionaries]   [{i}] StyleInclude -> {source}");
#if DEBUG
                    Console.WriteLine($"[TraceMergedDictionaries]   [{i}] StyleInclude -> {source}");
#endif
                }
                else if (dictionaries[i] is Styles nested)
                {
                    Debug.WriteLine($"[TraceMergedDictionaries]   [{i}] Styles (nested) count={nested.Count}");
#if DEBUG
                    Console.WriteLine($"[TraceMergedDictionaries]   [{i}] Styles (nested) count={nested.Count}");
#endif
                }
                else if (dictionaries[i] != null)
                {
                    Debug.WriteLine($"[TraceMergedDictionaries]   [{i}] {dictionaries[i].GetType().Name}");
#if DEBUG
                    Console.WriteLine($"[TraceMergedDictionaries]   [{i}] {dictionaries[i].GetType().Name}");
#endif
                }
                else
                {
                    Debug.WriteLine($"[TraceMergedDictionaries]   [{i}] <null entry>");
#if DEBUG
                    Console.WriteLine($"[TraceMergedDictionaries]   [{i}] <null entry>");
#endif
                }
            }
        }
    }
}
