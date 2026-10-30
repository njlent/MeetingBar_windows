using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using MeetingBar.Windows.Interop;
using MeetingBar.Windows.Models;
using MeetingBar.Windows.Services;
using MeetingBar.Windows.ViewModels;
using static MeetingBar.Localization.Localizer;

namespace MeetingBar.Windows.Views
{
    public partial class EventDetailsWindow : Window
    {
        private readonly Window _owner;
        private readonly MainViewModel _viewModel;
        private CalendarEvent _event;
        private IntPtr _handle;

        public event EventHandler SubmenuEntered;
        public event EventHandler SubmenuLeft;
        public event EventHandler ActionCompleted;

        public EventDetailsWindow(Window owner, MainViewModel viewModel)
        {
            InitializeComponent();
            _owner = owner;
            _viewModel = viewModel;
            Owner = owner;
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 0,
                GlassFrameThickness = new Thickness(-1),
                ResizeBorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(8),
                UseAeroCaptionButtons = false
            });
            SourceInitialized += Window_SourceInitialized;
        }

        public void ShowFor(CalendarEvent item, FrameworkElement anchor)
        {
            if (item == null || anchor == null) return;
            _event = item;
            DataContext = item;
            BuildRows(item);
            SummaryText.Text = CalendarEvent.FormatDate(item.Start, _viewModel.TimelineDateFormat) + "  ·  " + item.TimeRange + "  ·  " + item.DetailedCountdown;
            JoinButton.Visibility = item.CanJoin ? Visibility.Visible : Visibility.Collapsed;
            OpenLocationButton.Visibility = MapLinkService.HasPhysicalLocation(item.Location) ? Visibility.Visible : Visibility.Collapsed;
            CopyLinkButton.Visibility = item.CanJoin ? Visibility.Visible : Visibility.Collapsed;
            OpenCalendarButton.IsEnabled = _viewModel.OpenEventCommand.CanExecute(item);
            MutedIcon.Visibility = item.CanRestoreNotifications ? Visibility.Visible : Visibility.Collapsed;
            DismissButton.Visibility = item.IsDismissed || item.IsPast ? Visibility.Collapsed : Visibility.Visible;

            var work = SystemParameters.WorkArea;
            DetailsScroll.MaxHeight = Math.Max(180, work.Height - 190);
            if (!IsVisible) Show();
            UpdateLayout();
            ApplyBackdrop();

            var left = _owner.Left + _owner.ActualWidth + 6;
            if (left + ActualWidth > work.Right - 8)
            {
                _owner.Left = Math.Max(work.Left + 8, work.Right - 8 - ActualWidth - 6 - _owner.ActualWidth);
                left = _owner.Left + _owner.ActualWidth + 6;
            }
            var point = anchor.PointToScreen(new Point(0, 0));
            var dpi = VisualTreeHelper.GetDpi(_owner);
            var desiredTop = point.Y / dpi.DpiScaleY - 8;
            Left = left;
            Top = Math.Max(work.Top + 8, Math.Min(work.Bottom - ActualHeight - 8, desiredTop));
        }

        private void BuildRows(CalendarEvent item)
        {
            DetailsRows.Children.Clear();
            DetailsRows.RowDefinitions.Clear();
            AddRow(T("Date"), CalendarEvent.FormatDate(item.Start, _viewModel.TimelineDateFormat));
            AddRow(T("Time"), item.TimeRange);
            AddRow(T("Status"), item.IsCurrent ? T("Ongoing · {0}", item.Remaining) : item.DetailedCountdown);
            AddRow(T("Availability"), item.IsBusy ? T("Busy") : T("Free"));
            if (item.CanRestoreNotifications) AddRow(T("Notifications"), T("Muted"));
            if (item.IsDeclined) AddRow(T("Response"), T("Declined")); else if (item.IsTentative) AddRow(T("Response"), T("Tentative"));
            AddRow(T("Calendar"), item.CalendarName);
            AddRow(T("Organizer"), item.Organizer);
            AddRow(T("Location"), item.Location);
            AddRow(T("Service"), item.MeetingService);
            AddRow(T("Meeting link"), item.MeetingUrl);
            if (!string.Equals(item.EventUrl, item.MeetingUrl, StringComparison.OrdinalIgnoreCase)) AddRow(T("Event link"), item.EventUrl);
            AddRow(T("Notes"), item.DetailsNotes);
        }

        private void AddRow(string label, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            var row = DetailsRows.RowDefinitions.Count;
            DetailsRows.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var labelText = new TextBlock
            {
                Text = label.ToUpper(Culture),
                Foreground = (Brush)FindResource("MutedBrush"),
                FontSize = 9,
                FontWeight = FontWeights.SemiBold,
                Width = 92,
                Margin = new Thickness(0, 0, 10, 9),
                VerticalAlignment = VerticalAlignment.Top
            };
            var valueText = new TextBlock
            {
                Text = value,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 9),
                VerticalAlignment = VerticalAlignment.Top
            };
            Grid.SetRow(labelText, row);
            Grid.SetRow(valueText, row);
            Grid.SetColumn(valueText, 1);
            if (DetailsRows.ColumnDefinitions.Count == 0)
            {
                DetailsRows.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                DetailsRows.ColumnDefinitions.Add(new ColumnDefinition());
            }
            DetailsRows.Children.Add(labelText);
            DetailsRows.Children.Add(valueText);
        }

        private void Window_SourceInitialized(object sender, EventArgs e)
        {
            _handle = new WindowInteropHelper(this).Handle;
            var source = HwndSource.FromHwnd(_handle);
            if (source?.CompositionTarget != null) source.CompositionTarget.BackgroundColor = Colors.Transparent;
            ApplyBackdrop();
        }

        private void ApplyBackdrop()
        {
            if (_handle == IntPtr.Zero) return;
            var glass = NativeMethods.EnableTransientBackdrop(_handle, true);
            DetailsSurface.Background = glass ? new SolidColorBrush(Color.FromArgb(68, 32, 32, 32)) : new SolidColorBrush(Color.FromRgb(32, 32, 32));
        }

        private void Window_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e) => SubmenuEntered?.Invoke(this, EventArgs.Empty);
        private void Window_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e) => SubmenuLeft?.Invoke(this, EventArgs.Empty);
        private void Join_Click(object sender, RoutedEventArgs e) { if (_viewModel.JoinCommand.CanExecute(_event)) _viewModel.JoinCommand.Execute(_event); ActionCompleted?.Invoke(this, EventArgs.Empty); }
        private void OpenLocation_Click(object sender, RoutedEventArgs e) { if (_viewModel.OpenMapCommand.CanExecute(_event)) _viewModel.OpenMapCommand.Execute(_event); ActionCompleted?.Invoke(this, EventArgs.Empty); }
        private void CopyLink_Click(object sender, RoutedEventArgs e) { if (_viewModel.CopyMeetingLinkCommand.CanExecute(_event)) _viewModel.CopyMeetingLinkCommand.Execute(_event); }
        private void Dismiss_Click(object sender, RoutedEventArgs e) { if (_viewModel.DismissEventCommand.CanExecute(_event)) _viewModel.DismissEventCommand.Execute(_event); ActionCompleted?.Invoke(this, EventArgs.Empty); }
        private void OpenCalendar_Click(object sender, RoutedEventArgs e) { if (_viewModel.OpenEventCommand.CanExecute(_event)) _viewModel.OpenEventCommand.Execute(_event); ActionCompleted?.Invoke(this, EventArgs.Empty); }
    }
}
