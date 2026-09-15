SET PackageVersion=1.1.0
SET Configuration=Release

del nupkg\ImapCopy*.nupkg
del nupkg\ImapCopy*.snupkg

dotnet pack -c %Configuration% -p:Version=%PackageVersion% -p:FileVersion=%PackageVersion% -p:AssemblyVersion=%PackageVersion%
dotnet pack -c %Configuration% -p:Version=%PackageVersion% -p:FileVersion=%PackageVersion% -p:AssemblyVersion=%PackageVersion% -p:PackAsTool=false