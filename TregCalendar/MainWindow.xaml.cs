using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Dispatching;
using System.Text.Json;
using TregCalendar.Auth;
using TregCalendar.Core;
using TregCalendar.Data;
using TregCalendar.Remote;
using TregCalendar.Sync;
using TregCalendar.UI;
using Windows.Networking.Connectivity;

namespace TregCalendar
{
    public sealed partial class MainWindow : Window
    {
        private readonly LocalCalendarDatabase _database = new();
        private readonly LocalCalendarRepository _repository;
        private readonly SupabaseAuthClient _authClient;
        private readonly CalendarSyncService _syncService;
        private readonly DispatcherQueueTimer _syncTimer;
        private IReadOnlyList<LocalCalendarEvent> _events = [];
        private DateOnly _visibleDate = DateOnly.FromDateTime(DateTime.Now);
        private CalendarViewMode _viewMode = CalendarViewMode.Month;
        private bool _autoSyncRunning;
        private bool _isBusy;

        public MainWindow()
        {
            InitializeComponent();
            EventDateInput.Date = DateTimeOffset.Now;
            EventTimeInput.Time = DateTimeOffset.Now.TimeOfDay;
            _syncTimer = DispatcherQueue.CreateTimer();
            _syncTimer.Interval = TimeSpan.FromMinutes(5);
            _syncTimer.Tick += OnSyncTimerTick;
            _repository = new LocalCalendarRepository(_database);
            _authClient = new SupabaseAuthClient(new HttpClient(), new WindowsCredentialSessionStore());
            _syncService = new CalendarSyncService(
                _database,
                _repository,
                new NativeSyncClient(new HttpClient(), _authClient));
            NetworkInformation.NetworkStatusChanged += OnNetworkStatusChanged;
            Closed += OnClosed;
            _syncTimer.Start();
            _ = InitializeOfflineStoreAsync();
        }

        private async Task InitializeOfflineStoreAsync()
        {
            try
            {
                await _database.InitializeAsync();
                OfflineStoreStatusText.Text = "Offline calendar storage is ready.";
                await RefreshAuthStatusAsync();
                await RefreshEventsAsync();
                await TryAutoSyncAsync("Startup sync");
            }
            catch (Exception)
            {
                OfflineStoreStatusText.Text = "Offline calendar storage could not be initialized.";
            }
        }

        private async void OnSignInClicked(object sender, RoutedEventArgs args)
        {
            await RunUiActionAsync(async () =>
            {
                var session = await _authClient.SignInWithPasswordAsync(EmailInput.Text, PasswordInput.Password);
                PasswordInput.Password = string.Empty;
                AuthStatusText.Text = $"Signed in as {session.Email}.";
                await RefreshEventsAsync();
                await TrySyncAfterLocalChangeAsync("Signed in");
            });
        }

        private async void OnSyncClicked(object sender, RoutedEventArgs args)
        {
            await RunUiActionAsync(async () =>
            {
                await SyncAndRefreshAsync("Sync complete");
            });
        }

        private async void OnSignOutClicked(object sender, RoutedEventArgs args)
        {
            await RunUiActionAsync(async () =>
            {
                await _authClient.SignOutAsync();
                AuthStatusText.Text = "Signed out.";
                SyncStatusText.Text = "Sync idle.";
                await RefreshEventsAsync();
            });
        }

        private async void OnRefreshClicked(object sender, RoutedEventArgs args)
        {
            await RunUiActionAsync(RefreshEventsAsync);
        }

        private void OnPreviousRangeClicked(object sender, RoutedEventArgs args)
        {
            _visibleDate = _viewMode switch
            {
                CalendarViewMode.Day => _visibleDate.AddDays(-1),
                CalendarViewMode.Week => _visibleDate.AddDays(-7),
                CalendarViewMode.Month => _visibleDate.AddMonths(-1),
                _ => _visibleDate
            };
            RefreshCalendarSurface();
        }

        private void OnTodayClicked(object sender, RoutedEventArgs args)
        {
            _visibleDate = DateOnly.FromDateTime(DateTime.Now);
            EventDateInput.Date = DateTimeOffset.Now;
            RefreshCalendarSurface();
        }

