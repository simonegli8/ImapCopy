#if PackAsTool
using Avalonia;
using Avalonia.Media.TextFormatting.Unicode;
using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace ImapCopy
{
    internal class Program
    {
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args)
        {
            if (args.Length > 0)
            {
                var version = Assembly.GetExecutingAssembly().GetName().Version;
                var title = $"ImapCopy, v{version.ToString(3)}";
                Console.WriteLine(title);
                var op = args.FirstOrDefault().ToLower();
                var src = args.Skip(1).FirstOrDefault();
                var dest = args.Skip(2).FirstOrDefault();
                if (src == null) Console.WriteLine("No source specified");
                if (dest == null) Console.WriteLine("No destination specified");
                var form = new ConsoleForm(@$"{title}

Source: {src}
Destination: {dest}

Progress: [%Progress                                                 ]");
                Action<double> report = (double progress) => ((PercentField)form["Progress"]).Value = (float)progress;
                switch (op)
                {
                    case "copy":
                        form.Show();
                        ImapCopier.Copy(new Uri(src), new Uri(dest), report);
                        break;
                    case "update":
                        form.Show();
                        ImapCopier.Update(new Uri(src), new Uri(dest), report);
                        break;
                    case "backup":
                        form.Show();
                        using (var stream = new FileStream(dest, FileMode.Create, FileAccess.Write))
                        {
                            ImapCopier.Backup(new Uri(src), stream, report);
                        }
                        break;
                    case "restore":
                        form.Show();
                        using (var stream = new FileStream(src, FileMode.Open, FileAccess.Read))
                        {
                            ImapCopier.Restore(stream, new Uri(dest), report);
                        }
                        break;
                    default:
                        Console.WriteLine(@"usage: imapcopy <command> <source> <destination>

where <command> is one of copy, update, backup, restore
and <source> and <destination> are either an imap(s) url or a file path.");
                        break;
                }
            } else {
                BuildAvaloniaApp()
                    .StartWithClassicDesktopLifetime(args);
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
    }
}
#endif