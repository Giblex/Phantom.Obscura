#nullable enable
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using PhantomVault.Core.Services.Security;
using Xunit;

namespace PhantomVault.Core.Tests.Security;

/// <summary>
/// Startup-safety guards for the defence services' DI wiring.
///
/// ExportGuard and ClipboardGuard both take an ILogger&lt;T&gt; that the app never registers,
/// relying on the container honouring constructor default values. That behaviour is real
/// but easy to break: adding a non-optional parameter, or removing a default, turns a
/// silent assumption into an exception at first resolve — and for ExportGuard that is the
/// moment the user opens the export window.
///
/// These tests pin the assumption using exactly the registrations App.Composition uses.
/// </summary>
public sealed class GuardResolutionTests
{
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();

        // Mirrors App.Composition.cs — note the deliberate absence of AddLogging(),
        // which is the assumption under test. The rules list is required by both
        // DefenceEngine and DefenceSettingsService.
        services.AddSingleton<IReadOnlyList<DefenceRule>>(_ => new List<DefenceRule>
        {
            new DefenceRule("excessive-exports", ThreatType.HighRiskEntryFlood,
                ThreatLevel.Warning, new[] { DefenceActionType.AddDelay }),
            new DefenceRule("clipboard-guard", ThreatType.HighRiskEntryFlood,
                ThreatLevel.Warning, new[] { DefenceActionType.AddDelay }),
        });
        services.AddSingleton<IClipboardGuard, ClipboardGuard>();
        services.AddSingleton<IExportGuard, ExportGuard>();
        services.AddSingleton<IDefenceSettingsService, DefenceSettingsService>();

        // The real DefenceEngine drags in the whole auth/controller graph, which is not
        // what these tests are about. A stub keeps the subject narrow: the guards must
        // still activate when their ILogger parameter has no registration behind it.
        services.AddSingleton<IDefenceEngine, StubDefenceEngine>();

        return services.BuildServiceProvider();
    }

    [Fact]
    public void ExportGuard_ResolvesWithoutLoggingRegistered()
    {
        using var provider = BuildProvider();
        var guard = provider.GetRequiredService<IExportGuard>();
        Assert.NotNull(guard);
    }

    [Fact]
    public void ClipboardGuard_ResolvesWithoutLoggingRegistered()
    {
        using var provider = BuildProvider();
        var guard = provider.GetRequiredService<IClipboardGuard>();
        Assert.NotNull(guard);
    }

    [Fact]
    public void ExportGuard_AllowsExport_WhenRuleDisabled()
    {
        var settings = new StubDefenceSettings(enabled: false);
        var guard = new ExportGuard(defenceSettings: settings);

        // Rule off: the cooldown must never engage, no matter how many exports run.
        for (var i = 0; i < 25; i++)
        {
            guard.RegisterExport("CSV");
        }

        Assert.True(guard.CanExport("CSV"));
    }

    [Fact]
    public void ExportGuard_EngagesCooldown_WhenRuleEnabled()
    {
        var settings = new StubDefenceSettings(enabled: true);
        var guard = new ExportGuard(defenceSettings: settings);

        Assert.True(guard.CanExport("CSV"));

        // Comfortably past MaxExportsPerHour.
        for (var i = 0; i < 25; i++)
        {
            guard.RegisterExport("CSV");
        }

        Assert.False(guard.CanExport("CSV"));
    }

    [Fact]
    public void ClipboardGuard_AllowsCopy_WhenRuleDisabled()
    {
        var settings = new StubDefenceSettings(enabled: false);
        var guard = new ClipboardGuard(defenceSettings: settings);

        for (var i = 0; i < 200; i++)
        {
            guard.RegisterCopy("entry-" + i);
        }

        Assert.True(guard.CanCopy());
    }

    [Fact]
    public void ClipboardGuard_EngagesCooldown_WhenRuleEnabled()
    {
        var settings = new StubDefenceSettings(enabled: true);
        var guard = new ClipboardGuard(defenceSettings: settings);

        Assert.True(guard.CanCopy());

        for (var i = 0; i < 200; i++)
        {
            guard.RegisterCopy("entry-" + i);
        }

        Assert.False(guard.CanCopy());
    }

    /// <summary>Records threats without touching the auth/controller graph.</summary>
    private sealed class StubDefenceEngine : IDefenceEngine
    {
        public void RaiseThreat(ThreatEvent threat) { }
    }

    /// <summary>Defence settings stub with a fixed answer for every rule.</summary>
    private sealed class StubDefenceSettings : IDefenceSettingsService
    {
        private readonly bool _enabled;
        public StubDefenceSettings(bool enabled) => _enabled = enabled;
        public bool GetRuleEnabled(string ruleId) => _enabled;
        public void SetRuleEnabled(string ruleId, bool enabled) { }
    }
}