        private void OnNextRangeClicked(object sender, RoutedEventArgs args)
        {
            _visibleDate = _viewMode switch
            {
                CalendarViewMode.Day => _visibleDate.AddDays(1),
                CalendarViewMode.Week => _visibleDate.AddDays(7),
                CalendarViewMode.Month => _visibleDate.AddMonths(1),
                _ => _visibleDate
            };
            RefreshCalendarSurface();
        }

        private void OnViewModeChanged(object sender, SelectionChangedEventArgs args)
        {
            _viewMode = ViewModeInput.SelectedIndex switch
            {
                0 => CalendarViewMode.Day,
                1 => CalendarViewMode.Week,
                _ => CalendarViewMode.Month
            };
            RefreshCalendarSurface();
        }

        private void OnCalendarDaySelectionChanged(object sender, SelectionChangedEventArgs args)
        {
            if (CalendarDaysGrid.SelectedItem is not CalendarDayItem day)
            {
                return;
            }

            _visibleDate = day.Date;
            EventDateInput.Date = day.Date.ToDateTime(TimeOnly.MinValue);
            RefreshCalendarSurface();
        }

        private void OnEventSelectionChanged(object sender, SelectionChangedEventArgs args)
        {
            if (EventsList.SelectedItem is EventListItem item)
            {
                EventTitleInput.Text = item.Event.Title;
                var eventDate = item.Event.StartsAt ?? item.Event.DueAt ?? item.Event.EndsAt;
                if (eventDate is not null)
                {
                    var localDate = eventDate.Value.ToLocalTime();
                    EventDateInput.Date = localDate;
                    EventTimeInput.Time = localDate.TimeOfDay;
                }
            }
        }

        private async void OnAddEventClicked(object sender, RoutedEventArgs args)
        {
            await RunUiActionAsync(async () =>
            {
                var title = CleanTitle(EventTitleInput.Text);
                if (title.Length == 0)
                {
                    AuthStatusText.Text = "Enter an event title first.";
                    return;
                }

                var events = await _repository.GetEventsAsync();
                var calendarId = events.FirstOrDefault()?.CalendarId;
                if (calendarId is null)
                {
                    AuthStatusText.Text = "Sync once before creating a local event.";
                    return;
                }

                var startsAt = EventDateInput.Date.Date.Add(EventTimeInput.Time);
                _visibleDate = DateOnly.FromDateTime(startsAt.Date);
                var endsAt = startsAt.AddHours(1);
                var localEvent = new LocalCalendarEvent
                {
                    CalendarId = calendarId.Value,
                    Title = title,
                    StartsAt = startsAt.ToUniversalTime(),
                    EndsAt = endsAt.ToUniversalTime(),
                    DueAt = startsAt.ToUniversalTime(),
                    Status = "active"
                };

                await _repository.SaveEventWithMutationAsync(
                    localEvent,
                    PendingMutationOperation.Create,
                    JsonSerializer.Serialize(new
                    {
                        calendar_id = calendarId.Value,
                        title,
                        starts_at = startsAt.ToUniversalTime().ToString("O"),
                        ends_at = endsAt.ToUniversalTime().ToString("O"),
                        due_at = startsAt.ToUniversalTime().ToString("O"),
                        all_day = false,
                        status = "active"
                    }));

                AuthStatusText.Text = "Event saved locally. Click Sync to upload it.";
                EventTitleInput.Text = string.Empty;
                await RefreshEventsAsync();
                await TrySyncAfterLocalChangeAsync("Event saved");
            });
        }

        private async void OnSaveEventClicked(object sender, RoutedEventArgs args)
        {
            await RunUiActionAsync(async () =>
            {
                if (EventsList.SelectedItem is not EventListItem item)
                {
                    AuthStatusText.Text = "Select an event to edit.";
                    return;
                }

                if (item.Event.RemoteId is null)
                {
                    AuthStatusText.Text = "Sync this new event before editing it.";
                    return;
                }

                var title = CleanTitle(EventTitleInput.Text);
                if (title.Length == 0)
                {
                    AuthStatusText.Text = "Event title cannot be empty.";
                    return;
                }

                await _repository.SaveEventWithMutationAsync(
                    item.Event with { Title = title },
                    PendingMutationOperation.Update,
                    JsonSerializer.Serialize(new { title }));

                AuthStatusText.Text = "Event title saved locally. Click Sync to upload it.";
                await RefreshEventsAsync();
                await TrySyncAfterLocalChangeAsync("Event updated");
            });
        }

