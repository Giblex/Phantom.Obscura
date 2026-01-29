using Android.OS;
using System;

namespace PhantomVault.UI.Mobile.Platforms.Android
{
    /// <summary>
    /// Detects if running on Android emulator with detailed information
    /// </summary>
    public static class EmulatorDetector
    {
        /// <summary>
        /// Check if running on any Android emulator
        /// </summary>
        public static bool IsEmulator()
        {
            return IsGenericEmulator() || 
                   IsGoogleEmulator() || 
                   IsGenymotion() || 
                   IsBluestacks();
        }

        /// <summary>
        /// Check for generic Android emulator
        /// </summary>
        public static bool IsGenericEmulator()
        {
            var brand = Build.Brand?.ToLower() ?? "";
            var device = Build.Device?.ToLower() ?? "";
            var product = Build.Product?.ToLower() ?? "";
            var model = Build.Model?.ToLower() ?? "";
            var hardware = Build.Hardware?.ToLower() ?? "";

            return brand.Contains("generic") ||
                   device.Contains("generic") ||
                   product.Contains("sdk") ||
                   product.Contains("emulator") ||
                   model.Contains("sdk") ||
                   model.Contains("emulator") ||
                   model.Contains("android sdk built for") ||
                   hardware.Contains("goldfish") ||
                   hardware.Contains("ranchu");
        }

        /// <summary>
        /// Check for Google Android Emulator (AVD)
        /// </summary>
        public static bool IsGoogleEmulator()
        {
            var manufacturer = Build.Manufacturer?.ToLower() ?? "";
            var brand = Build.Brand?.ToLower() ?? "";
            var product = Build.Product?.ToLower() ?? "";
            var fingerprint = Build.Fingerprint?.ToLower() ?? "";

            return (manufacturer.Contains("google") && brand.Contains("google")) ||
                   product.Contains("sdk_gphone") ||
                   product.Contains("google_sdk") ||
                   fingerprint.Contains("generic");
        }

        /// <summary>
        /// Check for Genymotion emulator
        /// </summary>
        public static bool IsGenymotion()
        {
            var product = Build.Product?.ToLower() ?? "";
            var manufacturer = Build.Manufacturer?.ToLower() ?? "";

            return product.Contains("vbox") ||
                   manufacturer.Contains("genymotion");
        }

        /// <summary>
        /// Check for Bluestacks emulator
        /// </summary>
        public static bool IsBluestacks()
        {
            var manufacturer = Build.Manufacturer?.ToLower() ?? "";
            var model = Build.Model?.ToLower() ?? "";

            return manufacturer.Contains("bluestacks") ||
                   model.Contains("bluestacks");
        }

        /// <summary>
        /// Get emulator type name
        /// </summary>
        public static string GetEmulatorType()
        {
            if (IsGoogleEmulator()) return "Google Android Emulator (AVD)";
            if (IsGenymotion()) return "Genymotion";
            if (IsBluestacks()) return "Bluestacks";
            if (IsGenericEmulator()) return "Generic Emulator";
            return "Physical Device";
        }

        /// <summary>
        /// Get detailed device information
        /// </summary>
        public static string GetDetailedInfo()
        {
            return $@"Device Information:
- Type: {GetEmulatorType()}
- Brand: {Build.Brand}
- Manufacturer: {Build.Manufacturer}
- Device: {Build.Device}
- Model: {Build.Model}
- Product: {Build.Product}
- Hardware: {Build.Hardware}
- Fingerprint: {Build.Fingerprint}
- Android Version: {Build.VERSION.Release} (SDK {Build.VERSION.SdkInt})
- Is Emulator: {IsEmulator()}";
        }

        /// <summary>
        /// Log device information for debugging
        /// </summary>
        public static void LogDeviceInfo()
        {
            System.Diagnostics.Debug.WriteLine("=== Android Device Detection ===");
            System.Diagnostics.Debug.WriteLine(GetDetailedInfo());
            System.Diagnostics.Debug.WriteLine("================================");
        }
    }
}
