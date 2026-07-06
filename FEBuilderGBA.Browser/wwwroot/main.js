import { dotnet } from './_framework/dotnet.js'

const is_browser = typeof window != "undefined";
if (!is_browser) throw new Error(`Expected to be running in a browser`);

// #1869: the HTML loading splash (.app-splash) lives inside #out; Avalonia's StartBrowserAppAsync
// mounts its <canvas class="avalonia-canvas"> into #out but does NOT remove the pre-existing splash,
// so the spinner stays over the rendered app forever ("always loading"). Remove the splash as soon
// as Avalonia's canvas is attached — keyed on the canvas (NOT a timer / not after runMain), so a
// genuine boot failure (no canvas) keeps the splash + its message visible instead of masking it.
const _out = document.getElementById('out');
if (_out) {
    const _removeSplash = () => { for (const s of _out.querySelectorAll('.app-splash')) s.remove(); };
    if (_out.querySelector('canvas')) {
        _removeSplash();
    } else {
        const _obs = new MutationObserver(() => {
            if (_out.querySelector('canvas')) { _removeSplash(); _obs.disconnect(); }
        });
        _obs.observe(_out, { childList: true, subtree: true });
    }
}

const dotnetRuntime = await dotnet
    .withDiagnosticTracing(false)
    .withApplicationArgumentsFromQuery()
    .create();

const config = dotnetRuntime.getConfig();

// Pass document.baseURI as the first arg so the app resolves relative fetches (config.zip)
// against the deployed <base href> — correct under a GitHub Pages project path (/FEBuilderGBA/).
await dotnetRuntime.runMain(config.mainAssemblyName, [globalThis.document.baseURI]);
