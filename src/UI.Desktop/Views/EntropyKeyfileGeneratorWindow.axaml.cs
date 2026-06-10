using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using PhantomVault.UI.Services;

namespace PhantomVault.UI.Views;

public partial class EntropyKeyfileGeneratorWindow : Window
{
    private readonly EntropyKeyfileGenerator _generator = new();
    private readonly int _outputSizeBytes;
    private bool _leftPressed;
    private bool _rightPressed;

    public EntropyKeyfileGeneratorWindow() : this(64 * 1024)
    {
    }

    public EntropyKeyfileGeneratorWindow(int outputSizeBytes = 64 * 1024)
    {
        _outputSizeBytes = outputSizeBytes;
        InitializeComponent();
        OutputSizeText.Text = $"{_outputSizeBytes / 1024} KB";
        UpdateUi();
    }

    private void EntropySurface_PointerMoved(object? sender, PointerEventArgs e)
    {
        var point = e.GetPosition(EntropySurface);
        _generator.AddMouseSample(point.X, point.Y, _leftPressed, _rightPressed);
        UpdateUi();
    }

    private void EntropySurface_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var props = e.GetCurrentPoint(EntropySurface).Properties;
        _leftPressed = props.IsLeftButtonPressed;
        _rightPressed = props.IsRightButtonPressed;
        EntropySurface_PointerMoved(sender, e);
    }

    private void EntropySurface_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var props = e.GetCurrentPoint(EntropySurface).Properties;
        _leftPressed = props.IsLeftButtonPressed;
        _rightPressed = props.IsRightButtonPressed;
        EntropySurface_PointerMoved(sender, e);
    }

    private void UpdateUi()
    {
        var progress = Math.Min(100d,
            (_generator.CollectedEntropyBits / (double)_generator.MinimumRequiredBits) * 100d);

        EntropyProgress.Value = progress;
        SampleCountText.Text = _generator.SampleCount.ToString();
        EntropyDetailText.Text = $"{_generator.CollectedEntropyBits} estimated bits collected";

        var ready = _generator.CanFinalize;
        DoneButton.IsEnabled = ready;
        StatusText.Text = ready
            ? "Minimum threshold reached. Keep moving or generate now."
            : "Keep moving to build the entropy pool.";
        FooterStatusText.Text = ready
            ? "Ready to derive the keyfile"
            : $"Need at least {_generator.MinimumRequiredBits - _generator.CollectedEntropyBits} more estimated bits";
        EntropyBadge.Text = ready ? "✅" : "🌀";
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }

    private void Done_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var result = _generator.FinalizeKeyMaterial(_outputSizeBytes);
            Close(result);
        }
        catch (Exception ex)
        {
            FooterStatusText.Text = ex.Message;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _generator.Dispose();
        base.OnClosed(e);
    }
}

