using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Text.Json;
using TregCalendar.Auth;
using TregCalendar.Core;
using TregCalendar.Data;
using TregCalendar.Remote;
using TregCalendar.Sync;
using TregCalendar.UI;

namespace TregCalendar
{
    public sealed partial class MainWindow : Window
    {
        private readonly LocalCalendarDatabase _database = new();
        private readonly LocalCalendarRepository _repository;
        private readonly SupabaseAuthClient _authClient;
        private readonly CalendarSyncService _syncService;

        public MainWindow()
        {
            InitializeComponent();
            EventDateInput.Date = DateTimeOffset.Now;
            EventTimeInput.Time = DateTimeOffset.Now.TimeOfDay;
            _repository = new LocalCalendarRepository(_database);
            _authClient = new SupabaseAuthClient(new HttpClient(), new WindowsCredentialSessionStore());
            _syncService = new CalendarSyncService(
                _database,
                _repository,
                new NativeSyncClient(new HttpClient(), _authClient));
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
            });
        }

        private async void OnSyncClicked(object sender, RoutedEventArgs args)
        {
            await RunUiActionAsync(async () =>
            {
                var result = await _syncService.SyncOnceAsync();
                AuthStatusText.Text = $"Sync complete. Accepted {result.AcceptedMutationCount}, applied {result.AppliedEventCount}, conflicts {result.ConflictCount}.";
                await RefreshEventsAsync();
            });
        }

        private async void OnSignOutClicked(object sender, RoutedEventArgs args)
        {
            await RunUiActionAsync(async () =>
            {
                await _authClient.SignOutAsync();
                AuthStatusText.Text = "Signed out.";
                await RefreshEventsAsync();
            });
        }

        private async void OnRefreshClicked(object sender, RoutedEventArgs args)
        {
            await RunUiActionAsync(RefreshEventsAsync);
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
            var events = await _repository.GetEventsAsync();
            var items = events
                .Select(EventListItem.FromEvent)
                .ToArray();

            EventsList.ItemsSource = items;
            EventsHeadingText.Text = items.Length == 1 ? "1 event" : $"{items.Length} events";
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
