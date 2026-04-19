using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using PhantomVault.UI.Services;
using PhantomVault.UI.ViewModels;
using Serilog;

namespace PhantomVault.UI.Views
{
    public partial class VaultCreationLoadingWindow : ThemeAwareWindow
    {
        // ── Core UI elements ──
        private Border? _progressFill;
        private TextBlock? _statusText;
        private TextBlock? _stepDetailText;
        private TextBlock? _loadingTitle;
        private Border? _completionCheck;
        private TextBlock? _progressPercent;
        private TextBlock? _progressLabel;
        private TextBlock? _hashReadout;

        // ── Techy elements ──
        private TextBlock? _hexColumnLeft;
        private TextBlock? _hexColumnRight;
        private Border? _scanLine;
        private StackPanel? _phaseChecklist;

        // ── Animation elements ──
        private Border? _orbitDot1;
        private Border? _orbitDot2;
        private Border? _orbitDot3;
        private Border? _pulseRing1;
        private Border? _pulseRing2;
        private Border? _pulseRing3;
        private Canvas? _segmentedRing;
        private Canvas? _segmentedRingInner;

        private DispatcherTimer? _animationTimer;
        private DateTime _animationStart;
        private readonly Random _rng = new();

        // ── Cipher scramble state ──
        private string _targetDetailText = "";
        private double _scrambleProgress;
        private bool _isScrambling;
        private const string HexChars = "0123456789ABCDEF";
        private const string CipherChars = "0123456789ABCDEFabcdef!@#$%^&*(){}[]<>|/\\~";

        // ── Hex column state ──
        private readonly List<string> _hexLinesLeft = new();
        private readonly List<string> _hexLinesRight = new();
        private int _hexScrollOffset;
        private const int HexVisibleLines = 38;

        // ── Phase tracking ──
        private int _currentPhaseIndex = -1;
        private double _currentPercent;
        private readonly List<(string icon, string label, Border border, TextBlock iconText, TextBlock labelText)> _phaseItems = new();

        public event EventHandler? CreationCompleted;
        private SetupWizardViewModel? _wizardViewModel;

        // Phase definitions
        private static readonly (string Icon, string Label)[] Phases =
        {
            ("\u25B7", "INITIALIZE"),
            ("\u25B7", "STAGING"),
            ("\u25B7", "KEYFILE"),
            ("\u25B7", "BIND"),
            ("\u25B7", "CONTAINERS"),
            ("\u25B7", "TRANSPORT"),
            ("\u25B7", "FINALIZE"),
        };

        public VaultCreationLoadingWindow()
        {
            // Fixed dark navy — pre-vault screens never follow user theme
            ThemeScope.SetIsThemed(this, false);
            InitializeComponent();

            // Core elements
            _progressFill = this.FindControl<Border>("ProgressFill");
            _statusText = this.FindControl<TextBlock>("StatusText");
            _stepDetailText = this.FindControl<TextBlock>("StepDetailText");
            _loadingTitle = this.FindControl<TextBlock>("LoadingTitle");
            _completionCheck = this.FindControl<Border>("CompletionCheck");
            _progressPercent = this.FindControl<TextBlock>("ProgressPercent");
            _progressLabel = this.FindControl<TextBlock>("ProgressLabel");
            _hashReadout = this.FindControl<TextBlock>("HashReadout");

            // Techy elements
            _hexColumnLeft = this.FindControl<TextBlock>("HexColumnLeft");
            _hexColumnRight = this.FindControl<TextBlock>("HexColumnRight");
            _scanLine = this.FindControl<Border>("ScanLine");
            _phaseChecklist = this.FindControl<StackPanel>("PhaseChecklist");

            // Animated elements
            _orbitDot1 = this.FindControl<Border>("OrbitDot1");
            _orbitDot2 = this.FindControl<Border>("OrbitDot2");
            _orbitDot3 = this.FindControl<Border>("OrbitDot3");
            _pulseRing1 = this.FindControl<Border>("PulseRing1");
            _pulseRing2 = this.FindControl<Border>("PulseRing2");
            _pulseRing3 = this.FindControl<Border>("PulseRing3");
            _segmentedRing = this.FindControl<Canvas>("SegmentedRing");
            _segmentedRingInner = this.FindControl<Canvas>("SegmentedRingInner");

            InitializeHexData();
            BuildSegmentedRings();
            BuildPhaseChecklist();
            StartAnimationTimer();
        }

