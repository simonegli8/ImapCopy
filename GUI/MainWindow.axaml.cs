#if PackAsTool
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using System;
using System.IO;
using System.Threading.Tasks;

namespace ImapCopy
{
    public partial class MainWindow : Window
    {
        private bool busy;

        public MainWindow()
        {
            InitializeComponent();

            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            var settings = new Settings().Load();
            switch (settings.Command)
            {
                default:
                case Command.Copy:
                    tab.SelectedIndex = 0;
                    CopySourceField.Url = settings.Source;
                    CopyDestField.Url = settings.Destination;
                    UpdateSourceField.Url = settings.Source;
                    UpdateDestField.Url = settings.Destination;
                    BackupSourceField.Url = settings.Source;
                    RestoreDestField.Url = settings.Destination;
                    break;
                case Command.Update:
                    tab.SelectedIndex = 1;
                    CopySourceField.Url = settings.Source;
                    CopyDestField.Url = settings.Destination;
                    UpdateSourceField.Url = settings.Source;
                    UpdateDestField.Url = settings.Destination;
                    BackupSourceField.Url = settings.Source;
                    RestoreDestField.Url = settings.Destination;
                    break;
                case Command.Backup:
                    tab.SelectedIndex = 2;
                    CopySourceField.Url = settings.Source;
                    UpdateSourceField.Url = settings.Source;
                    BackupSourceField.Url = settings.Source;
                    BackupFileField.FileName = settings.Destination;
                    RestoreFileField.FileName = settings.Destination;
                    RestoreDestField.Url = settings.Source;
                    break;
                case Command.Restore:
                    tab.SelectedIndex = 3;
                    CopyDestField.Url = settings.Destination;
                    UpdateDestField.Url = settings.Destination;
                    BackupSourceField.Url = settings.Destination;
                    BackupFileField.FileName = settings.Source;
                    RestoreFileField.FileName = settings.Source;
                    RestoreDestField.Url = settings.Destination;
                    break;
            }
        }

        private async void CopyButton_Click(object? sender, RoutedEventArgs e) => await RunAsync(() =>
            ImapCopier.Copy(ParseUri(CopySourceField.Url, "Source"), ParseUri(CopyDestField.Url, "Destination"), ReportProgress));

        private async void UpdateButton_Click(object? sender, RoutedEventArgs e) => await RunAsync(() =>
            ImapCopier.Update(ParseUri(UpdateSourceField.Url, "Source"), ParseUri(UpdateDestField.Url, "Destination"), ReportProgress));

        private async void BackupButton_Click(object? sender, RoutedEventArgs e) => await RunAsync(async () =>
        {
            Uri source = ParseUri(BackupSourceField.Url, "Source");
            string path = RequireText(BackupFileField.FileName, "Backup file");

            using FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write);
            await ImapCopier.Backup(source, stream, ReportProgress);
        });

        private async void RestoreButton_Click(object? sender, RoutedEventArgs e) => await RunAsync(async () =>
        {
            string path = RequireText(RestoreFileField.FileName, "Backup file");
            Uri destination = ParseUri(RestoreDestField.Url, "Destination");

            using FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read);
            await ImapCopier.Restore(stream, destination, ReportProgress);
        });

        private async void CopySourceTestButton_Click(object? sender, RoutedEventArgs e) => await TestConnectionAsync(CopySourceField);

        private async void CopyDestTestButton_Click(object? sender, RoutedEventArgs e) => await TestConnectionAsync(CopyDestField);

        private async void UpdateSourceTestButton_Click(object? sender, RoutedEventArgs e) => await TestConnectionAsync(UpdateSourceField);

        private async void UpdateDestTestButton_Click(object? sender, RoutedEventArgs e) => await TestConnectionAsync(UpdateDestField);

        private async void BackupSourceTestButton_Click(object? sender, RoutedEventArgs e) => await TestConnectionAsync(BackupSourceField);

        private async void RestoreDestTestButton_Click(object? sender, RoutedEventArgs e) => await TestConnectionAsync(RestoreDestField);
        private async void Exit_Click(object? sender, RoutedEventArgs e) => Close();
        private Task TestConnectionAsync(ImapUrlField field) =>
            RunAsync(() => ImapCopier.TestConnection(ParseUri(field.Url, "URL")), "Connection successful.");

        private Task RunAsync(Func<Task> operation) => RunAsync(operation, "Done.");

        private async Task RunAsync(Func<Task> operation, string successMessage)
        {
            if (busy)
                return;

            busy = true;
            var settings = new Settings();
            switch (tab.SelectedIndex) {
                case 0:
                    settings.Command = Command.Copy;
                    settings.Source = CopySourceField.Url;
                    settings.Destination = CopyDestField.Url;
                    break;
                case 1:
                    settings.Command = Command.Update;
                    settings.Source = UpdateSourceField.Url;
                    settings.Destination = UpdateDestField.Url;
                    break;
                case 2:
                    settings.Command = Command.Backup;
                    settings.Source = BackupSourceField.Url;
                    settings.Destination = BackupFileField.FileName;
                    break;
                case 3:
                    settings.Command = Command.Restore;
                    settings.Source = RestoreFileField.FileName;
                    settings.Destination = RestoreDestField.Url;
                    break;
                default:
                    settings.Command = Command.None;
                    settings.Source = settings.Destination = null;
                    break;
            };
            settings.Save();

            SetControlsEnabled(false);
            ProgressBarControl.Value = 0;
            StatusText.Text = "Working...";

            try
            {
                await operation();
                StatusText.Text = successMessage;
            }
            catch (Exception ex)
            {
                StatusText.Text = "Error: " + ex.Message;
            }
            finally
            {
                busy = false;
                SetControlsEnabled(true);
            }
        }

        private void ReportProgress(double value) => Dispatcher.UIThread.Post(() => ProgressBarControl.Value = value);

        private static Uri ParseUri(string? text, string fieldName)
        {
            string value = RequireText(text, fieldName);

            try
            {
                return new Uri(value);
            }
            catch (UriFormatException)
            {
                throw new ArgumentException($"{fieldName} is not a valid imap(s):// URL.");
            }
        }

        private static string RequireText(string? text, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new ArgumentException($"{fieldName} is required.");

            return text.Trim();
        }

        private void SetControlsEnabled(bool enabled)
        {
            CopyStartButton.IsEnabled = enabled;
            UpdateStartButton.IsEnabled = enabled;
            BackupStartButton.IsEnabled = enabled;
            RestoreStartButton.IsEnabled = enabled;
            CopySourceTestButton.IsEnabled = enabled;
            CopyDestTestButton.IsEnabled = enabled;
            UpdateSourceTestButton.IsEnabled = enabled;
            UpdateDestTestButton.IsEnabled = enabled;
            BackupSourceTestButton.IsEnabled = enabled;
            RestoreDestTestButton.IsEnabled = enabled;
            CopySourceField.IsEnabled = enabled;
            CopyDestField.IsEnabled = enabled;
            UpdateSourceField.IsEnabled = enabled;
            UpdateDestField.IsEnabled = enabled;
            BackupSourceField.IsEnabled = enabled;
            RestoreDestField.IsEnabled = enabled;
            BackupFileField.IsEnabled = enabled;
            RestoreFileField.IsEnabled = enabled;
        }
    }
}
#endif
