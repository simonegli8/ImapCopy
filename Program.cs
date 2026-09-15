#if PackAsTool && NETCOREAPP
using Avalonia;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace ImapCopy;

internal class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    //
    // Main must NOT return a Task (async or not): a Task-returning entry point breaks
    // COM-dependent Windows features (drag&drop, file dialogs) even with [STAThread] set,
    // causing "CoInitialize has not been called" (CO_E_NOTINITIALIZED) at runtime.
    // See https://github.com/AvaloniaUI/Avalonia/issues/13694
    [STAThread]
    public static void Main(string[] args)
    {
        if (GUIToolInstaller.Installer.Run(args, "ImapCopy", "sync-envelope",
               "ImapCopy, a tool to copy or backup IMAP mailboxes.")) return;

        if (args.Length > 0)
            RunCli(args).GetAwaiter().GetResult();
        else
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
    }

    private static async Task RunCli(string[] args)
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        var title = $"ImapCopy, v{version!.ToString(3)}";
        Console.WriteLine(title);

        var op = args?.FirstOrDefault()?.ToLower();
        var src = args?.Skip(1).FirstOrDefault();
        var dest = args?.Skip(2).FirstOrDefault();
        if (src == null)
        {
            ShowHelp();
            Environment.Exit(-1);
        }
        if (dest == null)
        {
            ShowHelp();
            Environment.Exit(-2);
        }
        var form = new ConsoleForm(@$"{title}

Source: {src}
Destination: {dest}

Progress: [%Progress                                                 ]");
        Action<double> report = (double progress) => ((PercentField)form["Progress"]).Value = (float)progress;
        try
        {
            switch (op)
            {
                case "copy":
                    form.Show();
                    await ImapCopier.Copy(new Uri(src), new Uri(dest), report);
                    break;
                case "update":
                    form.Show();
                    await ImapCopier.Update(new Uri(src), new Uri(dest), report);
                    break;
                case "backup":
                    form.Show();
                    using (var stream = new FileStream(dest, FileMode.Create, FileAccess.Write))
                    {
                        await ImapCopier.Backup(new Uri(src), stream, report);
                    }
                    break;
                case "restore":
                    form.Show();
                    using (var stream = new FileStream(src, FileMode.Open, FileAccess.Read))
                    {
                        await ImapCopier.Restore(stream, new Uri(dest), report);
                    }
                    break;
                default:
                    ShowHelp();
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
        }
    }
    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();

    private static void ShowHelp()
    {
        Console.WriteLine(@"
Usage: imapcopy <command> [source] [destination]

<command> is one of copy, update, backup, restore, install, uninstall or help.
[source] and [destination] are either an imap(s) url or a file path.

Commands:

copy: Copies all mails from a source mailbox to a destination mailbox.
update: Like copy but updates mail flags and deletes mails from destination
             not present in source.
backup: Creates a backup archive of the source mailbox.
restore: Restores the backup archive to the destination mailbox.
install: Installs the app to the system's program menu.
uninstall: Removes the app from the system's program menu.
help: Shows this help text.
");
    }
}
#endif