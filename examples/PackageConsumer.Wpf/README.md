# PackageConsumer.Wpf

A small WPF app that uses Audio-Centric-Controls **only through the NuGet packages**, the way an outside application
would. It is deliberately not part of `Audio-Centric-Controls.sln`: the packages have to be built first.

## Run it in Visual Studio 2022

1. **Create the packages** (once, and again after changing the library). From the repository root:

   ```
   powershell -ExecutionPolicy Bypass -File scripts\verify-packages.ps1
   ```

   or build `Audio-Centric-Controls.sln` as **Release | x64** and run
   `dotnet pack Audio-Centric-Controls.sln -c Release --no-build`.
   The packages land in `bin\x64\Release\packages\`.
2. **Open `PackageConsumer.sln`** (this folder) in Visual Studio. NuGet restores `AudioCentric.Controls.Wpf` from
   that folder (see `nuget.config`).
3. Press **F5**.

After changing the library and packing again (the version stays 0.1.0), make Visual Studio pick up the new package:
close the solution, delete this folder's `obj` directory (it holds the private package cache), and reopen.
`verify-packages.ps1` does this for you.

The app's output goes to `bin\x64\<Configuration>\PackageConsumer\` at the repository root.
`DemoHost.cs` is a deterministic mock host: it shows each integration pattern without any audio device.
