using System;
using System.IO;
using System.Text;
using FEBuilderGBA;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// xUnit fixture that loads a ROM and initializes CoreState for headless tests.
    /// Use with IClassFixture&lt;RomFixture&gt; to share ROM state across test classes.
    ///
    /// ROM loading uses the same RomLoader.InitFull() pattern as the CLI:
    ///   CoreState.ROM, headless caches, Huffman encoder, event scripts, etc.
    ///
    /// If no ROM is available, IsAvailable is false and tests should skip.
    /// </summary>
    public class RomFixture : IDisposable
    {
        /// <summary>Whether a ROM was successfully loaded.</summary>
        public bool IsAvailable { get; }

        /// <summary>The loaded ROM instance, or null if unavailable.</summary>
        public ROM? ROM { get; }

        /// <summary>The detected version string ("FE6", "FE7J", "FE7U", "FE8J", "FE8U"), or null.</summary>
        public string? Version { get; }

        /// <summary>Path to the ROM file, or null if unavailable.</summary>
        public string? RomPath { get; }

        // Snapshot of CoreState before we modified it, so Dispose can restore.
        private readonly ROM? _prevRom;
        private readonly IEtcCache? _prevCommentCache;
        private readonly IEtcCache? _prevLintCache;
        private readonly IEtcCache? _prevWorkSupportCache;
        private readonly ISystemTextEncoder? _prevSystemTextEncoder;
        private readonly string? _prevBaseDirectory;

        /// <summary>
        /// Creates the fixture. Attempts to load a ROM in priority order:
        /// FE8U, FE7U, FE8J, FE7J, FE6 (US English ROMs preferred for broader test coverage).
        /// </summary>
        public RomFixture()
        {
            // Save previous CoreState
            _prevRom = CoreState.ROM;
            _prevCommentCache = CoreState.CommentCache;
            _prevLintCache = CoreState.LintCache;
            _prevWorkSupportCache = CoreState.WorkSupportCache;
            _prevSystemTextEncoder = CoreState.SystemTextEncoder;
            _prevBaseDirectory = CoreState.BaseDirectory;

            // Try to find a ROM, preferring US versions
            string[] preferred = { "FE8U", "FE7U", "FE8J", "FE7J", "FE6" };
            foreach (string ver in preferred)
            {
                string? path = TestRomLocator.FindRom(ver);
                if (path != null)
                {
                    RomPath = path;
                    Version = ver;
                    break;
                }
            }

            if (RomPath == null)
            {
                IsAvailable = false;
                return;
            }

            try
            {
                // Set BaseDirectory to the test assembly output dir (where config/ is copied)
                string assemblyDir = Path.GetDirectoryName(
                    System.Reflection.Assembly.GetExecutingAssembly().Location) ?? ".";
                CoreState.BaseDirectory = assemblyDir;

                // Register code pages for Shift-JIS, etc.
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

                // Load config if available
                string configPath = Path.Combine(assemblyDir, "config", "config.xml");
                if (File.Exists(configPath))
                {
                    var config = new Config();
                    config.Load(configPath);
                    CoreState.Config = config;
                }

                // Load the ROM
                var rom = new ROM();
                bool ok = rom.Load(RomPath, out string _);
                if (!ok)
                {
                    IsAvailable = false;
                    return;
                }

                CoreState.ROM = rom;
                ROM = rom;

                // Wire headless caches
                CoreState.CommentCache = new HeadlessEtcCache();
                CoreState.LintCache = new HeadlessEtcCache();
                CoreState.WorkSupportCache = new HeadlessEtcCache();

                // Wire text encoder
                try
                {
                    CoreState.SystemTextEncoder = new SystemTextEncoder(CoreState.TextEncoding, rom);
                }
                catch
                {
                    CoreState.SystemTextEncoder = new HeadlessSystemTextEncoder(rom);
                }

                // Init Huffman text encoder
                try
                {
                    CoreState.FETextEncoder = new FETextEncode();
                }
                catch
                {
                    // Non-fatal: some tests don't need text encoding
                }

                // Init text escape
                CoreState.TextEscape ??= new TextEscape();

                // Init undo
                CoreState.Undo ??= new Undo();

                IsAvailable = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RomFixture init failed: {ex.Message}");
                IsAvailable = false;
            }
        }

        public void Dispose()
        {
            // Restore previous CoreState to avoid leaking between test collections
            CoreState.ROM = _prevRom;
            CoreState.CommentCache = _prevCommentCache;
            CoreState.LintCache = _prevLintCache;
            CoreState.WorkSupportCache = _prevWorkSupportCache;
            CoreState.SystemTextEncoder = _prevSystemTextEncoder;
            if (_prevBaseDirectory != null)
                CoreState.BaseDirectory = _prevBaseDirectory;
        }
    }
}
