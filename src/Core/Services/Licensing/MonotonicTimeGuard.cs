using System;
using System.Globalization;
using System.IO;

namespace PhantomVault.Core.Services.Licensing
{
    /// <summary>
    /// Detects wall-clock rollback. The local license/subscription timer is only as
    /// trustworthy as the system clock, so a user can "extend" an expired license simply by
    /// setting the date backwards. This guard records the highest UTC instant ever observed
    /// and flags any later reading that falls meaningfully behind it.
    /// </summary>
    public interface IMonotonicTimeGuard
    {
        /// <summary>
        /// Returns <c>true</c> if <paramref name="now"/> is earlier than the highest time
        /// previously seen (beyond a small tolerance), indicating the clock was wound back.
        /// Always advances the persisted high-watermark to the furthest-forward trusted time.
        /// </summary>
        bool IsRollback(DateTimeOffset now);
    }

    /// <summary>
    /// File-backed <see cref="IMonotonicTimeGuard"/>. The watermark is durably written next to
    /// the vault so it travels with the device. Deleting the file only resets the watermark —
    /// it cannot move time forward — so the worst a tamperer achieves is one undetected reset,
    /// while casual "set the clock back a month" attacks are caught.
    /// </summary>
    public sealed class MonotonicTimeGuard : IMonotonicTimeGuard
    {
        private readonly string _statePath;
        private readonly TimeSpan _tolerance;
        private readonly object _gate = new();

        public MonotonicTimeGuard(string statePath, TimeSpan? tolerance = null)
        {
            _statePath = statePath ?? throw new ArgumentNullException(nameof(statePath));
            // Absorb routine NTP corrections / DST quirks without false positives.
            _tolerance = tolerance ?? TimeSpan.FromHours(1);
        }

        public bool IsRollback(DateTimeOffset now)
        {
            lock (_gate)
            {
                DateTimeOffset? watermark = ReadWatermark();
                bool rollback = watermark.HasValue && now < watermark.Value - _tolerance;

                DateTimeOffset advanced = watermark.HasValue && watermark.Value > now ? watermark.Value : now;
                WriteWatermark(advanced);

                return rollback;
            }
        }

        private DateTimeOffset? ReadWatermark()
        {
            try
            {
                if (!File.Exists(_statePath)) return null;
                string text = File.ReadAllText(_statePath).Trim();
                if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long unix))
                    return DateTimeOffset.FromUnixTimeSeconds(unix);
            }
            catch { }
            return null;
        }

        private void WriteWatermark(DateTimeOffset value)
        {
            try
            {
                var dir = Path.GetDirectoryName(_statePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                string tmp = _statePath + ".tmp";
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 64, FileOptions.WriteThrough))
                using (var sw = new StreamWriter(fs))
                {
                    sw.Write(value.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
                    sw.Flush();
                    fs.Flush(flushToDisk: true);
                }

                if (File.Exists(_statePath))
                    File.Replace(tmp, _statePath, null, ignoreMetadataErrors: true);
                else
                    File.Move(tmp, _statePath);
            }
            catch { }
        }
    }
}
