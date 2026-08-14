using System;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;

namespace PhantomVault.UI.Views.Controls
{

    public enum DateFieldMode
    {

        DayMonthYear = 0,

        MonthYear = 1
    }

    public sealed class MonthOption
    {
        public MonthOption(int number, string name)
        {
            Number = number;
            Name = name;
        }

        public int Number { get; }

        public string Name { get; }

        public override string ToString() => Name;
    }

    public partial class DateField : UserControl
    {
        private const int YearsBack = 80;
        private const int YearsForward = 25;

        public static readonly StyledProperty<DateTimeOffset?> SelectedDateProperty =
            AvaloniaProperty.Register<DateField, DateTimeOffset?>(
                nameof(SelectedDate),
                defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

        public static readonly StyledProperty<DateFieldMode> ModeProperty =
            AvaloniaProperty.Register<DateField, DateFieldMode>(nameof(Mode), defaultValue: DateFieldMode.DayMonthYear);

        public static readonly StyledProperty<string> MonthTextProperty =
            AvaloniaProperty.Register<DateField, string>(
                nameof(MonthText),
                defaultValue: string.Empty,
                defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

        public static readonly StyledProperty<string> YearTextProperty =
            AvaloniaProperty.Register<DateField, string>(
                nameof(YearText),
                defaultValue: string.Empty,
                defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

        private bool _suppress;
        private bool _initialized;

        public DateTimeOffset? SelectedDate
        {
            get => GetValue(SelectedDateProperty);
            set => SetValue(SelectedDateProperty, value);
        }

        public DateFieldMode Mode
        {
            get => GetValue(ModeProperty);
            set => SetValue(ModeProperty, value);
        }

        public string MonthText
        {
            get => GetValue(MonthTextProperty);
            set => SetValue(MonthTextProperty, value);
        }

        public string YearText
        {
            get => GetValue(YearTextProperty);
            set => SetValue(YearTextProperty, value);
        }

        public DateField()
        {
            InitializeComponent();
            PopulateOptions();
            ApplyMode();
            _initialized = true;
        }

        private void PopulateOptions()
        {
            _suppress = true;

            for (var day = 1; day <= 31; day++)
            {
                DayBox.Items.Add(day);
            }

            var monthNames = CultureInfo.CurrentCulture.DateTimeFormat.MonthNames;
            for (var month = 1; month <= 12; month++)
            {
                var name = month - 1 < monthNames.Length && !string.IsNullOrWhiteSpace(monthNames[month - 1])
                    ? monthNames[month - 1]
                    : month.ToString("00");

                MonthBox.Items.Add(new MonthOption(month, name));
            }

            var currentYear = DateTimeOffset.Now.Year;
            for (var year = currentYear + YearsForward; year >= currentYear - YearsBack; year--)
            {
                YearBox.Items.Add(year);
            }

            _suppress = false;
        }

        private void ApplyMode()
        {
            var showDay = Mode == DateFieldMode.DayMonthYear;

            DayBox.IsVisible = showDay;
            PartsGrid.ColumnDefinitions[0].Width = showDay ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            PartsGrid.ColumnDefinitions[1].Width = showDay ? new GridLength(8) : new GridLength(0);
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (!_initialized)
                return;

            if (change.Property == ModeProperty)
            {
                ApplyMode();
            }
            else if (change.Property == SelectedDateProperty)
            {
                if (_suppress)
                    return;

                SyncFromSelectedDate();
            }
            else if (change.Property == MonthTextProperty || change.Property == YearTextProperty)
            {
                if (_suppress)
                    return;

                SyncFromText();
            }
        }

        private void SyncFromSelectedDate()
        {
            _suppress = true;

            var date = SelectedDate;
            DayBox.SelectedItem = date.HasValue ? (object?)date.Value.Day : null;
            MonthBox.SelectedItem = date.HasValue
                ? MonthBox.Items.OfType<MonthOption>().FirstOrDefault(m => m.Number == date.Value.Month)
                : null;
            YearBox.SelectedItem = date.HasValue ? (object?)date.Value.Year : null;

            _suppress = false;
        }

        private void SyncFromText()
        {
            _suppress = true;

            if (int.TryParse(MonthText, out var month) && month is >= 1 and <= 12)
            {
                MonthBox.SelectedItem = MonthBox.Items.OfType<MonthOption>().FirstOrDefault(m => m.Number == month);
            }
            else if (string.IsNullOrWhiteSpace(MonthText))
            {
                MonthBox.SelectedItem = null;
            }

            if (int.TryParse(YearText, out var year))
            {

                if (year is >= 0 and <= 99)
                    year += 2000;

                var known = YearBox.Items.OfType<int>().Any(y => y == year);
                YearBox.SelectedItem = known ? (object?)year : null;
            }
            else if (string.IsNullOrWhiteSpace(YearText))
            {
                YearBox.SelectedItem = null;
            }

            _suppress = false;
        }

        private void OnPartChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_suppress || !_initialized)
                return;

            var month = (MonthBox.SelectedItem as MonthOption)?.Number;
            var year = YearBox.SelectedItem as int?;
            var day = DayBox.SelectedItem as int?;

            _suppress = true;

            MonthText = month?.ToString("00") ?? string.Empty;
            YearText = year?.ToString() ?? string.Empty;

            if (Mode == DateFieldMode.MonthYear)
            {
                SelectedDate = month.HasValue && year.HasValue
                    ? new DateTimeOffset(new DateTime(year.Value, month.Value, 1, 0, 0, 0, DateTimeKind.Utc))
                    : null;
            }
            else if (day.HasValue && month.HasValue && year.HasValue)
            {

                var safeDay = Math.Min(day.Value, DateTime.DaysInMonth(year.Value, month.Value));
                SelectedDate = new DateTimeOffset(new DateTime(year.Value, month.Value, safeDay, 0, 0, 0, DateTimeKind.Utc));

                if (safeDay != day.Value)
                    DayBox.SelectedItem = safeDay;
            }
            else
            {
                SelectedDate = null;
            }

            _suppress = false;
        }
    }
}
