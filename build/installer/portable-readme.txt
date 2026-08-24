FG Scanner — portable build
===========================

Run FgScanner.exe from this folder; nothing is installed.

Notes for portable use:
  * Data (database, trash, OCR languages, logs) still lives under
    %APPDATA%\FGScanner and %LOCALAPPDATA%\FGScanner.
  * File associations, the scanner-button integration, and auto-update are
    only available through the installer.
  * The command line tool is fgscanner.exe in this folder. It needs the .NET 10
    Desktop Runtime installed (FgScanner.exe does not - it is self-contained).
    Without it, fgscanner.exe exits with "You must install .NET".
    Get it from https://dotnet.microsoft.com/download/dotnet/10.0
  * Global options (--verbose, --fake) go BEFORE the command:
    fgscanner --verbose scan --group C:\Scans\Inbox