        private async void OnDeleteEventClicked(object sender, RoutedEventArgs args)
        {
            await RunUiActionAsync(async () =>
            {
                if (EventsList.SelectedItem is not EventListItem item)
                {
                    AuthStatusText.Text = "Select an event to delete.";
                    return;
                }

                if (item.Event.RemoteId is null)
                {
                    AuthStatusText.Text = "Sync this new event before deleting it.";
                    return;
                }

                await _repository.SaveEventWithMutationAsync(
                    item.Event,
                    PendingMutationOperation.Delete,
                    "{}");

                AuthStatusText.Text = "Event deleted locally. Click Sync to upload it.";
                EventTitleInput.Text = string.Empty;
                await RefreshEventsAsync();
                await TrySyncAfterLocalChangeAsync("Event deleted");
            });
        }

        private async Task RefreshAuthStatusAsync()
        {
            var session = await _authClient.GetCurrentSessionAsync();
            AuthStatusText.Text = session is null
                ? "Not signed in."
                : $"Signed in as {session.Email}.";
        }

        private async Task RefreshEventsAsync()
        {
            _events = await _repository.GetEventsAsync();
            RefreshCalendarSurface();
            await RefreshPendingStatusAsync();
        }

        private void RefreshCalendarSurface()
        {
            var days = BuildCalendarDays();
            var agendaEvents = FilterAgendaEvents()
                .Select(EventListItem.FromEvent)
                .ToArray();

            CalendarDaysGrid.ItemsSource = days;
            EventsList.ItemsSource = agendaEvents;
            RangeTitleText.Text = BuildRangeTitle();
            EventsHeadingText.Text = agendaEvents.Length == 1 ? "1 agenda item" : $"{agendaEvents.Length} agenda items";
        }

        private IReadOnlyList<CalendarDayItem> BuildCalendarDays()
        {
            var dates = VisibleDates();
            var focusedMonth = new DateOnly(_visibleDate.Year, _visibleDate.Month, 1);
            return dates
                .Select(date => CalendarDayItem.FromDate(date, _visibleDate, focusedMonth, EventsForDate(date)))
                .ToArray();
        }

        private IReadOnlyList<DateOnly> VisibleDates()
        {
            if (_viewMode == CalendarViewMode.Day)
            {
                return [_visibleDate];
            }

            if (_viewMode == CalendarViewMode.Week)
            {
                var start = StartOfWeek(_visibleDate);
                return Enumerable.Range(0, 7)
                    .Select(offset => start.AddDays(offset))
                    .ToArray();
            }

            var firstOfMonth = new DateOnly(_visibleDate.Year, _visibleDate.Month, 1);
            var monthStart = StartOfWeek(firstOfMonth);
            return Enumerable.Range(0, 42)
                .Select(offset => monthStart.AddDays(offset))
                .ToArray();
        }

        private IReadOnlyList<LocalCalendarEvent> FilterAgendaEvents()
        {
            var visibleDates = VisibleDates().ToHashSet();
            return _events
                .Where(calendarEvent =>
                {
                    var eventDate = DateForEvent(calendarEvent);
                    return eventDate is not null && visibleDates.Contains(eventDate.Value);
                })
                .OrderBy(DateForEvent)
                .ThenBy(calendarEvent => calendarEvent.Title)
                .ToArray();
        }

        private IReadOnlyList<LocalCalendarEvent> EventsForDate(DateOnly date)
        {
            return _events
                .Where(calendarEvent => DateForEvent(calendarEvent) == date)
                .OrderBy(calendarEvent => calendarEvent.StartsAt ?? calendarEvent.DueAt ?? calendarEvent.EndsAt)
                .ThenBy(calendarEvent => calendarEvent.Title)
                .ToArray();
        }

