SET PackageVersion=4.1.8
SET Configuration=Release

del nupkg\SSLCertificateMaker*.nupkg
del nupkg\SSLCertificateMaker*.snupkg

dotnet pack -c %Configuration% -p:Version=%PackageVersion% -p:FileVersion=%PackageVersion% -p:AssemblyVersion=%PackageVersion%
dotnet pack -c %Configuration% -p:Version=%PackageVersion% -p:FileVersion=%PackageVersion% -p:AssemblyVersion=%PackageVersion% -p:PackAsTool=false