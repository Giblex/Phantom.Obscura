using System;
using Microsoft.Win32;
using PhantomVault.UI.Services;
using Xunit;

namespace PhantomVault.UI.Tests;

// Exercises the real per-user Run key. The original value (if any) is captured and
// restored so a developer's actual "start with Windows" preference is never clobbered.
[Collection("WindowsStartup")]
public sealed class WindowsStartupRegistrationTests : IDisposable
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "PhantomObscura";
    private readonly string? _originalValue;

    public WindowsStartupRegistrationTests()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        _originalValue = key?.GetValue(ValueName) as string;
    }

    public void Dispose()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key == null) return;
            if (_originalValue == null)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            else
            {
                key.SetValue(ValueName, _originalValue, RegistryValueKind.String);
            }
        }
        catch
        {
            // Best effort restore of a shared machine-local key.
        }
    }

    [Fact]
    public void Apply_Enable_RegistersQuotedExecutablePath()
    {
        var applied = WindowsStartupRegistration.Apply(enabled: true);

        Assert.True(applied);
        Assert.True(WindowsStartupRegistration.IsEnabled());

        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        var value = key?.GetValue(ValueName) as string;
        Assert.False(string.IsNullOrEmpty(value));
        Assert.StartsWith("\"", value);
        Assert.EndsWith("\"", value);
    }

    [Fact]
    public void Apply_Disable_RemovesValue()
    {
        WindowsStartupRegistration.Apply(enabled: true);
        Assert.True(WindowsStartupRegistration.IsEnabled());

        var applied = WindowsStartupRegistration.Apply(enabled: false);

        Assert.True(applied);
        Assert.False(WindowsStartupRegistration.IsEnabled());
    }

    [Fact]
    public void Apply_Disable_WhenAlreadyAbsent_IsNoOpAndSucceeds()
    {
        WindowsStartupRegistration.Apply(enabled: false);

        var applied = WindowsStartupRegistration.Apply(enabled: false);

        Assert.True(applied);
        Assert.False(WindowsStartupRegistration.IsEnabled());
    }

    [Fact]
    public void Apply_Roundtrip_TogglesIsEnabled()
    {
        WindowsStartupRegistration.Apply(enabled: false);
        Assert.False(WindowsStartupRegistration.IsEnabled());

        WindowsStartupRegistration.Apply(enabled: true);
        Assert.True(WindowsStartupRegistration.IsEnabled());

        WindowsStartupRegistration.Apply(enabled: false);
        Assert.False(WindowsStartupRegistration.IsEnabled());
    }
}

[CollectionDefinition("WindowsStartup", DisableParallelization = true)]
public sealed class WindowsStartupCollectionDefinition
{
}