        private string BuildRangeTitle()
        {
            if (_viewMode == CalendarViewMode.Day)
            {
                return _visibleDate.ToDateTime(TimeOnly.MinValue).ToString("dddd, MMMM d, yyyy");
            }

            if (_viewMode == CalendarViewMode.Week)
            {
                var start = StartOfWeek(_visibleDate);
                var end = start.AddDays(6);
                return $"{start.ToDateTime(TimeOnly.MinValue):MMM d} - {end.ToDateTime(TimeOnly.MinValue):MMM d, yyyy}";
            }

            return _visibleDate.ToDateTime(TimeOnly.MinValue).ToString("MMMM yyyy");
        }

        private static DateOnly StartOfWeek(DateOnly date)
        {
            var offset = (int)date.DayOfWeek;
            return date.AddDays(-offset);
        }

        private static DateOnly? DateForEvent(LocalCalendarEvent calendarEvent)
        {
            var value = calendarEvent.StartsAt ?? calendarEvent.DueAt ?? calendarEvent.EndsAt;
            return value is null ? null : DateOnly.FromDateTime(value.Value.ToLocalTime().Date);
        }

        private async Task RefreshPendingStatusAsync()
        {
            var pendingCount = await _repository.CountPendingMutationsAsync();
            SyncStatusText.Text = pendingCount == 0
                ? "All local changes are synced."
                : pendingCount == 1
                    ? "1 local change is waiting to sync."
                    : $"{pendingCount} local changes are waiting to sync.";
        }

        private async Task SyncAndRefreshAsync(string label)
        {
            SyncStatusText.Text = "Syncing...";
            var result = await _syncService.SyncOnceAsync();
            AuthStatusText.Text = $"{label}. Accepted {result.AcceptedMutationCount}, applied {result.AppliedEventCount}, conflicts {result.ConflictCount}.";
            await RefreshEventsAsync();
        }

        private async Task TrySyncAfterLocalChangeAsync(string label)
        {
            try
            {
                await SyncAndRefreshAsync($"{label} and synced");
            }
            catch (Exception exception)
            {
                AuthStatusText.Text = $"{label} locally. Sync will retry later.";
                SyncStatusText.Text = $"Sync paused: {exception.Message}";
                await RefreshPendingStatusAsync();
            }
        }

        private async Task TryAutoSyncAsync(string label)
        {
            if (_isBusy || _autoSyncRunning || !HasInternetAccess())
            {
                return;
            }

            var session = await _authClient.GetCurrentSessionAsync();
            if (session is null)
            {
                return;
            }

            _autoSyncRunning = true;
            try
            {
                SyncStatusText.Text = $"{label}...";
                await SyncAndRefreshAsync(label);
            }
            catch (Exception exception)
            {
                SyncStatusText.Text = $"{label} paused: {exception.Message}";
                await RefreshPendingStatusAsync();
            }
            finally
            {
                _autoSyncRunning = false;
            }
        }

        private void OnSyncTimerTick(DispatcherQueueTimer sender, object args)
        {
            _ = TryAutoSyncAsync("Scheduled sync");
        }

        private void OnNetworkStatusChanged(object sender)
        {
            _ = DispatcherQueue.TryEnqueue(() => _ = TryAutoSyncAsync("Network sync"));
        }

        private void OnClosed(object sender, WindowEventArgs args)
        {
            _syncTimer.Stop();
            NetworkInformation.NetworkStatusChanged -= OnNetworkStatusChanged;
        }

        private static bool HasInternetAccess()
        {
            return NetworkInformation.GetInternetConnectionProfile()?.GetNetworkConnectivityLevel()
                == NetworkConnectivityLevel.InternetAccess;
        }

        private async Task RunUiActionAsync(Func<Task> action)
        {
            SetBusy(true);
            try
            {
                await action();
            }
            catch (Exception exception)
            {
                AuthStatusText.Text = exception.Message;
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void SetBusy(bool isBusy)
        {
            _isBusy = isBusy;
            SignInButton.IsEnabled = !isBusy;
            SyncButton.IsEnabled = !isBusy;
            SignOutButton.IsEnabled = !isBusy;
            RefreshButton.IsEnabled = !isBusy;
            AddEventButton.IsEnabled = !isBusy;
            SaveEventButton.IsEnabled = !isBusy;
            DeleteEventButton.IsEnabled = !isBusy;
        }

        private static string CleanTitle(string value)
        {
            return string.Join(" ", value.Split(default(string[]), StringSplitOptions.RemoveEmptyEntries)).Trim();
        }
    }
}
