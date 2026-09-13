#if PackAsTool
using Avalonia.Controls;
using Avalonia.Interactivity;
using System;

namespace ImapCopy
{
    // a reusable "imap(s) url" editor: a raw url textbox plus a collapsible panel that edits its
    // user, password, host, port and ssl parts individually; the two stay in sync in both directions
    public partial class ImapUrlField : UserControl
    {
        private bool syncing;
        private string? path;

        public ImapUrlField()
        {
            InitializeComponent();
        }

        public string? Label
        {
            get => LabelText.Text;
            set => LabelText.Text = value;
        }

        public string? Url
        {
            get => UrlBox.Text;
            set => UrlBox.Text = value;
        }

        private void UrlBox_TextChanged(object? sender, TextChangedEventArgs e)
        {
            if (syncing)
                return;

            syncing = true;
            try
            {
                PopulateFieldsFromUrl(UrlBox.Text);
            }
            finally
            {
                syncing = false;
            }
        }

        private void TextField_Changed(object? sender, TextChangedEventArgs e) => OnFieldChanged();

        private void SslCheckBox_Changed(object? sender, RoutedEventArgs e) => OnFieldChanged();

        private void OnFieldChanged()
        {
            if (syncing)
                return;

            syncing = true;
            try
            {
                UrlBox.Text = ComposeUrlFromFields();
            }
            finally
            {
                syncing = false;
            }
        }

        private void PopulateFieldsFromUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url) ||
                !Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ||
                (!string.Equals(uri.Scheme, "imap", StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(uri.Scheme, "imaps", StringComparison.OrdinalIgnoreCase)))
                return;

            SslCheckBox.IsChecked = string.Equals(uri.Scheme, "imaps", StringComparison.OrdinalIgnoreCase);
            HostBox.Text = uri.Host;
            PortBox.Text = uri.Port > 0 ? uri.Port.ToString() : "";

            string userInfo = uri.UserInfo;
            if (string.IsNullOrEmpty(userInfo))
            {
                UserBox.Text = "";
                PasswordBox.Text = "";
            }
            else
            {
                int index = userInfo.IndexOf(':');
                string user = index < 0 ? userInfo : userInfo.Substring(0, index);
                string password = index < 0 ? "" : userInfo.Substring(index + 1);

                UserBox.Text = Uri.UnescapeDataString(user);
                PasswordBox.Text = Uri.UnescapeDataString(password);
            }

            path = uri.AbsolutePath.Trim('/');
        }

        private string ComposeUrlFromFields()
        {
            bool ssl = SslCheckBox.IsChecked == true;
            string scheme = ssl ? "imaps" : "imap";
            string host = HostBox.Text ?? "";

            // host is required to build a real Uri; while it's still empty (e.g. the user hasn't
            // typed it yet) fall back to just the scheme so the raw url box doesn't throw mid-edit
            if (string.IsNullOrWhiteSpace(host))
                return scheme + "://";

            int.TryParse(PortBox.Text, out int port);

            try
            {
                // OriginalString (not ToString()) is what was actually passed to the Uri
                // constructor, so it round-trips exactly, including percent-encoded credentials
                string url = ImapCopier.ImapUrl(host, port, ssl, UserBox.Text, PasswordBox.Text).OriginalString;
                return string.IsNullOrEmpty(path) ? url : url + "/" + path;
            }
            catch (UriFormatException)
            {
                // host isn't a valid uri authority yet (e.g. mid-edit, contains a stray space);
                // show a best-effort string instead of throwing on every keystroke
                string portText = PortBox.Text;
                return $"{scheme}://{host}{(string.IsNullOrEmpty(portText) ? "" : ":" + portText)}";
            }
        }
    }
}
#endif
