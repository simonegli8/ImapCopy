#if PackAsTool
using Avalonia.Controls;
using Avalonia.Interactivity;
using System;
using System.Text;

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
            string scheme = SslCheckBox.IsChecked == true ? "imaps" : "imap";
            string user = UserBox.Text ?? "";
            string password = PasswordBox.Text ?? "";
            string host = HostBox.Text ?? "";
            string port = PortBox.Text ?? "";

            StringBuilder builder = new StringBuilder();
            builder.Append(scheme).Append("://");

            if (user.Length > 0 || password.Length > 0)
            {
                builder.Append(Uri.EscapeDataString(user));

                if (password.Length > 0)
                    builder.Append(':').Append(Uri.EscapeDataString(password));

                builder.Append('@');
            }

            builder.Append(host);

            if (port.Length > 0)
                builder.Append(':').Append(port);

            if (!string.IsNullOrEmpty(path))
                builder.Append('/').Append(path);

            return builder.ToString();
        }
    }
}
#endif
