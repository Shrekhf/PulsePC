# Changes for the public source package

- Formatted all C# files consistently and added editor settings; verified executable tokens were unchanged by formatting.
- Added an installer preflight check so running the source-folder installer explains how to build first.

- Enabled optimized x64 builds with a dedicated bin directory.
- Cached sensor property and method reflection metadata across polling cycles.
- Backed off missing external WMI provider retries to once per minute.
- Bounded subprocess output-drain waits after process exit.
- Prevented polling from restarting during shutdown, waited for initialization and active sampling, and disposed the refresh timer.
- Allowed standard-user startup without optional sensor dependencies; driver installation and administrator restart are explicit actions.
- Added Windows build/smoke-test automation, contributor notes, dependency setup instructions and ignore rules for local data.
- Excluded personal settings, captured reports, history and third-party binaries from the source package.

Validation: Windows x64 builds passed. Existing integrated smoke checks passed both without the sensor library and with LibreHardwareMonitor available under standard-user permissions. Storage rendering was visually inspected. Full elevated sensor access, driver installation, destructive process actions and hosted GitHub CI were not exercised in this preparation.

No comparative performance benchmark was run; the changes reduce repeated operations but no percentage speedup is claimed.
