#if PackAsTool
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using System.Collections.Generic;

namespace ImapCopy
{
    // a backup-archive filename editor: a textbox plus a "Browse..." button that opens the
    // platform file picker. MustExist selects which picker Browse opens: an existing-file picker
    // for Restore (the archive must already be there), or a save/new-file picker for Backup
    // (the archive doesn't exist yet).
    public partial class ArchiveFileField : UserControl
    {
        private static readonly FilePickerFileType SevenZipFileType = new FilePickerFileType("7-Zip archive")
        {
            Patterns = new[] { "*.7z" }
        };

        public ArchiveFileField()
        {
            InitializeComponent();
        }

        public string? FileName
        {
            get => FileNameBox.Text;
            set => FileNameBox.Text = value;
        }

        public bool MustExist { get; set; }

        private async void BrowseButton_Click(object? sender, RoutedEventArgs e)
        {
            IStorageProvider storageProvider = TopLevel.GetTopLevel(this)!.StorageProvider;

            if (MustExist)
            {
                IReadOnlyList<IStorageFile> files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Select backup file",
                    AllowMultiple = false,
                    FileTypeFilter = new[] { SevenZipFileType }
                });

                if (files.Count > 0)
                    FileNameBox.Text = files[0].Path.LocalPath;
            }
            else
            {
                IStorageFile? file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Save backup as",
                    SuggestedFileName = "backup.7z",
                    DefaultExtension = "7z",
                    FileTypeChoices = new[] { SevenZipFileType }
                });

                if (file != null)
                    FileNameBox.Text = file.Path.LocalPath;
            }
        }
    }
}
#endif
