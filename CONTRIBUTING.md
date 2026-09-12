# Contributing

Build with build.ps1 and run test.ps1 on Windows before submitting changes. Describe the observed behavior, the change, and how it was verified.

Do not commit executables, sensor dependencies, personal settings, diagnostic exports or history. Keep hardware and process mutations behind explicit user actions. Missing telemetry must remain unavailable.

The source uses partial C# classes and the Windows .NET Framework compiler. Avoid unsupported language features without updating the build requirements. Follow `.editorconfig`: four-space indentation and braces on separate lines for C#.

Do not post sensitive reports or credentials in public issues. A private security-reporting channel has not yet been configured.
