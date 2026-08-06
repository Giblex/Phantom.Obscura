using System;

namespace PhantomVault.UI.Services
{
    // Shared "coloured blur" preference for category/tile surfaces. When enabled,
    // tile surfaces are tinted with their own category colour; when disabled they
    // use a neutral translucent glass. Surfaced as a toggle in both Theme settings
    // and the Category Manager, which reflect each other through this single source.
    public static class CategoryBlurPreference
    {
        private static bool _useColouredBlur;
        private static bool _loaded;

        public static event EventHandler? Changed;

        public static bool UseColouredBlur
        {
            get
            {
                EnsureLoaded();
                return _useColouredBlur;
            }
            set
            {
                EnsureLoaded();
                if (_useColouredBlur == value)
                {
                    return;
                }

                _useColouredBlur = value;
                try { SettingsService.Update(s => s.UseColouredCategoryBlur = value); } catch { }
                Changed?.Invoke(null, EventArgs.Empty);
            }
        }

        private static void EnsureLoaded()
        {
            if (_loaded)
            {
                return;
            }

            try { _useColouredBlur = SettingsService.Load().UseColouredCategoryBlur; } catch { }
            _loaded = true;
        }
    }
}
