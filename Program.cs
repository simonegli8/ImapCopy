#if PackAsTool
using Avalonia;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace ImapCopy
{
    internal class Program
    {
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static async Task Main(string[] args)
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            var title = $"ImapCopy, v{version.ToString(3)}";
            Console.WriteLine(title);

            if (args.Length > 0)
            {
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
                            Console.WriteLine(@"
usage: imapcopy <command> <source> <destination>

where <command> is one of copy, update, backup, restore
and <source> and <destination> are either an imap(s) url or a file path.");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
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