using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MeetingBar.Windows.Models;
using MeetingBar.Windows.Services;
using static MeetingBar.Localization.Localizer;

namespace MeetingBar.Windows.Views
{
    public sealed class TimelineDayChangedEventArgs : EventArgs
    {
        public TimelineDayChangedEventArgs(int dayOffset, int direction) { DayOffset = dayOffset; Direction = direction; }
        public int DayOffset { get; }
        public int Direction { get; }
    }

    public sealed class DayTimelineControl : FrameworkElement
    {
        public static readonly DependencyProperty EventsProperty = DependencyProperty.Register(
            nameof(Events), typeof(IEnumerable), typeof(DayTimelineControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, EventsChanged));
        public static readonly DependencyProperty PastHoursProperty = DependencyProperty.Register(nameof(PastHours), typeof(int), typeof(DayTimelineControl), new FrameworkPropertyMetadata(3, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty FutureHoursProperty = DependencyProperty.Register(nameof(FutureHours), typeof(int), typeof(DayTimelineControl), new FrameworkPropertyMetadata(24, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty IncludePastDayProperty = DependencyProperty.Register(nameof(IncludePastDay), typeof(bool), typeof(DayTimelineControl), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty IncludeNextDayProperty = DependencyProperty.Register(nameof(IncludeNextDay), typeof(bool), typeof(DayTimelineControl), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty PastDaysProperty = DependencyProperty.Register(nameof(PastDays), typeof(int), typeof(DayTimelineControl), new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty FutureDaysProperty = DependencyProperty.Register(nameof(FutureDays), typeof(int), typeof(DayTimelineControl), new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty Use24HourClockProperty = DependencyProperty.Register(nameof(Use24HourClock), typeof(bool), typeof(DayTimelineControl), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty ShowTimelineDateProperty = DependencyProperty.Register(nameof(ShowTimelineDate), typeof(bool), typeof(DayTimelineControl), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty TimelineDateFormatProperty = DependencyProperty.Register(nameof(TimelineDateFormat), typeof(string), typeof(DayTimelineControl), new FrameworkPropertyMetadata("dd.MM.yyyy", FrameworkPropertyMetadataOptions.AffectsRender));

        private readonly DispatcherTimer _clock;
        private readonly Stopwatch _scrubClock = new Stopwatch();
        private INotifyCollectionChanged _observedEvents;
        private double _displayOffsetHours;
        private double _animationFromHours;
        private double _targetOffsetHours;
        private int _selectedDayOffset;
        private bool _scrubbing;

        public event EventHandler<TimelineDayChangedEventArgs> SelectedDayChanged;

        public DayTimelineControl()
        {
            SnapsToDevicePixels = true;
            ClipToBounds = true;
            Cursor = Cursors.Hand;
            _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _clock.Tick += (s, e) => InvalidateVisual();
            Loaded += (s, e) => _clock.Start();
            Unloaded += (s, e) => { _clock.Stop(); StopScrub(); };
        }

        public IEnumerable Events { get => (IEnumerable)GetValue(EventsProperty); set => SetValue(EventsProperty, value); }
        public int PastHours { get => (int)GetValue(PastHoursProperty); set => SetValue(PastHoursProperty, value); }
        public int FutureHours { get => (int)GetValue(FutureHoursProperty); set => SetValue(FutureHoursProperty, value); }
        public bool IncludePastDay { get => (bool)GetValue(IncludePastDayProperty); set => SetValue(IncludePastDayProperty, value); }
        public bool IncludeNextDay { get => (bool)GetValue(IncludeNextDayProperty); set => SetValue(IncludeNextDayProperty, value); }
        public int PastDays { get => (int)GetValue(PastDaysProperty); set => SetValue(PastDaysProperty, value); }
        public int FutureDays { get => (int)GetValue(FutureDaysProperty); set => SetValue(FutureDaysProperty, value); }
        public bool Use24HourClock { get => (bool)GetValue(Use24HourClockProperty); set => SetValue(Use24HourClockProperty, value); }
        public bool ShowTimelineDate { get => (bool)GetValue(ShowTimelineDateProperty); set => SetValue(ShowTimelineDateProperty, value); }
        public string TimelineDateFormat { get => (string)GetValue(TimelineDateFormatProperty); set => SetValue(TimelineDateFormatProperty, value); }

        public void ResetToToday()
        {
            StopScrub();
            _displayOffsetHours = 0;
            _animationFromHours = 0;
            _targetOffsetHours = 0;
            _selectedDayOffset = 0;
            InvalidateVisual();
        }

        protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
        {
            e.Handled = true;
            if (e.Delta == 0) return;
            var now = DateTime.Now;
            var baseRange = BaseRange(now);
            var allowed = AllowedRange(now);
            var viewportHours = Math.Max(1, (baseRange.Item2 - baseRange.Item1).TotalHours);
            var hoursPerNotch = Math.Max(0.5, Math.Min(2, viewportHours / 12.0));
            var requested = _targetOffsetHours - e.Delta / 120.0 * hoursPerNotch;
            var minimum = (allowed.Item1 - baseRange.Item1).TotalHours;
            var maximum = (allowed.Item2 - baseRange.Item2).TotalHours;
            requested = Math.Max(minimum, Math.Min(maximum, requested));
            if (Math.Abs(requested - _targetOffsetHours) < 0.001) return;
            var direction = requested > _targetOffsetHours ? 1 : -1;
            StartScrub(requested);

            var anchor = now.AddHours(_targetOffsetHours);
            var dayOffset = (anchor.Date - now.Date).Days;
            if (dayOffset == _selectedDayOffset) return;
            _selectedDayOffset = dayOffset;
            SelectedDayChanged?.Invoke(this, new TimelineDayChangedEventArgs(dayOffset, direction));
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (Math.Abs(_targetOffsetHours) < 0.001) return;
            var previousDay = _selectedDayOffset;
            var direction = _targetOffsetHours > 0 ? -1 : 1;
            StartScrub(0);
            _selectedDayOffset = 0;
            if (previousDay != 0) SelectedDayChanged?.Invoke(this, new TimelineDayChangedEventArgs(0, direction));
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);
            var width = ActualWidth;
            if (width <= 0 || ActualHeight <= 0) return;

            // A transparent fill makes blank timeline areas receive wheel input too.
            drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
            drawingContext.PushClip(new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight)));
            DrawTimeline(drawingContext, CurrentOffsetHours());
            drawingContext.Pop();
        }

        private void DrawTimeline(DrawingContext drawingContext, double offsetHours)
        {
            var width = ActualWidth;
            var now = DateTime.Now;
            var baseRange = BaseRange(now);
            var rangeStart = baseRange.Item1.AddHours(offsetHours);
            var rangeEnd = baseRange.Item2.AddHours(offsetHours);
            var rangeTicks = (double)(rangeEnd - rangeStart).Ticks;
            const double gridTop = 31;
            var gridBottom = Math.Max(gridTop + 32, ActualHeight - 3);
            var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var muted = BrushFrom("#FFB8B8B8");
            var gridBrush = BrushFrom("#4D707070");
            var grid = new Pen(gridBrush, 1);
            var dayGrid = new Pen(BrushFrom("#997A7A7A"), 1);

            var anchor = now.AddHours(offsetHours);
            var dayOffset = (anchor.Date - now.Date).Days;
            var dayName = dayOffset == 0 ? T("TODAY") : dayOffset == -1 ? T("YESTERDAY") : dayOffset == 1 ? T("TOMORROW") : CalendarEvent.FormatDayName(anchor);
            drawingContext.DrawText(Text(dayName, 9, FontWeights.SemiBold, muted, pixelsPerDip), new Point(0, 0));
            if (ShowTimelineDate)
            {
                var date = Text(CalendarEvent.FormatDate(anchor, TimelineDateFormat), 9, FontWeights.SemiBold, muted, pixelsPerDip);
                drawingContext.DrawText(date, new Point(Math.Max(0, width - date.Width), 0));
            }

            var firstHour = new DateTime(rangeStart.Year, rangeStart.Month, rangeStart.Day, rangeStart.Hour, 0, 0).AddHours(1);
            var durationHours = (rangeEnd - rangeStart).TotalHours;
            var labelInterval = durationHours <= 10 ? 2 : durationHours <= 17 ? 3 : 4;
            var hourIndex = 1;
            for (var hour = firstHour; hour < rangeEnd; hour = hour.AddHours(1), hourIndex++)
            {
                var x = X(hour, rangeStart, rangeTicks, width);
                var midnight = hour.Hour == 0;
                drawingContext.DrawLine(midnight ? dayGrid : grid, new Point(x, midnight ? 12 : gridTop), new Point(x, gridBottom));
                if (hourIndex % labelInterval == 0 || midnight)
                {
                    var label = Text(CalendarEvent.FormatTime(hour, Use24HourClock, !Use24HourClock), 9, midnight ? FontWeights.SemiBold : FontWeights.Normal, muted, pixelsPerDip);
                    drawingContext.DrawText(label, new Point(Math.Max(0, Math.Min(width - label.Width, x - label.Width / 2)), 14));
                }
            }

            var visible = (Events ?? Enumerable.Empty<object>()).Cast<object>().OfType<CalendarEvent>()
                .Where(item => !item.IsAllDay && !item.IsCancelled && item.End > rangeStart && item.Start < rangeEnd)
                .OrderBy(item => item.Start).ThenBy(item => item.End).ToList();
            var laneEnds = new List<DateTime>();
            foreach (var item in visible)
            {
                var lane = 0;
                while (lane < laneEnds.Count && laneEnds[lane] > item.Start) lane++;
                if (lane == laneEnds.Count) laneEnds.Add(item.End); else laneEnds[lane] = item.End;
                if (lane >= 3) lane = 2;

                var left = X(item.Start < rangeStart ? rangeStart : item.Start, rangeStart, rangeTicks, width);
                var right = X(item.End > rangeEnd ? rangeEnd : item.End, rangeStart, rangeTicks, width);
                var segmentWidth = Math.Max(3, right - left);
                var top = gridTop + 5 + lane * 13;
                drawingContext.DrawRoundedRectangle(EventBrush(item.CalendarColor), item.IsCurrent ? new Pen(Brushes.White, 1) : null, new Rect(left, top, segmentWidth, 10), 3, 3);
                if (segmentWidth < 36) continue;
                drawingContext.PushClip(new RectangleGeometry(new Rect(left + 3, top, Math.Max(0, segmentWidth - 6), 10)));
                DrawEventTitle(drawingContext, item.Title ?? "", new Point(left + 3, top - 1), pixelsPerDip);
                drawingContext.Pop();
            }

            if (now >= rangeStart && now <= rangeEnd)
            {
                var nowX = X(now, rangeStart, rangeTicks, width);
                var accent = BrushFrom("#FFD0D0D0");
                drawingContext.DrawLine(new Pen(accent, 1.5), new Point(nowX, gridTop - 2), new Point(nowX, gridBottom));
                drawingContext.DrawEllipse(accent, null, new Point(nowX, gridTop - 2), 3, 3);
            }
        }

        private Tuple<DateTime, DateTime> BaseRange(DateTime now)
        {
            var raw = EventWindowPolicy.Range(PastHours, FutureHours, IncludePastDay, IncludeNextDay, now);
            var allowed = AllowedRange(now);
            var start = raw.Item1 < allowed.Item1 ? allowed.Item1 : raw.Item1;
            var end = raw.Item2 > allowed.Item2 ? allowed.Item2 : raw.Item2;
            if (end <= start) end = start.AddHours(1);
            return Tuple.Create(start, end);
        }

        private Tuple<DateTime, DateTime> AllowedRange(DateTime now) => EventWindowPolicy.FullDayRange(
            IncludePastDay ? PastDays : 0,
            IncludeNextDay ? FutureDays : 0,
            now);

        private void StartScrub(double targetOffsetHours)
        {
            var current = CurrentOffsetHours();
            StopScrub();
            _displayOffsetHours = current;
            _animationFromHours = current;
            _targetOffsetHours = targetOffsetHours;
            _scrubbing = true;
            _scrubClock.Restart();
            CompositionTarget.Rendering += ScrubFrame;
            InvalidateVisual();
        }

        private double CurrentOffsetHours()
        {
            if (!_scrubbing) return _displayOffsetHours;
            var progress = Math.Min(1, _scrubClock.Elapsed.TotalMilliseconds / 110.0);
            var eased = 1 - Math.Pow(1 - progress, 3);
            return _animationFromHours + (_targetOffsetHours - _animationFromHours) * eased;
        }

        private void ScrubFrame(object sender, EventArgs e)
        {
            InvalidateVisual();
            if (_scrubClock.Elapsed.TotalMilliseconds < 110) return;
            _displayOffsetHours = _targetOffsetHours;
            StopScrub();
            InvalidateVisual();
        }

        private void StopScrub()
        {
            if (!_scrubbing) return;
            CompositionTarget.Rendering -= ScrubFrame;
            _scrubbing = false;
            _scrubClock.Stop();
        }

        private static void EventsChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        {
            var control = (DayTimelineControl)sender;
            if (control._observedEvents != null) control._observedEvents.CollectionChanged -= control.CollectionChanged;
            control._observedEvents = args.NewValue as INotifyCollectionChanged;
            if (control._observedEvents != null) control._observedEvents.CollectionChanged += control.CollectionChanged;
            control.InvalidateVisual();
        }

        private void CollectionChanged(object sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();
        private static double X(DateTime value, DateTime start, double rangeTicks, double width) => Math.Max(0, Math.Min(width, (value - start).Ticks / rangeTicks * width));
        private static SolidColorBrush BrushFrom(string value) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
        private static Brush EventBrush(string value)
        {
            try { return BrushFrom(string.IsNullOrWhiteSpace(value) ? "#FF6D7CFF" : value); }
            catch { return BrushFrom("#FF6D7CFF"); }
        }
        private static void DrawEventTitle(DrawingContext drawingContext, string value, Point origin, double pixelsPerDip)
        {
            var x = origin.X;
            var plain = new StringBuilder();
            foreach (var token in EmojiTokenizer.Tokenize(value))
            {
                if (token.Key != null && EmojiTextBlock.TryGetEmoji(token.Key, out var image))
                {
                    x += DrawEventTitleText(drawingContext, plain, new Point(x, origin.Y), pixelsPerDip);
                    const double emojiSize = 9;
                    drawingContext.DrawImage(image, new Rect(x, origin.Y + 1, emojiSize, emojiSize));
                    x += emojiSize;
                }
                else plain.Append(token.Text);
            }
            DrawEventTitleText(drawingContext, plain, new Point(x, origin.Y), pixelsPerDip);
        }
        private static double DrawEventTitleText(DrawingContext drawingContext, StringBuilder value, Point origin, double pixelsPerDip)
        {
            if (value.Length == 0) return 0;
            var text = Text(value.ToString(), 8, FontWeights.SemiBold, Brushes.White, pixelsPerDip);
            drawingContext.DrawText(text, origin);
            value.Clear();
            return text.WidthIncludingTrailingWhitespace;
        }
        private static FormattedText Text(string value, double size, FontWeight weight, Brush brush, double pixelsPerDip) =>
            new FormattedText(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, weight, FontStretches.Normal), size, brush, pixelsPerDip);
    }
}
