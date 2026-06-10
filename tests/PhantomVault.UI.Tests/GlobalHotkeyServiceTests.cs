using System.Threading;
using PhantomVault.UI.Services;
using Xunit;

namespace PhantomVault.UI.Tests;

// Lifecycle/idempotency smoke tests for the Win32 hotkey service. We can't synthesise a
// real system hotkey reliably in a unit test, so we verify the P/Invoke surface starts and
// tears down cleanly. An unlikely chord (Ctrl+Alt+Shift+F24) avoids clobbering real bindings.
public sealed class GlobalHotkeyServiceTests
{
    private const uint VK_F24 = 0x87;
    private const uint Modifiers =
        GlobalHotkeyService.MOD_CONTROL | GlobalHotkeyService.MOD_ALT | GlobalHotkeyService.MOD_SHIFT;

    [Fact]
    public void Construct_DoesNotThrow()
    {
        using var service = new GlobalHotkeyService(Modifiers, VK_F24);
        Assert.NotNull(service);
    }

    [Fact]
    public void StartAndDispose_CompleteCleanly()
    {
        var service = new GlobalHotkeyService(Modifiers, VK_F24);
        service.Start();
        Thread.Sleep(100); // let the message pump bring up its window

        var ex = Record.Exception(() => service.Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public void Start_IsIdempotent()
    {
        using var service = new GlobalHotkeyService(Modifiers, VK_F24);

        var ex = Record.Exception(() =>
        {
            service.Start();
            service.Start();
        });

        Assert.Null(ex);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var service = new GlobalHotkeyService(Modifiers, VK_F24);
        service.Start();
        Thread.Sleep(50);

        service.Dispose();
        var ex = Record.Exception(() => service.Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public void HotkeyPressed_CanSubscribeAndUnsubscribe()
    {
        using var service = new GlobalHotkeyService(Modifiers, VK_F24);

        void Handler() { }

        var ex = Record.Exception(() =>
        {
            service.HotkeyPressed += Handler;
            service.HotkeyPressed -= Handler;
        });

        Assert.Null(ex);
    }
}
