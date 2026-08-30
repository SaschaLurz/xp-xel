// Boots the .NET WebAssembly runtime and hands control to XP-xel's Program.Main.
//
// This is the standard Avalonia browser bootstrap. It is the only JavaScript in the
// project, and it should stay that way: anything that looks like it belongs here almost
// certainly belongs in C# where the desktop build gets it too.

import { dotnet } from './_framework/dotnet.js';

const isBrowser = typeof window !== 'undefined';
if (!isBrowser) {
  throw new Error('XP-xel must run in a browser.');
}

const runtime = await dotnet
  .withDiagnosticTracing(false)
  .withApplicationArgumentsFromQuery()
  .create();

const config = runtime.getConfig();
await runtime.runMain(config.mainAssemblyName, [globalThis.location.href]);
