# Third-party runtime

`mod_out` includes the official BepInEx Unity IL2CPP Windows x64 bleeding-edge build `6.0.0-be.785+6abdba4`, downloaded from the official BepInEx build service. This build is required because the game's IL2CPP metadata version is 31; the older `6.0.0-pre.2` package fails before plugin loading.

The plugin output also contains the Microsoft Windows SDK .NET projection and `WinRT.Runtime.dll`, produced from the official `Microsoft.Windows.SDK.NET.Ref` targeting pack during build, so Windows BLE APIs resolve inside BepInEx's bundled .NET 6 runtime.
