# ImapCopy
A cross platform GUI/CLI tool to backup or copy IMAP mailboxes

## Usage
To install imapcopy, install the [.NET 10 SDK](https://get.dot.net/10.0) and run the shell command:
```
dotnet tool install -g ImapCopy
```
And then run
```
imapcopy install
```
to install a shortcut to your start menu.

and then run it by executing
```
imapcopy
```
You can also run imapcopy as CLI tool, run `imapcopy help` for help.

# Library
To use the library, include the following in your .csproj file:
```
<PackageReference Include="ImapCopy.Library" Version="1.0.6" />
```
and then in your code:
```
using ImapCopy;

...

ImapCompier.Copy(new Uri(sourceUrl), new Uri(destinationUrl));
or
ImapCopier.Update(new Uri(sourceUrl), new Uri(destinationUrl));
or
ImapCopier.Backup(new Uri(sourceUrl), destination7zipStream);
or
ImapCopier.Restore(source7zipStream, new Uri(destinationUrl));
```
You can also specify an `Action<double>` parameter at the end to report progress.
