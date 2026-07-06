import { dotnet } from './_framework/dotnet.js'

const is_browser = typeof window != "undefined";
if (!is_browser) throw new Error(`Expected to be running in a browser`);

const dotnetRuntime = await dotnet
    .withDiagnosticTracing(false)
    .withApplicationArgumentsFromQuery()
    .create();

const config = dotnetRuntime.getConfig();

// Pass document.baseURI as the first arg so the app resolves relative fetches (config.zip)
// against the deployed <base href> — correct under a GitHub Pages project path (/FEBuilderGBA/).
await dotnetRuntime.runMain(config.mainAssemblyName, [globalThis.document.baseURI]);
