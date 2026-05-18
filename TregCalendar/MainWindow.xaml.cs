using Microsoft.UI.Xaml;
using TregCalendar.Auth;
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
        }
    }
}
