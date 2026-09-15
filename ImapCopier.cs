using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using MimeKit;
using Newtonsoft.Json;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using SharpCompress.Writers;
using SharpCompress.Writers.SevenZip;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ImapCopy;

public class ImapCopier
{
    // uri format: imap(s)://user:password@host:port/optional/folder/path
    // progress is invoked with the fraction (0.0-1.0) of messages processed across all copied folders
    public static Task Copy(Uri source, Uri destination, Action<double>? progress = null) => CopyMailboxAsync(source, destination, false, progress);

    // like Copy, but also syncs flags (\Seen, \Flagged, ...) of messages that already exist at the destination,
    // and deletes destination messages (matched by Message-Id) that no longer exist in the source folder
    public static Task Update(Uri source, Uri destination, Action<double>? progress = null) => CopyMailboxAsync(source, destination, true, progress);

    // checks that a uri is a well-formed imap(s) url, connects to the host and authenticates with
    // the credentials in the url, then disconnects; throws (ArgumentException, MailKit's
    // AuthenticationException, SocketException, ...) if any of that fails
    public static async Task TestConnection(Uri uri)
    {
        if (uri == null)
            throw new ArgumentNullException(nameof(uri));

        if (!string.Equals(uri.Scheme, "imap", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, "imaps", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"'{uri.Scheme}' is not a valid imap(s) url scheme.", nameof(uri));

        (string? username, _) = ParseCredentials(uri);
        if (string.IsNullOrEmpty(username))
            throw new ArgumentException("The url must include a username and password to test authentication.", nameof(uri));

        using ImapClient client = new ImapClient();

        await ConnectAsync(client, uri).ConfigureAwait(false);
        await client.DisconnectAsync(true).ConfigureAwait(false);
    }

    // builds an imap(s) url from its parts; port <= 0 omits the port (the default for the scheme is
    // used when connecting), and user/password may be null for an anonymous connection
    public static Uri ImapUrl(string host, int port, bool ssl, string? user = null, string? password = null)
    {
        if (string.IsNullOrWhiteSpace(host))
            throw new ArgumentException("Host is required.", nameof(host));

        StringBuilder builder = new StringBuilder();
        builder.Append(ssl ? "imaps" : "imap").Append("://");

        if (!string.IsNullOrEmpty(user))
        {
            builder.Append(Uri.EscapeDataString(user));

            if (!string.IsNullOrEmpty(password))
                builder.Append(':').Append(Uri.EscapeDataString(password));

            builder.Append('@');
        }

        builder.Append(host);

        if (port > 0)
            builder.Append(':').Append(port);

        return new Uri(builder.ToString());
    }

    // writes every message under the source mailbox (or a single folder/subtree, if a path is given in the uri)
    // as a .eml entry in a 7z archive, one top-level directory per mailbox folder; destination is left open
    // (must be a seekable stream: 7z back-patches its header once the archive is finalized)
    public static async Task Backup(Uri source, Stream destination, Action<double>? progress = null)
    {
        if (destination == null)
            throw new ArgumentNullException(nameof(destination));

        using ImapClient sourceClient = new ImapClient();
        await ConnectAsync(sourceClient, source).ConfigureAwait(false);

        try
        {
            string sourcePath = GetFolderPath(source);

            (IMailFolder? rootFolder, List<IMailFolder> folders) =
                await ResolveFoldersAsync(sourceClient, sourcePath).ConfigureAwait(false);

            int totalMessages = progress != null ? await CountMessagesAsync(folders).ConfigureAwait(false) : 0;
            int processedMessages = 0;

            SevenZipWriterOptions writerOptions = new SevenZipWriterOptions {
                LeaveStreamOpen = true,
                CompressionType = CompressionType.LZMA2,
                CompressionLevel = 9
            };

            using (IWriter writer = WriterFactory.OpenWriter(destination, ArchiveType.SevenZip, writerOptions))
            {
                foreach (IMailFolder folder in folders)
                {
                    string folderPath = CombineFolderPath(string.Empty, GetRelativeFolderPath(folder, rootFolder), folder.Name);

                    await folder.OpenAsync(FolderAccess.ReadOnly).ConfigureAwait(false);

                    try
                    {
                        if (folder.Count == 0)
                            continue;

                        IList<UniqueId> uids = await folder.SearchAsync(SearchQuery.All).ConfigureAwait(false);
                        IList<IMessageSummary> summaries = await folder.FetchAsync(uids, MessageSummaryItems.Flags).ConfigureAwait(false);
                        Dictionary<UniqueId, IMessageSummary> summaryByUid = summaries.ToDictionary(s => s.UniqueId);

                        HashSet<string> usedEntryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        Dictionary<string, string[]> flagsByEntryName = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

                        foreach (UniqueId uid in uids)
                        {
                            MimeMessage message = await folder.GetMessageAsync(uid).ConfigureAwait(false);

                            // Uri.EscapeDataString both makes the name filesystem-safe (it percent-encodes
                            // '/', ':', control characters, ...) and leaves '.', '-', '_', '~' untouched, so
                            // the date/sender/subject parts stay readable in the resulting entry name
                            string dateTime = message.Date.UtcDateTime.ToString("yyyy-MM-dd__HHmmss");
                            string sender = message.From.Mailboxes.FirstOrDefault()?.Name ?? message.Sender?.Name ?? "unknown";
                            string subject = message.Subject ?? string.Empty;

                            string entryName = Uri.EscapeDataString($"{dateTime}__{sender.Replace(' ', '-')}__{subject.Replace(' ', '-')}")
                                .Replace("__"," ");

                            // disambiguate the (rare) case of two messages with the same date, sender and subject
                            if (!usedEntryNames.Add(entryName))
                                entryName += "-" + uid.Id;

                            // the writer reads its source stream synchronously, so the message is serialized on a
                            // background task through a pipe rather than buffered into memory in full first
                            Pipe pipe = new Pipe();

                            Task writeTask = Task.Run(async () =>
                            {
                                Exception? writeError = null;

                                try
                                {
                                    await message.WriteToAsync(pipe.Writer.AsStream()).ConfigureAwait(false);
                                }
                                catch (Exception ex)
                                {
                                    writeError = ex;
                                }
                                finally
                                {
                                    await pipe.Writer.CompleteAsync(writeError).ConfigureAwait(false);
                                }
                            });

                            using (Stream messageStream = pipe.Reader.AsStream())
                                writer.Write($"{folderPath}/{entryName}.eml", messageStream, message.Date.UtcDateTime);

                            await writeTask.ConfigureAwait(false);

                            IMessageSummary? summary = summaryByUid.TryGetValue(uid, out IMessageSummary? s) ? s : null;
                            MessageFlags flags = (summary?.Flags ?? MessageFlags.None) & ~MessageFlags.Recent;
                            flagsByEntryName[entryName] = ToFlagStrings(flags, summary?.Keywords!);

                            if (totalMessages > 0)
                                progress?.Invoke((double)++processedMessages / totalMessages);
                        }

                        // per-folder sidecar mapping each entry name to its IMAP flags (system flags prefixed with
                        // '\', same as the IMAP wire format, custom keywords bare), so Restore can reapply them
                        string flagsJson = JsonConvert.SerializeObject(flagsByEntryName, Formatting.Indented);

                        using (MemoryStream flagsStream = new MemoryStream(Encoding.UTF8.GetBytes(flagsJson)))
                            writer.Write($"{folderPath}/flags.json", flagsStream, null);
                    }
                    finally
                    {
                        await folder.CloseAsync().ConfigureAwait(false);
                    }
                }
            }

            if (progress != null && totalMessages == 0)
                progress(1.0);
        }
        finally
        {
            if (sourceClient.IsConnected)
                await sourceClient.DisconnectAsync(true).ConfigureAwait(false);
        }

        await destination.FlushAsync().ConfigureAwait(false);
    }

    // writes every message under the source mailbox (or a single folder/subtree, if a path is given in the uri)
    // as a .eml entry in a 7z archive, one top-level directory per mailbox folder; destination is the path to
    // a 7zip archive
    public static async Task Backup(Uri source, string destination, Action<double>? progress = null)
    {
        using var file = new FileStream(destination, FileMode.Create, FileAccess.Write);
        await Backup(source, file, progress);
    }

    // restores a 7z archive produced by Backup; existing messages (matched by Message-Id) are skipped,
    // so restoring the same archive twice does not create duplicates. source is left open
    public static async Task Restore(Stream source, Uri destination, Action<double>? progress = null)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));

        using ImapClient destinationClient = new ImapClient();
        await ConnectAsync(destinationClient, destination).ConfigureAwait(false);

        try
        {
            string destinationBasePath = GetFolderPath(destination);

            using IArchive archive = ArchiveFactory.OpenArchive(source, ReaderOptions.ForExternalStream);

            List<IArchiveEntry> allEntries = archive.Entries
                .Where(e => !e.IsDirectory && e.Size > 0 && !string.IsNullOrEmpty(e.Key) && e.Key.Contains('/'))
                .ToList();

            // each folder's flags.json sidecar is metadata, not a message, so it is tracked separately
            Dictionary<string, IArchiveEntry> flagsEntryByFolder = allEntries
                .Where(e => e.Key != null && string.Equals(Path.GetFileName(e.Key), "flags.json", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(e => e.Key!.Substring(0, e.Key.LastIndexOf('/')), e => e);

            // group the flat list of "<folder path>/<entry name>.eml" entries by their folder path
            List<IGrouping<string, IArchiveEntry>> entriesByFolder = allEntries
                .Where(e => e.Key != null && !string.Equals(Path.GetFileName(e.Key), "flags.json", StringComparison.OrdinalIgnoreCase))
                .GroupBy(e => e.Key!.Substring(0, e.Key.LastIndexOf('/')))
                .ToList();

            int totalMessages = progress != null ? entriesByFolder.Sum(g => g.Count()) : 0;
            int processedMessages = 0;

            foreach (IGrouping<string, IArchiveEntry> group in entriesByFolder)
            {
                string destinationPath = string.IsNullOrEmpty(destinationBasePath)
                    ? group.Key
                    : destinationBasePath + "/" + group.Key;

                IMailFolder? folder = await GetOrCreateFolderAsync(destinationClient, destinationPath).ConfigureAwait(false);
                if (folder == null) throw new Exception($"Could not create IMAP folder {destinationPath}");
                await folder.OpenAsync(FolderAccess.ReadWrite).ConfigureAwait(false);

                try
                {
                    // messages are matched by Message-Id so restoring the same backup twice is a no-op
                    Dictionary<string, UniqueId> existingByMessageId = new Dictionary<string, UniqueId>(StringComparer.OrdinalIgnoreCase);

                    if (folder.Count > 0)
                    {
                        IList<UniqueId> existingUids = await folder.SearchAsync(SearchQuery.All).ConfigureAwait(false);
                        IList<IMessageSummary> existingSummaries = await folder.FetchAsync(
                            existingUids, MessageSummaryItems.Envelope).ConfigureAwait(false);

                        foreach (IMessageSummary summary in existingSummaries)
                        {
                            string? messageId = summary.Envelope?.MessageId;
                            if (!string.IsNullOrEmpty(messageId))
                                existingByMessageId[messageId] = summary.UniqueId;
                        }
                    }

                    Dictionary<string, string[]> flagsByEntryName = flagsEntryByFolder.TryGetValue(group.Key, out IArchiveEntry? flagsEntry)
                        ? await LoadFlagsAsync(flagsEntry).ConfigureAwait(false)
                        : new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

                    foreach (IArchiveEntry entry in group)
                    {
                        MimeMessage message;
                        using (Stream entryStream = entry.OpenEntryStream())
                            message = await MimeMessage.LoadAsync(entryStream).ConfigureAwait(false);

                        if (string.IsNullOrEmpty(message.MessageId) || !existingByMessageId.ContainsKey(message.MessageId))
                        {
                            string entryName = Path.GetFileNameWithoutExtension(entry.Key ?? "");
                            string[]? rawFlags = flagsByEntryName.TryGetValue(entryName, out string[]? f) ? f : null;
                            (MessageFlags flags, HashSet<string> keywords) = ParseFlagStrings(rawFlags);

                            IAppendRequest request = new AppendRequest(message, flags, keywords, message.Date);
                            await folder.AppendAsync(request).ConfigureAwait(false);
                        }

                        if (totalMessages > 0)
                            progress?.Invoke((double)++processedMessages / totalMessages);
                    }
                }
                finally
                {
                    await folder.CloseAsync().ConfigureAwait(false);
                }
            }

            if (progress != null && totalMessages == 0)
                progress(1.0);
        }
        finally
        {
            if (destinationClient.IsConnected)
                await destinationClient.DisconnectAsync(true).ConfigureAwait(false);
        }
    }
    // restores a 7z archive produced by Backup; existing messages (matched by Message-Id) are skipped,
    // so restoring the same archive twice does not create duplicates. source is the path to
    // a 7zip archive
    public static async Task Restore(string source, Uri destination, Action<double>? progress = null)
    {
        using var file = new FileStream(source, FileMode.Open, FileAccess.Read);
        await Restore(file, destination, progress);
    }

    private static async Task<(IMailFolder? rootFolder, List<IMailFolder> folders)> ResolveFoldersAsync(ImapClient client, string path)
    {
        IList<IMailFolder> allFolders = await client.GetFoldersAsync(client.PersonalNamespaces[0]).ConfigureAwait(false);

        IMailFolder? rootFolder = null;
        IEnumerable<IMailFolder> candidates = allFolders;

        if (!string.IsNullOrEmpty(path))
        {
            rootFolder = await client.GetFolderAsync(path).ConfigureAwait(false);
            string prefix = rootFolder.FullName + rootFolder.DirectorySeparator;

            candidates = allFolders.Where(f =>
                f.FullName == rootFolder.FullName || f.FullName.StartsWith(prefix, StringComparison.Ordinal));
        }

        List<IMailFolder> folders = candidates
            .Where(f => (f.Attributes & (FolderAttributes.NonExistent | FolderAttributes.NoSelect)) == 0)
            .ToList();

        return (rootFolder, folders);
    }

    // flags that are OR'd with the destination's existing value during Update rather than overwritten
    private const MessageFlags OrMergedFlags = MessageFlags.Seen | MessageFlags.Answered | MessageFlags.Flagged | MessageFlags.Deleted | MessageFlags.Draft;

    // the flags a backup round-trips; \Recent is excluded since it cannot be set through STORE/APPEND
    private static readonly (MessageFlags Flag, string Name)[] KnownFlags =
    {
        (MessageFlags.Seen, "Seen"),
        (MessageFlags.Answered, "Answered"),
        (MessageFlags.Flagged, "Flagged"),
        (MessageFlags.Deleted, "Deleted"),
        (MessageFlags.Draft, "Draft"),
    };

    // system flags are written with their IMAP-wire '\' prefix (e.g. "\Seen"); anything without a
    // leading '\' is a custom keyword (Gmail labels, "$Forwarded", "NonJunk", ...) and passed through as-is
    private static string[] ToFlagStrings(MessageFlags flags, IEnumerable<string> keywords)
    {
        List<string> values = KnownFlags.Where(kf => (flags & kf.Flag) != 0).Select(kf => "\\" + kf.Name).ToList();

        if (keywords != null)
            values.AddRange(keywords);

        return values.ToArray();
    }

    private static (MessageFlags flags, HashSet<string> keywords) ParseFlagStrings(IEnumerable<string>? values)
    {
        MessageFlags flags = MessageFlags.None;
        HashSet<string> keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (values == null)
            return (flags, keywords);

        foreach (string value in values)
        {
            if (value.Length > 0 && value[0] == '\\')
            {
                string name = value.Substring(1);
                foreach ((MessageFlags Flag, string Name) knownFlag in KnownFlags)
                {
                    if (string.Equals(knownFlag.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        flags |= knownFlag.Flag;
                        break;
                    }
                }
            }
            else
            {
                keywords.Add(value);
            }
        }

        return (flags, keywords);
    }

    private static async Task<Dictionary<string, string[]>> LoadFlagsAsync(IArchiveEntry entry)
    {
        string json;
        using (Stream stream = entry.OpenEntryStream())
        using (StreamReader reader = new StreamReader(stream))
            json = await reader.ReadToEndAsync().ConfigureAwait(false);

        Dictionary<string, string[]>? raw = JsonConvert.DeserializeObject<Dictionary<string, string[]>>(json);
        return raw != null
            ? new Dictionary<string, string[]>(raw, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<int> CountMessagesAsync(IEnumerable<IMailFolder> folders)
    {
        int total = 0;

        foreach (IMailFolder folder in folders)
        {
            await folder.StatusAsync(StatusItems.Count).ConfigureAwait(false);
            total += folder.Count;
        }

        return total;
    }

    private static async Task CopyMailboxAsync(Uri source, Uri destination, bool update, Action<double>? progress = null)
    {
        using ImapClient sourceClient = new ImapClient();
        using ImapClient destinationClient = new ImapClient();

        await ConnectAsync(sourceClient, source).ConfigureAwait(false);
        await ConnectAsync(destinationClient, destination).ConfigureAwait(false);

        try
        {
            string sourcePath = GetFolderPath(source);
            string destinationBasePath = GetFolderPath(destination);

            (IMailFolder? rootFolder, List<IMailFolder> foldersToCopy) =
                await ResolveFoldersAsync(sourceClient, sourcePath).ConfigureAwait(false);

            // pre-count messages (via STATUS, without opening each folder) so progress can be reported as a fraction
            int totalMessages = progress != null ? await CountMessagesAsync(foldersToCopy).ConfigureAwait(false) : 0;
            Action? onMessageProcessed = null;

            if (progress != null && totalMessages > 0)
            {
                int processedMessages = 0;
                onMessageProcessed = () => progress((double)++processedMessages / totalMessages);
            }

            foreach (IMailFolder sourceFolder in foldersToCopy)
            {
                string relativePath = GetRelativeFolderPath(sourceFolder, rootFolder);
                string destinationPath = CombineFolderPath(destinationBasePath, relativePath, sourceFolder.Name);

                IMailFolder? destinationFolder = await GetOrCreateFolderAsync(destinationClient, destinationPath)
                    .ConfigureAwait(false);
                if (destinationFolder == null) throw new Exception($"Could not create IMAP folder {destinationPath}");

                await CopyFolderAsync(sourceFolder, destinationFolder, update, onMessageProcessed).ConfigureAwait(false);
            }

            if (progress != null && totalMessages == 0)
                progress(1.0);
        }
        finally
        {
            if (sourceClient.IsConnected)
                await sourceClient.DisconnectAsync(true).ConfigureAwait(false);
            if (destinationClient.IsConnected)
                await destinationClient.DisconnectAsync(true).ConfigureAwait(false);
        }
    }

    private static async Task CopyFolderAsync(IMailFolder sourceFolder, IMailFolder destinationFolder, bool update, Action? onMessageProcessed = null)
    {
        await sourceFolder.OpenAsync(FolderAccess.ReadOnly).ConfigureAwait(false);
        await destinationFolder.OpenAsync(FolderAccess.ReadWrite).ConfigureAwait(false);

        try
        {
            // messages are matched across the two servers by Message-Id, since UIDs are server-specific
            Dictionary<string, UniqueId> existingByMessageId = new Dictionary<string, UniqueId>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, MessageFlags> existingFlagsByMessageId = new Dictionary<string, MessageFlags>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, HashSet<string>> existingKeywordsByMessageId = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

            if (destinationFolder.Count > 0)
            {
                IList<UniqueId> destinationUids = await destinationFolder.SearchAsync(SearchQuery.All).ConfigureAwait(false);
                IList<IMessageSummary> destinationSummaries = await destinationFolder.FetchAsync(
                    destinationUids, MessageSummaryItems.Envelope | MessageSummaryItems.Flags).ConfigureAwait(false);

                foreach (IMessageSummary summary in destinationSummaries)
                {
                    string? messageId = summary.Envelope?.MessageId;
                    if (!string.IsNullOrEmpty(messageId))
                    {
                        existingByMessageId[messageId] = summary.UniqueId;
                        existingFlagsByMessageId[messageId] = summary.Flags ?? MessageFlags.None;
                        existingKeywordsByMessageId[messageId] = new HashSet<string>(
                            summary.Keywords ?? (IEnumerable<string>)Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
                    }
                }
            }

            HashSet<string> sourceMessageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (sourceFolder.Count > 0)
            {
                IList<UniqueId> sourceUids = await sourceFolder.SearchAsync(SearchQuery.All).ConfigureAwait(false);
                IList<IMessageSummary> sourceSummaries = await sourceFolder.FetchAsync(
                    sourceUids, MessageSummaryItems.Envelope | MessageSummaryItems.Flags | MessageSummaryItems.InternalDate)
                    .ConfigureAwait(false);

                foreach (IMessageSummary summary in sourceSummaries)
                {
                    string? messageId = summary.Envelope?.MessageId;
                    if (!string.IsNullOrEmpty(messageId))
                        sourceMessageIds.Add(messageId);

                    // \Recent cannot be set through STORE, so it is stripped before appending/syncing
                    MessageFlags flags = (summary.Flags ?? MessageFlags.None) & ~MessageFlags.Recent;

                    if (!string.IsNullOrEmpty(messageId) && existingByMessageId.TryGetValue(messageId, out UniqueId destinationUid))
                    {
                        if (update)
                        {
                            // \Seen, \Answered, \Flagged and \Deleted are OR'd with the destination's current
                            // value rather than overwritten, so actions already taken at the destination aren't undone
                            MessageFlags destinationFlags = existingFlagsByMessageId.TryGetValue(messageId, out MessageFlags df)
                                ? df : MessageFlags.None;
                            MessageFlags mergedFlags = flags | (destinationFlags & OrMergedFlags);

                            // keywords (custom flags / Gmail labels) are unioned rather than overwritten, for the same reason
                            HashSet<string> keywords = new HashSet<string>(
                                summary.Keywords ?? (IEnumerable<string>)Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
                            if (existingKeywordsByMessageId.TryGetValue(messageId, out HashSet<string>? destinationKeywords))
                                keywords.UnionWith(destinationKeywords);

                            await destinationFolder.SetFlagsAsync(destinationUid, mergedFlags, keywords, true).ConfigureAwait(false);
                        }

                        onMessageProcessed?.Invoke();
                        continue;
                    }

                    MimeMessage message = await sourceFolder.GetMessageAsync(summary.UniqueId).ConfigureAwait(false);
                    IAppendRequest request = new AppendRequest(message, flags, summary.Keywords!, summary.InternalDate ?? DateTimeOffset.Now);
                    await destinationFolder.AppendAsync(request).ConfigureAwait(false);

                    onMessageProcessed?.Invoke();
                }
            }

            if (update)
            {
                // mirror deletions: a previously-copied message (identified by Message-Id) that is no
                // longer present in the source folder is removed from the destination as well
                List<UniqueId> uidsToDelete = existingByMessageId
                    .Where(pair => !sourceMessageIds.Contains(pair.Key))
                    .Select(pair => pair.Value)
                    .ToList();

                if (uidsToDelete.Count > 0)
                {
                    await destinationFolder.AddFlagsAsync(uidsToDelete, MessageFlags.Deleted, true).ConfigureAwait(false);
                    await destinationFolder.ExpungeAsync(uidsToDelete).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            await sourceFolder.CloseAsync().ConfigureAwait(false);
            await destinationFolder.CloseAsync().ConfigureAwait(false);
        }
    }

    private static async Task ConnectAsync(ImapClient client, Uri uri)
    {
        if (uri == null)
            throw new ArgumentNullException(nameof(uri));

        bool implicitTls = string.Equals(uri.Scheme, "imaps", StringComparison.OrdinalIgnoreCase);
        int port = uri.Port > 0 ? uri.Port : (implicitTls ? 993 : 143);
        SecureSocketOptions sslOptions = implicitTls ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable;

        await client.ConnectAsync(uri.Host, port, sslOptions).ConfigureAwait(false);

        (string? username, string? password) = ParseCredentials(uri);
        if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
            await client.AuthenticateAsync(username, password).ConfigureAwait(false);
    }

    private static (string? username, string? password) ParseCredentials(Uri uri)
    {
        string userInfo = uri.UserInfo;
        if (string.IsNullOrEmpty(userInfo))
            return (null, null);

        int index = userInfo.IndexOf(':');
        string username = index < 0 ? userInfo : userInfo.Substring(0, index);
        string password = index < 0 ? string.Empty : userInfo.Substring(index + 1);

        return (Uri.UnescapeDataString(username), Uri.UnescapeDataString(password));
    }

    private static string GetFolderPath(Uri uri)
    {
        string path = uri.AbsolutePath.Trim('/');
        return string.IsNullOrEmpty(path) ? string.Empty : Uri.UnescapeDataString(path);
    }

    // folder paths are compared/combined using '/' as a generic separator; MailKit maps '/' to
    // whatever separator each server actually uses when resolving a path via GetFolder(Async)
    private static string GetRelativeFolderPath(IMailFolder folder, IMailFolder? root)
    {
        if (root == null)
            return NormalizeSeparator(folder.FullName, folder.DirectorySeparator);

        if (folder.FullName == root.FullName)
            return string.Empty;

        string prefix = root.FullName + root.DirectorySeparator;
        return NormalizeSeparator(folder.FullName.Substring(prefix.Length), folder.DirectorySeparator);
    }

    private static string NormalizeSeparator(string path, char separator)
    {
        return separator == '/' ? path : path.Replace(separator, '/');
    }

    private static string CombineFolderPath(string basePath, string relativePath, string rootFolderName)
    {
        if (string.IsNullOrEmpty(relativePath))
            return string.IsNullOrEmpty(basePath) ? rootFolderName : basePath;

        return string.IsNullOrEmpty(basePath) ? relativePath : basePath + "/" + relativePath;
    }

    private static async Task<IMailFolder?> GetOrCreateFolderAsync(ImapClient client, string path)
    {
        try
        {
            return await client.GetFolderAsync(path).ConfigureAwait(false);
        }
        catch (FolderNotFoundException)
        {
            // fall through and create the missing folder (and any missing parents) below
        }

        string[] segments = path.Split('/');
        IMailFolder parent = client.GetFolder(client.PersonalNamespaces[0]);
        IMailFolder? folder = null;
        string? currentPath = null;

        foreach (string segment in segments)
        {
            currentPath = currentPath == null ? segment : currentPath + "/" + segment;

            try
            {
                folder = await client.GetFolderAsync(currentPath).ConfigureAwait(false);
            }
            catch (FolderNotFoundException)
            {
                folder = await parent.CreateAsync(segment, true).ConfigureAwait(false);
            }

            if (folder == null) break;

            parent = folder;
        }

        return folder;
    }
}
