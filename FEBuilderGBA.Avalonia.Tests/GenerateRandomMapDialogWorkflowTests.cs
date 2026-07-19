using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using global::Avalonia.Automation;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Threading;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class GenerateRandomMapDialogWorkflowTests : IDisposable
    {
        readonly ROM? _savedRom = CoreState.ROM;
        readonly Undo? _savedUndo = CoreState.Undo;
        readonly DecompProject? _savedDecompProject = CoreState.DecompProject;

        public void Dispose()
        {
            CoreState.ROM = _savedRom;
            CoreState.Undo = _savedUndo;
            CoreState.DecompProject = _savedDecompProject;
        }

        [AvaloniaFact]
        public async Task OpenDialogIfReadyAsync_NoMapLoaded_ShowsErrorAndDoesNotOpenDialog()
        {
            var vm = new MapEditorViewModel();
            bool dialogOpened = false;
            string? error = null;

            GenerateRandomMapDialogResult? result = await GenerateRandomMapWorkflow.OpenDialogIfReadyAsync(
                vm,
                _ => false,
                message => error = message,
                (width, height) =>
                {
                    dialogOpened = true;
                    return Task.FromResult<GenerateRandomMapDialogResult?>(null);
                });

            Assert.Null(result);
            Assert.False(dialogOpened);
            Assert.Equal("No map data loaded — select a map first.", error);
        }

        [AvaloniaFact]
        public async Task OpenDialogIfReadyAsync_DecompMode_BlocksBeforeOpeningDialog()
        {
            var vm = new MapEditorViewModel { MapWidth = 15, MapHeight = 10 };
            SeedMapData(vm, BuildMap(15, 10, 0x0001));
            CoreState.DecompProject = new DecompProject { ProjectRoot = "." };

            bool dialogOpened = false;
            GenerateRandomMapDialogResult? result = await GenerateRandomMapWorkflow.OpenDialogIfReadyAsync(
                vm,
                assetName => DecompMapAssetGuard.BlockIfDecomp(assetName),
                _ => { },
                (width, height) =>
                {
                    dialogOpened = true;
                    return Task.FromResult<GenerateRandomMapDialogResult?>(null);
                });

            Assert.Null(result);
            Assert.False(dialogOpened);
        }

        [Fact]
        public async Task ViewModel_OpeningTypingAndBrowse_DoNotInvokeProcessRunner()
        {
            var fakeRunner = new FakeProcessRunner();
            var vm = new GenerateRandomMapDialogViewModel(fakeRunner.Run);
            vm.Initialize(15, 10);
            vm.SetBrowseHandlers(
                () => Task.FromResult<string?>(@"C:\trusted\FEMapCreator.exe"),
                () => Task.FromResult<string?>(@"C:\trusted\assets"));

            Assert.Empty(fakeRunner.Calls);

            vm.FEMapCreatorPath = @"C:\manual\FEMapCreator.exe";
            vm.AssetsDir = @"C:\manual\assets";
            vm.SeedText = "1234";
            Assert.Empty(fakeRunner.Calls);

            await vm.BrowseFEMapCreatorAsync();
            await vm.BrowseAssetsDirAsync();

            Assert.Equal(@"C:\trusted\FEMapCreator.exe", vm.FEMapCreatorPath);
            Assert.Equal(@"C:\trusted\assets", vm.AssetsDir);
            Assert.Empty(fakeRunner.Calls);
        }

        [Fact]
        public async Task BrowseCommand_PickerFailure_IsLoggedAndSurfacedAsLocalizedError()
        {
            var vm = new GenerateRandomMapDialogViewModel();
            vm.Initialize(15, 10);
            vm.SetBrowseHandlers(
                () => Task.FromException<string?>(
                    new UnauthorizedAccessException("picker denied")),
                () => Task.FromResult<string?>(null));

            var errorSet = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(GenerateRandomMapDialogViewModel.ErrorMessage)
                    && vm.HasError)
                {
                    errorSet.TrySetResult(true);
                }
            };

            vm.BrowseFEMapCreatorCommand.Execute(null);
            await errorSet.Task.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.StartsWith(
                "Random map dialog action failed:",
                vm.ErrorMessage,
                StringComparison.Ordinal);
            Assert.Contains("picker denied", vm.ErrorMessage);
        }

        [AvaloniaFact]
        public void Dialog_UsesSupportedAlgorithmSelector()
        {
            var content = new GenerateRandomMapDialogContent();
            var combo = content.FindControl<ComboBox>("AlgorithmComboBox");

            Assert.NotNull(combo);
            Assert.Equal(RandomMapGeneratorAlgorithms.All, combo!.ItemsSource);
            Assert.Equal(
                RandomMapGeneratorAlgorithms.Default,
                combo.SelectedItem);
        }

        [AvaloniaFact]
        public void Dialog_InputsExposeLocalizedAccessibleNames()
        {
            var content = new GenerateRandomMapDialogContent();

            Assert.Equal(
                "FEMapCreator program",
                AutomationProperties.GetName(
                    content.FindControl<TextBox>("FEMapCreatorPathTextBox")!));
            Assert.Equal(
                "Assets directory (optional)",
                AutomationProperties.GetName(
                    content.FindControl<TextBox>("AssetsDirTextBox")!));
            Assert.Equal(
                "Tileset",
                AutomationProperties.GetName(
                    content.FindControl<ComboBox>("TilesetComboBox")!));
            Assert.Equal(
                "Algorithm",
                AutomationProperties.GetName(
                    content.FindControl<ComboBox>("AlgorithmComboBox")!));
            Assert.Equal(
                "Seed (optional)",
                AutomationProperties.GetName(
                    content.FindControl<TextBox>("SeedTextBox")!));
        }

        [AvaloniaFact]
        public async Task Dialog_TitleBarCloseIsBlockedWhileExternalWorkIsBusy()
        {
            using var started = new ManualResetEventSlim(false);
            using var release = new ManualResetEventSlim(false);
            var vm = new GenerateRandomMapDialogViewModel(
                discoverTilesets: (path, assetsDir, runner) =>
                {
                    started.Set();
                    release.Wait(TimeSpan.FromSeconds(10));
                    return new FEMapCreatorTilesetDiscoveryResult
                    {
                        Success = false,
                        ErrorCategory = RandomMapGeneratorErrorCategory.ProcessStartFailed,
                        ErrorMessage = "finished",
                    };
                });
            vm.Initialize(15, 10);
            vm.FEMapCreatorPath = Path.Combine(Path.GetTempPath(), "FEMapCreator.exe");

            var content = new GenerateRandomMapDialogContent(vm);
            var dialog = new GenerateRandomMapDialog(content);
            dialog.Show();
            Task operation = vm.DiscoverTilesetsAsync();
            try
            {
                Assert.True(started.Wait(TimeSpan.FromSeconds(10)));
                Assert.True(vm.IsBusy);
                Assert.False(content.CanClose);

                dialog.Close();

                Assert.True(dialog.IsVisible);
                Assert.True(vm.IsBusy);

                release.Set();
                await operation;
                Assert.True(content.CanClose);

                dialog.Close();
                Assert.False(dialog.IsVisible);
            }
            finally
            {
                release.Set();
                await operation;
                if (dialog.IsVisible)
                    dialog.Close();
            }
        }

        [Fact]
        public async Task ViewModel_UsesSupportedAlgorithmsAndRejectsUnknownValueBeforeGeneration()
        {
            int generationCalls = 0;
            var vm = new GenerateRandomMapDialogViewModel(
                generateRandomMap: (request, runner) =>
                {
                    generationCalls++;
                    return new RandomMapGenerationResult { Success = true };
                });
            vm.Initialize(15, 10);

            Assert.Equal(RandomMapGeneratorAlgorithms.Default, vm.Algorithm);
            Assert.Equal(RandomMapGeneratorAlgorithms.All, vm.Algorithms);

            vm.FEMapCreatorPath = @"C:\trusted\FEMapCreator.exe";
            var tileset = new GenerateRandomMapTilesetOption { Name = "Grassland" };
            vm.Tilesets.Add(tileset);
            vm.SelectedTileset = tileset;
            vm.Algorithm = "cellular";

            await vm.GenerateAsync();

            Assert.Equal(0, generationCalls);
            Assert.True(vm.HasError);
            Assert.Contains("experimental", vm.ErrorMessage);
        }

        [AvaloniaFact]
        public async Task DiscoverTilesetsAsync_Success_InvokesRunnerOnceAndPopulatesTilesets()
        {
            string tempRoot = CreateTempDirectory();
            try
            {
                string femapCreatorPath = CreateEmptyFile(tempRoot, "FEMapCreator.exe");
                string assetsRoot = Path.Combine(tempRoot, "assets");
                Directory.CreateDirectory(assetsRoot);
                File.WriteAllBytes(Path.Combine(assetsRoot, "grassland.png"), CreateIndexedPng(512, 16));
                File.WriteAllText(Path.Combine(assetsRoot, "grassland.json"), "{}");

                var fakeRunner = new FakeProcessRunner
                {
                    Handler = call => new ProcessRunResult
                    {
                        Started = true,
                        ExitCode = 0,
                        Stdout = JsonSerializer.Serialize(new
                        {
                            assetsRoot = assetsRoot,
                            tilesets = new object[]
                            {
                                new
                                {
                                    name = "Grassland",
                                    imagePath = "grassland.png",
                                    generationDataPath = "grassland.json",
                                    hasImage = true,
                                    hasGenerationData = true,
                                    diagnostic = "",
                                }
                            }
                        }),
                        Stderr = "",
                    }
                };

                var vm = new GenerateRandomMapDialogViewModel(fakeRunner.Run);
                vm.Initialize(15, 10);
                vm.FEMapCreatorPath = femapCreatorPath;
                vm.AssetsDir = assetsRoot;

                var busyTransitions = TrackBusyTransitions(vm);
                await vm.DiscoverTilesetsAsync();

                Assert.Equal(new[] { true, false }, busyTransitions);
                Assert.Single(fakeRunner.Calls);
                Assert.Equal(femapCreatorPath, fakeRunner.Calls[0].Command);
                Assert.Equal(new[] { "tilesets", "list", "--json", "--assets-dir", assetsRoot }, fakeRunner.Calls[0].Arguments);
                Assert.Single(vm.Tilesets);
                Assert.Equal("Grassland", vm.Tilesets[0].Name);
                Assert.Same(vm.Tilesets[0], vm.SelectedTileset);
                Assert.False(vm.HasError);
            }
            finally
            {
                DeleteDirectoryIfPresent(tempRoot);
            }
        }

        [AvaloniaFact]
        public async Task DiscoverTilesetsAsync_Failure_ShowsErrorAndStaysOpen()
        {
            string tempRoot = CreateTempDirectory();
            try
            {
                string femapCreatorPath = CreateEmptyFile(tempRoot, "FEMapCreator.exe");
                var fakeRunner = new FakeProcessRunner
                {
                    Handler = call => new ProcessRunResult
                    {
                        Started = true,
                        ExitCode = 7,
                        Stdout = "",
                        Stderr = "boom",
                    }
                };

                var vm = new GenerateRandomMapDialogViewModel(fakeRunner.Run);
                vm.Initialize(15, 10);
                vm.FEMapCreatorPath = femapCreatorPath;
                bool closeRequested = false;
                vm.CloseRequested += (_, _) => closeRequested = true;

                var busyTransitions = TrackBusyTransitions(vm);
                await vm.DiscoverTilesetsAsync();

                Assert.Equal(new[] { true, false }, busyTransitions);
                Assert.Single(fakeRunner.Calls);
                Assert.True(vm.HasError);
                Assert.StartsWith(
                    "Tileset discovery failed:",
                    vm.ErrorMessage,
                    StringComparison.Ordinal);
                Assert.Contains("exited with code 7", vm.ErrorMessage, StringComparison.OrdinalIgnoreCase);
                Assert.Empty(vm.Tilesets);
                Assert.Null(vm.SelectedTileset);
                Assert.False(closeRequested);
            }
            finally
            {
                DeleteDirectoryIfPresent(tempRoot);
            }
        }

        [AvaloniaFact]
        public async Task GenerateAsync_Success_InvokesExpectedArgumentsAndClosesWithResult()
        {
            string tempRoot = CreateTempDirectory();
            try
            {
                string femapCreatorPath = CreateEmptyFile(tempRoot, "FEMapCreator.exe");
                string assetsRoot = Path.Combine(tempRoot, "assets");
                Directory.CreateDirectory(assetsRoot);
                File.WriteAllBytes(Path.Combine(assetsRoot, "grassland.png"), CreateIndexedPng(512, 16));
                File.WriteAllText(Path.Combine(assetsRoot, "grassland.json"), "{}");

                var fakeRunner = new FakeProcessRunner
                {
                    Handler = call =>
                    {
                        if (call.Arguments.SequenceEqual(new[] { "tilesets", "list", "--json", "--assets-dir", assetsRoot }))
                        {
                            return new ProcessRunResult
                            {
                                Started = true,
                                ExitCode = 0,
                                Stdout = JsonSerializer.Serialize(new
                                {
                                    assetsRoot = assetsRoot,
                                    tilesets = new object[]
                                    {
                                        new
                                        {
                                            name = "Grassland",
                                            imagePath = "grassland.png",
                                            generationDataPath = "grassland.json",
                                            hasImage = true,
                                            hasGenerationData = true,
                                            diagnostic = "",
                                        }
                                    }
                                }),
                                Stderr = "",
                            };
                        }

                        string outputPath = FindArgumentValue(call.Arguments, "--output");
                        WriteRawMar(outputPath, 4, index => index * 32);
                        return new ProcessRunResult
                        {
                            Started = true,
                            ExitCode = 0,
                            Stdout = "ok",
                            Stderr = "",
                        };
                    }
                };

                var vm = new GenerateRandomMapDialogViewModel(fakeRunner.Run, generateSeed: () => 12345);
                vm.Initialize(2, 2);
                vm.FEMapCreatorPath = femapCreatorPath;
                vm.AssetsDir = assetsRoot;
                await vm.DiscoverTilesetsAsync();

                bool closeRequested = false;
                vm.CloseRequested += (_, _) => closeRequested = true;

                var busyTransitions = TrackBusyTransitions(vm);
                await vm.GenerateAsync();

                Assert.Equal(new[] { true, false }, busyTransitions);
                Assert.Equal(2, fakeRunner.Calls.Count);

                RecordedCall generateCall = fakeRunner.Calls[1];
                Assert.Equal(femapCreatorPath, generateCall.Command);
                Assert.Equal(new[]
                {
                    "generate",
                    "--width", "2",
                    "--height", "2",
                    "--tileset", "Grassland",
                    "--algorithm", RandomMapGeneratorAlgorithms.Default,
                    "--seed", "12345",
                    "--output", FindArgumentValue(generateCall.Arguments, "--output"),
                    "--format", "mar",
                    "--require-complete",
                    "--force",
                    "--assets-dir", assetsRoot,
                }, generateCall.Arguments);
                Assert.DoesNotContain("--json", generateCall.Arguments);
                Assert.True(closeRequested);
                Assert.NotNull(vm.Result);
                Assert.Equal(12345, vm.Result!.EffectiveSeed);
                Assert.Equal(new ushort[] { 0, 4, 8, 12 }, vm.Result.Mars);
            }
            finally
            {
                DeleteDirectoryIfPresent(tempRoot);
            }
        }

        [AvaloniaFact]
        public async Task GenerateAsync_Failure_ShowsErrorAndDoesNotClose()
        {
            string tempRoot = CreateTempDirectory();
            try
            {
                string femapCreatorPath = CreateEmptyFile(tempRoot, "FEMapCreator.exe");
                string assetsRoot = Path.Combine(tempRoot, "assets");
                Directory.CreateDirectory(assetsRoot);
                File.WriteAllBytes(Path.Combine(assetsRoot, "grassland.png"), CreateIndexedPng(512, 16));
                File.WriteAllText(Path.Combine(assetsRoot, "grassland.json"), "{}");

                var fakeRunner = new FakeProcessRunner
                {
                    Handler = call =>
                    {
                        if (call.Arguments.Contains("--json"))
                        {
                            return new ProcessRunResult
                            {
                                Started = true,
                                ExitCode = 0,
                                Stdout = JsonSerializer.Serialize(new
                                {
                                    assetsRoot = assetsRoot,
                                    tilesets = new object[]
                                    {
                                        new
                                        {
                                            name = "Grassland",
                                            imagePath = "grassland.png",
                                            generationDataPath = "grassland.json",
                                            hasImage = true,
                                            hasGenerationData = true,
                                            diagnostic = "",
                                        }
                                    }
                                }),
                                Stderr = "",
                            };
                        }

                        return new ProcessRunResult
                        {
                            Started = true,
                            ExitCode = 9,
                            Stdout = "",
                            Stderr = "failed",
                        };
                    }
                };

                var vm = new GenerateRandomMapDialogViewModel(fakeRunner.Run, generateSeed: () => 77);
                vm.Initialize(2, 2);
                vm.FEMapCreatorPath = femapCreatorPath;
                vm.AssetsDir = assetsRoot;
                await vm.DiscoverTilesetsAsync();

                bool closeRequested = false;
                vm.CloseRequested += (_, _) => closeRequested = true;

                var busyTransitions = TrackBusyTransitions(vm);
                await vm.GenerateAsync();

                Assert.Equal(new[] { true, false }, busyTransitions);
                Assert.Equal(2, fakeRunner.Calls.Count);
                Assert.True(vm.HasError);
                Assert.StartsWith(
                    "Random map generation failed:",
                    vm.ErrorMessage,
                    StringComparison.Ordinal);
                Assert.Contains("exited with code 9", vm.ErrorMessage, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("failed", vm.ErrorMessage, StringComparison.OrdinalIgnoreCase);
                Assert.Null(vm.Result);
                Assert.False(closeRequested);
            }
            finally
            {
                DeleteDirectoryIfPresent(tempRoot);
            }
        }

        [AvaloniaFact]
        public async Task ApplyGeneratedMapOnUiThreadAsync_Success_CommitsOnceRefreshesAndShowsSeed()
        {
            CoreState.ROM = CreateRom();
            CoreState.Undo = new Undo();

            const uint pointerEntryAddr = 0x240;
            const uint oldAddr = 0x1000;
            byte[] originalMap = BuildMap(2, 2, 0x0001);
            byte[] originalCompressed = LZ77.compress(originalMap);
            CoreState.ROM!.write_p32(pointerEntryAddr, oldAddr);
            CoreState.ROM.write_range(oldAddr, originalCompressed);

            var vm = new MapEditorViewModel
            {
                CurrentAddr = 0x300,
                MapId = 1,
                MapWidth = 2,
                MapHeight = 2,
            };
            ConfigureMapPointerIdentity(vm, pointerEntryAddr, mapPlist: 1);
            SetPrivateField(vm, "_cachedMapPointerEntryAddr", pointerEntryAddr);
            SetPrivateField(vm, "_cachedMapData", (byte[])originalMap.Clone());
            Assert.True(
                vm.TryCaptureMapWriteIdentity(
                    out MapEditorViewModel.MapWriteIdentity writeIdentity,
                    out string identityError),
                identityError);

            var sequence = new List<string>();
            var undo = new RecordingUndoService(sequence);
            int refreshCalls = 0;
            int paletteCalls = 0;
            bool callbacksOnUiThread = true;
            string infoMessage = "";
            var result = new GenerateRandomMapDialogResult
            {
                Mars = new ushort[] { 0x0002, 0x0003, 0x0004, 0x0005 },
                Width = 2,
                Height = 2,
                EffectiveSeed = 4242,
            };

            string? error = await Task.Run(() =>
                GenerateRandomMapWorkflow.ApplyGeneratedMapOnUiThreadAsync(
                    vm,
                    undo,
                    result,
                    writeIdentity,
                    refreshMapFromCurrentSelection: () =>
                    {
                        callbacksOnUiThread &= Dispatcher.UIThread.CheckAccess();
                        refreshCalls++;
                        sequence.Add("Refresh");
                    },
                    updateTilePalette: () =>
                    {
                        callbacksOnUiThread &= Dispatcher.UIThread.CheckAccess();
                        paletteCalls++;
                        sequence.Add("Palette");
                    },
                    reloadFromRom: () => sequence.Add("Reload"),
                    showInfo: message =>
                    {
                        callbacksOnUiThread &= Dispatcher.UIThread.CheckAccess();
                        infoMessage = message;
                        sequence.Add("Info");
                    }));

            Assert.Null(error);
            Assert.True(callbacksOnUiThread);
            Assert.True(undo.BeginOnUiThread);
            Assert.True(undo.CommitOnUiThread);
            Assert.Equal(1, undo.BeginCalls);
            Assert.Equal(1, undo.CommitCalls);
            Assert.Equal(0, undo.RollbackCalls);
            Assert.Equal("MapEditor.GenerateRandomMap", undo.LastBeginName);
            Assert.Equal(new[] { "Begin", "Refresh", "Palette", "Info", "Commit" }, sequence);
            Assert.Equal(1, refreshCalls);
            Assert.Equal(1, paletteCalls);
            Assert.Contains("2x2", infoMessage);
            Assert.Contains("seed=4242", infoMessage);
            Assert.Equal(
                new byte[] { 2, 2, 2, 0, 3, 0, 4, 0, 5, 0 },
                vm.GetMapDataSnapshot());
        }

        [AvaloniaTheory]
        [InlineData("rom")]
        [InlineData("selection")]
        [InlineData("plist")]
        [InlineData("table")]
        [InlineData("pointer")]
        [InlineData("payload")]
        public async Task ApplyGeneratedMapOnUiThreadAsync_ContextChanged_AbortsBeforeUndo(
            string changeKind)
        {
            CoreState.ROM = CreateRom();
            CoreState.Undo = new Undo();

            const uint pointerEntryAddr = 0x240;
            const uint oldAddr = 0x1000;
            byte[] originalMap = BuildMap(2, 2, 0x0001);
            CoreState.ROM!.write_p32(pointerEntryAddr, oldAddr);
            CoreState.ROM.write_range(oldAddr, LZ77.compress(originalMap));

            var vm = new MapEditorViewModel
            {
                CurrentAddr = 0x300,
                MapId = 1,
                MapWidth = 2,
                MapHeight = 2,
            };
            ConfigureMapPointerIdentity(vm, pointerEntryAddr, mapPlist: 1);
            SetPrivateField(vm, "_cachedMapPointerEntryAddr", pointerEntryAddr);
            SetPrivateField(vm, "_cachedMapData", (byte[])originalMap.Clone());
            Assert.True(
                vm.TryCaptureMapWriteIdentity(
                    out MapEditorViewModel.MapWriteIdentity writeIdentity,
                    out string identityError),
                identityError);

            switch (changeKind)
            {
                case "rom":
                    CoreState.ROM = CreateRom();
                    break;
                case "selection":
                    vm.MapId = 2;
                    break;
                case "plist":
                    CoreState.ROM!.write_u8(vm.CurrentAddr + 8, 2);
                    break;
                case "table":
                    CoreState.ROM!.write_p32(0x220, 0x260);
                    break;
                case "pointer":
                    const uint newMapAddr = 0x1200;
                    CoreState.ROM!.write_range(
                        newMapAddr,
                        LZ77.compress(originalMap));
                    CoreState.ROM.write_p32(pointerEntryAddr, newMapAddr);
                    break;
                case "payload":
                    CoreState.ROM!.write_range(
                        oldAddr,
                        LZ77.compress(BuildMap(2, 2, 0x0009)));
                    break;
                default:
                    throw new InvalidOperationException(
                        "Unknown change kind: " + changeKind);
            }

            var undo = new RecordingUndoService(new List<string>());
            int reloadCalls = 0;
            string? error = await Task.Run(() =>
                GenerateRandomMapWorkflow.ApplyGeneratedMapOnUiThreadAsync(
                    vm,
                    undo,
                    new GenerateRandomMapDialogResult
                    {
                        Mars = new ushort[] { 2, 3, 4, 5 },
                        Width = 2,
                        Height = 2,
                        EffectiveSeed = 7,
                    },
                    writeIdentity,
                    refreshMapFromCurrentSelection: () =>
                        throw new InvalidOperationException("must not refresh success"),
                    updateTilePalette: () =>
                        throw new InvalidOperationException("must not update palette"),
                    reloadFromRom: () => reloadCalls++,
                    showInfo: _ =>
                        throw new InvalidOperationException("must not show success")));

            Assert.NotNull(error);
            Assert.Contains("changed", error, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, undo.BeginCalls);
            Assert.Equal(0, undo.CommitCalls);
            Assert.Equal(0, undo.RollbackCalls);
            Assert.Equal(changeKind == "rom" ? 0 : 1, reloadCalls);
        }

        [AvaloniaFact]
        public void MapEditorView_StrictRandomMapAdaptersThrowAndAreWired()
        {
            var view = new MapEditorView();

            Assert.Throws<InvalidOperationException>(
                view.RefreshMapImageFromCurrentSelectionStrict);
            Assert.Throws<InvalidOperationException>(
                view.UpdateTilePaletteStrict);

            string source = File.ReadAllText(Path.Combine(
                FindRepoRoot(),
                "FEBuilderGBA.Avalonia",
                "Views",
                "MapEditorView.axaml.cs"));
            int callStart = source.IndexOf(
                "ApplyGeneratedMapOnUiThreadAsync(",
                StringComparison.Ordinal);
            Assert.True(callStart >= 0);
            string call = source.Substring(
                callStart,
                Math.Min(1200, source.Length - callStart));

            Assert.Contains(
                "RefreshMapImageFromCurrentSelectionStrict",
                call);
            Assert.Contains("UpdateTilePaletteStrict", call);
            Assert.Contains("RefreshMapFromCurrentSelectionStrict", call);
        }

        [AvaloniaFact]
        public async Task ApplyGeneratedMapOnUiThreadAsync_PostWriteFailure_RollsBackAndReloadsCache()
        {
            CoreState.ROM = CreateRom();
            CoreState.Undo = new Undo();

            const uint pointerEntryAddr = 0x240;
            const uint oldAddr = 0x1000;
            byte[] originalMap = BuildMap(2, 2, 0x0001);
            byte[] originalCompressed = LZ77.compress(originalMap);
            CoreState.ROM!.write_p32(pointerEntryAddr, oldAddr);
            CoreState.ROM.write_range(oldAddr, originalCompressed);

            var vm = new MapEditorViewModel
            {
                CurrentAddr = 0x300,
                MapId = 1,
                MapWidth = 2,
                MapHeight = 2,
            };
            ConfigureMapPointerIdentity(vm, pointerEntryAddr, mapPlist: 1);
            SetPrivateField(vm, "_cachedMapPointerEntryAddr", pointerEntryAddr);
            SetPrivateField(vm, "_cachedMapData", (byte[])originalMap.Clone());
            Assert.True(
                vm.TryCaptureMapWriteIdentity(
                    out MapEditorViewModel.MapWriteIdentity writeIdentity,
                    out string identityError),
                identityError);

            ushort[] generatedMars = { 0x0002, 0x0003, 0x0004, 0x0005 };
            bool reloadCalled = false;
            var undo = new UndoService();

            string? error = await Task.Run(() =>
                GenerateRandomMapWorkflow.ApplyGeneratedMapOnUiThreadAsync(
                    vm,
                    undo,
                    new GenerateRandomMapDialogResult
                    {
                        Mars = generatedMars,
                        Width = 2,
                        Height = 2,
                        EffectiveSeed = 9,
                    },
                    writeIdentity,
                    refreshMapFromCurrentSelection: () => { },
                    updateTilePalette: () =>
                        throw new InvalidOperationException("post-write failure"),
                    reloadFromRom: () =>
                    {
                        reloadCalled = true;
                        SetPrivateField(vm, "_cachedMapData", (byte[])originalMap.Clone());
                    },
                    showInfo: _ => { }));

            Assert.NotNull(error);
            Assert.True(reloadCalled);
            Assert.Contains("post-write failure", error, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(originalCompressed, CoreState.ROM.Data.Skip((int)oldAddr).Take(originalCompressed.Length).ToArray());
            Assert.Equal(originalMap, vm.GetMapDataSnapshot());
        }

        static List<bool> TrackBusyTransitions(GenerateRandomMapDialogViewModel vm)
        {
            var transitions = new List<bool>();
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(GenerateRandomMapDialogViewModel.IsBusy))
                    transitions.Add(vm.IsBusy);
            };
            return transitions;
        }

        sealed class RecordingUndoService : UndoService
        {
            readonly List<string> _sequence;

            internal RecordingUndoService(List<string> sequence)
            {
                _sequence = sequence;
            }

            internal int BeginCalls { get; private set; }
            internal int CommitCalls { get; private set; }
            internal int RollbackCalls { get; private set; }
            internal string LastBeginName { get; private set; } = "";
            internal bool BeginOnUiThread { get; private set; }
            internal bool CommitOnUiThread { get; private set; }

            public override void Begin(string name)
            {
                BeginCalls++;
                LastBeginName = name;
                BeginOnUiThread = Dispatcher.UIThread.CheckAccess();
                _sequence.Add("Begin");
                base.Begin(name);
            }

            public override void Commit()
            {
                CommitCalls++;
                CommitOnUiThread = Dispatcher.UIThread.CheckAccess();
                _sequence.Add("Commit");
                base.Commit();
            }

            public override void Rollback()
            {
                RollbackCalls++;
                _sequence.Add("Rollback");
                base.Rollback();
            }
        }

        static ROM CreateRom(int size = 0x8000)
        {
            byte[] data = Enumerable.Repeat((byte)0xAA, size).ToArray();
            for (int i = size / 2; i < size / 2 + 0x1000; i++)
                data[i] = 0x00;

            var rom = new ROM();
            Assert.True(rom.LoadLow("synthetic.gba", data, "NAZO"));
            return rom;
        }

        static byte[] BuildMap(int width, int height, ushort fill)
        {
            byte[] map = new byte[2 + width * height * 2];
            map[0] = (byte)width;
            map[1] = (byte)height;
            for (int i = 0; i < width * height; i++)
            {
                int off = 2 + i * 2;
                map[off] = (byte)(fill & 0xFF);
                map[off + 1] = (byte)(fill >> 8);
            }
            return map;
        }

        static void SeedMapData(MapEditorViewModel vm, byte[] mapData)
            => SetPrivateField(vm, "_cachedMapData", (byte[])mapData.Clone());

        static void ConfigureMapPointerIdentity(
            MapEditorViewModel vm,
            uint pointerEntryAddr,
            byte mapPlist)
        {
            ROM rom = CoreState.ROM!;
            const uint tablePointerAddr = 0x220;
            uint tableBase = pointerEntryAddr - mapPlist * 4u;
            PropertyInfo? property = rom.RomInfo.GetType().BaseType?.GetProperty(
                "map_map_pointer_pointer",
                BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(property);
            property!.SetValue(rom.RomInfo, tablePointerAddr);
            rom.write_p32(tablePointerAddr, tableBase);
            rom.write_u8(vm.CurrentAddr + 8, mapPlist);
        }

        static string FindRepoRoot()
        {
            string? directory = AppContext.BaseDirectory;
            while (directory != null
                && !File.Exists(Path.Combine(directory, "FEBuilderGBA.sln")))
            {
                directory = Path.GetDirectoryName(directory);
            }
            Assert.NotNull(directory);
            return directory!;
        }

        static void SetPrivateField(object target, string name, object value)
        {
            FieldInfo? field = target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(field);
            field!.SetValue(target, value);
        }

        static byte[] CreateIndexedPng(int width, int height)
        {
            byte[] palette = new byte[32];
            byte[] indices = new byte[width * height];
            byte[] pngBytes = IndexedPngWriter.Write(indices, width, height, palette, paletteColorCount: 1);
            Assert.NotNull(pngBytes);
            return pngBytes;
        }

        static void WriteRawMar(string path, int entryCount, Func<int, int> rawValueFactory)
        {
            byte[] bytes = new byte[entryCount * 2];
            for (int i = 0; i < entryCount; i++)
            {
                int value = rawValueFactory(i);
                bytes[i * 2] = (byte)(value & 0xFF);
                bytes[i * 2 + 1] = (byte)((value >> 8) & 0xFF);
            }
            File.WriteAllBytes(path, bytes);
        }

        static string FindArgumentValue(IReadOnlyList<string> args, string key)
        {
            for (int i = 0; i < args.Count - 1; i++)
            {
                if (string.Equals(args[i], key, StringComparison.Ordinal))
                    return args[i + 1];
            }
            throw new InvalidOperationException("Argument not found: " + key);
        }

        static string CreateTempDirectory()
        {
            string path = Path.Combine(Path.GetTempPath(), "febuildergba-avalonia-random-map-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        static string CreateEmptyFile(string directory, string fileName)
        {
            string path = Path.Combine(directory, fileName);
            File.WriteAllText(path, "");
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    path,
                    File.GetUnixFileMode(path)
                    | UnixFileMode.UserExecute
                    | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherExecute);
            }
            return path;
        }

        static void DeleteDirectoryIfPresent(string path)
        {
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }

        sealed class RecordedCall
        {
            public string Command { get; init; } = "";
            public List<string> Arguments { get; init; } = new();
            public string WorkingDirectory { get; init; } = "";
            public int TimeoutMs { get; init; }
            public int MaximumOutputChars { get; init; }
        }

        sealed class FakeProcessRunner
        {
            public readonly List<RecordedCall> Calls = new();
            public Func<RecordedCall, ProcessRunResult>? Handler { get; init; }

            public ProcessRunResult Run(
                string command,
                IEnumerable<string> args,
                string workingDir,
                int timeoutMs,
                int maximumOutputChars)
            {
                var call = new RecordedCall
                {
                    Command = command,
                    Arguments = new List<string>(args),
                    WorkingDirectory = workingDir,
                    TimeoutMs = timeoutMs,
                    MaximumOutputChars = maximumOutputChars,
                };
                Calls.Add(call);
                return Handler?.Invoke(call) ?? ProcessRunResult.NotStarted("No fake handler configured.");
            }
        }
    }
}