        // ══════════════════════════════════════════════════════════════
        //  INITIALIZATION
        // ══════════════════════════════════════════════════════════════

        private void InitializeHexData()
        {
            // Pre-generate random hex lines for scrolling columns
            for (int i = 0; i < HexVisibleLines * 3; i++)
            {
                _hexLinesLeft.Add(GenerateHexLine(8));
                _hexLinesRight.Add(GenerateHexLine(8));
            }
        }

        private string GenerateHexLine(int bytes)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < bytes; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(HexChars[_rng.Next(16)]);
                sb.Append(HexChars[_rng.Next(16)]);
            }
            return sb.ToString();
        }

        private void BuildSegmentedRings()
        {
            // Outer ring: place small tick lines around a circle
            PopulateTickMarks(_segmentedRing, 175, 24, 6, "#186BB3AE");
            // Inner ring: smaller, more ticks, dimmer
            PopulateTickMarks(_segmentedRingInner, 140, 36, 4, "#106BB3AE");
        }

        private void PopulateTickMarks(Canvas? canvas, double diameter, int count, double tickLength, string colorHex)
        {
            if (canvas == null) return;

            double radius = diameter / 2.0;
            var brush = new SolidColorBrush(Color.Parse(colorHex));

            for (int i = 0; i < count; i++)
            {
                double angle = (2.0 * Math.PI * i) / count;
                // Tick sits on the outer edge, pointing inward
                double x1 = radius + Math.Cos(angle) * radius;
                double y1 = radius + Math.Sin(angle) * radius;
                double x2 = radius + Math.Cos(angle) * (radius - tickLength);
                double y2 = radius + Math.Sin(angle) * (radius - tickLength);

                var line = new Avalonia.Controls.Shapes.Line
                {
                    StartPoint = new Point(x1, y1),
                    EndPoint = new Point(x2, y2),
                    Stroke = brush,
                    StrokeThickness = 0.8,
                };
                canvas.Children.Add(line);
            }
        }

        private void BuildPhaseChecklist()
        {
            if (_phaseChecklist == null) return;

            foreach (var (icon, label) in Phases)
            {
                var container = new Border
                {
                    Opacity = 0.25,
                    Padding = new Thickness(0, 1),
                };

                var panel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                };

                var iconText = new TextBlock
                {
                    Text = icon,
                    FontFamily = new FontFamily("Consolas,Courier New,monospace"),
                    FontSize = 8,
                    Foreground = new SolidColorBrush(Color.Parse("#406BB3AE")),
                    VerticalAlignment = VerticalAlignment.Center,
                    Width = 12,
                };

                var labelText = new TextBlock
                {
                    Text = label,
                    FontFamily = new FontFamily("Consolas,Courier New,monospace"),
                    FontSize = 8.5,
                    FontWeight = FontWeight.Bold,
                    Foreground = new SolidColorBrush(Color.Parse("#40FFFFFF")),
                    VerticalAlignment = VerticalAlignment.Center,
                    LetterSpacing = 1.5,
                };

                panel.Children.Add(iconText);
                panel.Children.Add(labelText);
                container.Child = panel;
                _phaseChecklist.Children.Add(container);
                _phaseItems.Add((icon, label, container, iconText, labelText));
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  ANIMATION TIMER (~60fps)
        // ══════════════════════════════════════════════════════════════

        private void StartAnimationTimer()
        {
            _animationStart = DateTime.UtcNow;
            _animationTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _animationTimer.Tick += OnAnimationTick;
            _animationTimer.Start();
        }

        private void StopAnimationTimer()
        {
            if (_animationTimer != null)
            {
                _animationTimer.Stop();
                _animationTimer.Tick -= OnAnimationTick;
                _animationTimer = null;
            }
        }

        private void OnAnimationTick(object? sender, EventArgs e)
        {
            double elapsed = (DateTime.UtcNow - _animationStart).TotalSeconds;

            AnimateOrbitDots(elapsed);
            AnimatePulseRings(elapsed);
            AnimateSegmentedRings(elapsed);
            AnimateHexColumns(elapsed);
            AnimateScanLine(elapsed);
            AnimateCipherScramble(elapsed);
            AnimateHashReadout(elapsed);
        }

        private void AnimateOrbitDots(double elapsed)
        {
            if (_orbitDot1?.RenderTransform is RotateTransform rot1)
                rot1.Angle = (elapsed / 6.5 * 360.0) % 360.0;

            if (_orbitDot2?.RenderTransform is RotateTransform rot2)
                rot2.Angle = 360.0 - ((elapsed / 8.5 * 360.0) % 360.0);

            if (_orbitDot3?.RenderTransform is RotateTransform rot3)
                rot3.Angle = (elapsed / 11.0 * 360.0) % 360.0;
        }

        private void AnimatePulseRings(double elapsed)
        {
            if (_pulseRing1?.RenderTransform is ScaleTransform s1)
            {
                double t = (Math.Sin(elapsed * 2.0 * Math.PI / 3.2) + 1.0) / 2.0;
                double s = 0.85 + t * 0.20;
                s1.ScaleX = s; s1.ScaleY = s;
            }

            if (_pulseRing2?.RenderTransform is ScaleTransform s2)
            {
                double t = (Math.Sin((elapsed - 1.0) * 2.0 * Math.PI / 3.2) + 1.0) / 2.0;
                double s = 0.9 + t * 0.20;
                s2.ScaleX = s; s2.ScaleY = s;
            }

            if (_pulseRing3?.RenderTransform is ScaleTransform s3)
            {
                double t = (Math.Sin((elapsed - 2.0) * 2.0 * Math.PI / 3.2) + 1.0) / 2.0;
                double s = 0.95 + t * 0.20;
                s3.ScaleX = s; s3.ScaleY = s;
            }
        }

        private void AnimateSegmentedRings(double elapsed)
        {
            // Outer tick ring — slow clockwise
            if (_segmentedRing?.RenderTransform is RotateTransform segRot)
                segRot.Angle = (elapsed / 18.0 * 360.0) % 360.0;

            // Inner tick ring — slow counter-clockwise
            if (_segmentedRingInner?.RenderTransform is RotateTransform segRotInner)
                segRotInner.Angle = 360.0 - ((elapsed / 14.0 * 360.0) % 360.0);
        }

        private void AnimateHexColumns(double elapsed)
        {
            // Scroll hex data every ~150ms
            int newOffset = (int)(elapsed / 0.15);
            if (newOffset == _hexScrollOffset) return;
            _hexScrollOffset = newOffset;

            // Occasionally mutate a random line to simulate data flow
            if (_rng.Next(3) == 0)
            {
                int idx = _rng.Next(_hexLinesLeft.Count);
                _hexLinesLeft[idx] = GenerateHexLine(8);
            }
            if (_rng.Next(3) == 0)
            {
                int idx = _rng.Next(_hexLinesRight.Count);
                _hexLinesRight[idx] = GenerateHexLine(8);
            }

            // Build visible text
            var sbLeft = new StringBuilder();
            var sbRight = new StringBuilder();
            for (int i = 0; i < HexVisibleLines; i++)
            {
                int idxL = (_hexScrollOffset + i) % _hexLinesLeft.Count;
                int idxR = (_hexScrollOffset + i + 5) % _hexLinesRight.Count;
                sbLeft.AppendLine(_hexLinesLeft[idxL]);
                sbRight.AppendLine(_hexLinesRight[idxR]);
            }

            if (_hexColumnLeft != null) _hexColumnLeft.Text = sbLeft.ToString();
            if (_hexColumnRight != null) _hexColumnRight.Text = sbRight.ToString();
        }

        private void AnimateScanLine(double elapsed)
        {
            if (_scanLine == null) return;

            // Sweep down over 3 seconds, then reset
            double period = 4.8;
            double t = (elapsed % period) / period; // 0→1
            double windowHeight = 560.0;

            _scanLine.Opacity = t < 0.05 || t > 0.95 ? 0 : 0.4;
            _scanLine.Margin = new Thickness(60, t * windowHeight, 60, 0);
        }

        private void AnimateCipherScramble(double elapsed)
        {
            if (!_isScrambling || _stepDetailText == null) return;

            _scrambleProgress += 0.035; // speed of reveal

            if (_scrambleProgress >= 1.0)
            {
                _stepDetailText.Text = _targetDetailText;
                _isScrambling = false;
                return;
            }

            // Progressively reveal characters left to right, with random chars for unrevealed
            var sb = new StringBuilder();
            int revealedCount = (int)(_targetDetailText.Length * _scrambleProgress);

            for (int i = 0; i < _targetDetailText.Length; i++)
            {
                if (i < revealedCount)
                    sb.Append(_targetDetailText[i]);
                else
                    sb.Append(CipherChars[_rng.Next(CipherChars.Length)]);
            }

            _stepDetailText.Text = sb.ToString();
        }

        private void AnimateHashReadout(double elapsed)
        {
            if (_hashReadout == null) return;

            // Update hash readout every ~200ms with random hash fragment
            if ((int)(elapsed / 0.2) % 2 == 0 && _currentPercent > 0 && _currentPercent < 100)
            {
                var sb = new StringBuilder("SHA3: ");
                for (int i = 0; i < 24; i++)
                    sb.Append(HexChars[_rng.Next(16)]);
                _hashReadout.Text = sb.ToString();
            }
            else if (_currentPercent >= 100)
            {
                _hashReadout.Text = "";
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  VAULT CREATION FLOW
        // ══════════════════════════════════════════════════════════════

        public async Task RunCreationAsync(SetupWizardViewModel wizardViewModel)
        {
            _wizardViewModel = wizardViewModel;

            try
            {
                ApplyProvisioningProgress(0, 3, "Initializing secure provisioning...", "Handing off the validated setup plan to the provisioning engine.");
                _wizardViewModel.ProvisioningProgressChanged += OnProvisioningProgressChanged;
                await _wizardViewModel.ExecuteVaultCreationAsync();
                await ShowCompletion();
                await Task.Delay(900);

                CreationCompleted?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Vault creation failed during loading");
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    StopAnimationTimer();

                    if (_statusText != null)
                    {
                        _statusText.Text = $"Error: {ex.Message}";
                        _statusText.Classes.Remove("fade-status");
                        _statusText.Foreground = new SolidColorBrush(Color.Parse("#FF5252"));
                    }
                    if (_loadingTitle != null)
                        _loadingTitle.Text = "Vault Creation Failed";
                    if (_stepDetailText != null)
                        _stepDetailText.Text = "Please close this window and try again.";
                    if (_progressLabel != null)
                    {
                        _progressLabel.Text = "ERROR";
                        _progressLabel.Foreground = new SolidColorBrush(Color.Parse("#FF5252"));
                    }
                    if (_hashReadout != null)
                        _hashReadout.Text = "";
                });
            }
            finally
            {
                if (_wizardViewModel != null)
                    _wizardViewModel.ProvisioningProgressChanged -= OnProvisioningProgressChanged;
            }
        }

        private void OnProvisioningProgressChanged(object? sender, ProvisioningProgressEventArgs e)
        {
            ApplyProvisioningProgress(e.PhaseIndex, e.Percent, e.Status, e.Detail);
        }

        private void ApplyProvisioningProgress(int phaseIndex, double percent, string status, string detail)
        {
            _currentPercent = percent;
            const double maxWidth = 400.0;

            Dispatcher.UIThread.Post(() =>
            {
                if (_progressFill != null)
                    _progressFill.Width = maxWidth * (percent / 100.0);

                if (_statusText != null)
                    _statusText.Text = status;

                if (_progressPercent != null)
                    _progressPercent.Text = $"{Math.Clamp((int)Math.Round(percent), 0, 100)}%";

                if (_progressLabel != null)
                    _progressLabel.Text = phaseIndex >= 0 && phaseIndex < Phases.Length
                        ? Phases[phaseIndex].Label
                        : "PROVISIONING";

                _targetDetailText = detail;
                _scrambleProgress = 0;
                _isScrambling = true;
                UpdatePhaseChecklist(phaseIndex);
            });
        }

        private async Task AnimateProgress(double fromPercent, double toPercent, string status,
            string detail, string phaseLabel, int phaseIndex)
        {
            const double maxWidth = 400.0;
            _currentPercent = toPercent;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                // Progress bar
                if (_progressFill != null)
                    _progressFill.Width = maxWidth * (toPercent / 100.0);

                // Status text
                if (_statusText != null)
                    _statusText.Text = status;

                // Percentage readout
                if (_progressPercent != null)
                    _progressPercent.Text = $"{(int)toPercent}%";

                // Phase label
                if (_progressLabel != null)
                    _progressLabel.Text = phaseLabel;

                // Start cipher scramble for detail text
                _targetDetailText = detail;
                _scrambleProgress = 0;
                _isScrambling = true;

                // Update phase checklist
                UpdatePhaseChecklist(phaseIndex);
            });

            // Let transitions + scramble play out
            await Task.Delay(350);
        }

        private void UpdatePhaseChecklist(int activeIndex)
        {
            _currentPhaseIndex = activeIndex;

            for (int i = 0; i < _phaseItems.Count; i++)
            {
                var (_, _, border, iconText, labelText) = _phaseItems[i];

                if (i < activeIndex)
                {
                    // Completed
                    border.Opacity = 0.55;
                    iconText.Text = "\u2713"; // ✓
                    iconText.Foreground = new SolidColorBrush(Color.Parse("#6BB3AE"));
                    labelText.Foreground = new SolidColorBrush(Color.Parse("#506BB3AE"));
                }
                else if (i == activeIndex)
                {
                    // Active
                    border.Opacity = 1.0;
                    iconText.Text = "\u25B6"; // ▶
                    iconText.Foreground = new SolidColorBrush(Color.Parse("#8FB5DF"));
                    labelText.Foreground = new SolidColorBrush(Color.Parse("#C0FFFFFF"));
                }
                else
                {
                    // Pending
                    border.Opacity = 0.25;
                    iconText.Text = "\u25B7"; // ▷
                    iconText.Foreground = new SolidColorBrush(Color.Parse("#306BB3AE"));
                    labelText.Foreground = new SolidColorBrush(Color.Parse("#30FFFFFF"));
                }
            }
        }

        private async Task ShowCompletion()
        {
            StopAnimationTimer();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                // Mark all phases complete
                for (int i = 0; i < _phaseItems.Count; i++)
                {
                    var (_, _, border, iconText, labelText) = _phaseItems[i];
                    border.Opacity = 0.55;
                    iconText.Text = "\u2713";
                    iconText.Foreground = new SolidColorBrush(Color.Parse("#4CAF50"));
                    labelText.Foreground = new SolidColorBrush(Color.Parse("#504CAF50"));
                }

                if (_statusText != null)
                    _statusText.Classes.Remove("fade-status");
                if (_loadingTitle != null)
                    _loadingTitle.Text = "Vault Ready";
                if (_progressLabel != null)
                {
                    _progressLabel.Text = "COMPLETE";
                    _progressLabel.Foreground = new SolidColorBrush(Color.Parse("#504CAF50"));
                }
                if (_progressPercent != null)
                {
                    _progressPercent.Text = "100%";
                    _progressPercent.Foreground = new SolidColorBrush(Color.Parse("#504CAF50"));
                }
                if (_stepDetailText != null)
                {
                    _stepDetailText.Text = "All systems verified";
                    _isScrambling = false;
                }
                if (_hashReadout != null)
                    _hashReadout.Text = "";
                if (_completionCheck != null)
                {
                    _completionCheck.IsVisible = true;
                    _completionCheck.Classes.Add("visible");
                }

                // Fade out hex columns
                if (_hexColumnLeft != null) _hexColumnLeft.Opacity = 0;
                if (_hexColumnRight != null) _hexColumnRight.Opacity = 0;
                if (_scanLine != null) _scanLine.Opacity = 0;
            });
        }

        protected override void OnClosed(EventArgs e)
        {
            StopAnimationTimer();
            base.OnClosed(e);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
