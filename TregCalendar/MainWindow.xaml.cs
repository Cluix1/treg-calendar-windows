using Microsoft.UI.Xaml;
using TregCalendar.Data;

namespace TregCalendar
{
    public sealed partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            _ = InitializeOfflineStoreAsync();
        }

        private async Task InitializeOfflineStoreAsync()
        {
            try
            {
                var database = new LocalCalendarDatabase();
                await database.InitializeAsync();
                OfflineStoreStatusText.Text = "Offline calendar storage is ready.";
            }
            catch (Exception)
            {
                OfflineStoreStatusText.Text = "Offline calendar storage could not be initialized.";
            }
        }
    }
}
