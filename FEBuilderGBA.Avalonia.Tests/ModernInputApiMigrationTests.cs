// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Input;
using global::Avalonia.Platform.Storage;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    public class ModernInputApiMigrationTests
    {
        [Fact]
        public async Task ClipboardTextHelper_NullClipboardReturnsNull()
        {
            Assert.Null(await ClipboardTextHelper.TryGetTextAsync(null));
        }

        [AvaloniaFact]
        public async Task ClipboardTextHelper_NoTextDataReturnsNull()
        {
            var window = new Window();
            await window.Clipboard!.ClearAsync();

            string? text = await ClipboardTextHelper.TryGetTextAsync(window.Clipboard);

            Assert.Null(text);
        }

        [AvaloniaFact]
        public async Task ClipboardTextHelper_ReadsTextThroughModernDataTransfer()
        {
            var window = new Window();
            await window.Clipboard!.SetTextAsync("class,csv");

            string? text = await ClipboardTextHelper.TryGetTextAsync(window.Clipboard);

            Assert.Equal("class,csv", text);
        }

        [Fact]
        public void DragDropFileHelper_AcceptsFirstMatchingImageFile()
        {
            IDataTransfer transfer = MakeTransfer(
                CreateStorageFile("readme.txt"),
                CreateStorageFile("portrait.PNG"));

            string? path = DragDropFileHelper.GetFirstAcceptedPath(
                transfer,
                DragDropFileHelper.ImageExtensions);

            Assert.NotNull(path);
            Assert.EndsWith("portrait.PNG", path!.Replace('/', '\\'));
            Assert.True(DragDropFileHelper.HasAcceptedFile(
                transfer,
                DragDropFileHelper.ImageExtensions));
        }

        [Fact]
        public void DragDropFileHelper_RejectsNoFileData()
        {
            var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.CreateText("not a file"));

            Assert.Null(DragDropFileHelper.GetFirstAcceptedPath(
                transfer,
                DragDropFileHelper.ImageExtensions));
            Assert.False(DragDropFileHelper.HasAcceptedFile(
                transfer,
                DragDropFileHelper.ImageExtensions));
        }

        [Fact]
        public void DragDropFileHelper_RejectsUnsupportedExtensions()
        {
            IDataTransfer transfer = MakeTransfer(CreateStorageFile("portrait.gif"));

            Assert.Null(DragDropFileHelper.GetFirstAcceptedPath(
                transfer,
                DragDropFileHelper.ImageExtensions));
        }

        [Fact]
        public void FileDialogHelper_BackupPickerPreservesLegacyFilter()
        {
            var options = FileDialogHelper.CreateProblemReportBackupOpenOptions();
            var filter = Assert.Single(options.FileTypeFilter!);

            Assert.Equal("Select Backup File", options.Title);
            Assert.False(options.AllowMultiple);
            Assert.Equal("GBA ROMs", filter.Name);
            Assert.Equal(new[] { "*.gba", "*.bin" }, filter.Patterns);
        }

        [Fact]
        public void FileDialogHelper_SavPickerPreservesLegacyFilter()
        {
            var options = FileDialogHelper.CreateProblemReportSavOpenOptions();
            var filter = Assert.Single(options.FileTypeFilter!);

            Assert.Equal("Select SAV File", options.Title);
            Assert.False(options.AllowMultiple);
            Assert.Equal("SAV Files", filter.Name);
            Assert.Equal(new[] { "*.sav" }, filter.Patterns);
        }

        [Fact]
        public void FileDialogHelper_TranslationDataPickerPreservesNoFilter()
        {
            var options = FileDialogHelper.CreateSubtitleTranslationDataOpenOptions();

            Assert.Equal("Select Translation Data File", options.Title);
            Assert.False(options.AllowMultiple);
            Assert.Null(options.FileTypeFilter);
        }

        [Fact]
        public async Task FileDialogHelper_OpenResultReturnsNullOnCancel()
        {
            string? path = await FileDialogHelper.ResolveSingleOpenFileResultAsync(
                Array.Empty<IStorageFile>());

            Assert.Null(path);
        }

        [Fact]
        public async Task FileDialogHelper_OpenResultReturnsPickedPath()
        {
            string? path = await FileDialogHelper.ResolveSingleOpenFileResultAsync(
                new[] { CreateStorageFile("clean.gba") });

            Assert.NotNull(path);
            Assert.EndsWith("clean.gba", path!.Replace('/', '\\'));
        }

        [Fact]
        public void SkillConfigAnimePreview_MissingExportReturnsBlankFrames()
        {
            var preview = new SkillConfigAnimePreview();

            Assert.Equal(0, preview.FrameCount);
            Assert.Null(preview.TryGetFrameImage(0));
            Assert.Null(preview.TryGetFrameBitmap(0));
        }

        static IDataTransfer MakeTransfer(params IStorageItem[] files)
        {
            var transfer = new DataTransfer();
            foreach (IStorageItem file in files)
            {
                transfer.Add(DataTransferItem.CreateFile(file));
            }
            return transfer;
        }

        static IStorageFile CreateStorageFile(string fileName)
        {
            Type fileType = typeof(IStorageFile).Assembly.GetType("Avalonia.Platform.Storage.FileIO.BclStorageFile")
                ?? throw new InvalidOperationException("Avalonia BCL storage file implementation not found.");
            var fileInfo = new FileInfo(System.IO.Path.Combine(AppContext.BaseDirectory, "modern-input", fileName));
            return (IStorageFile)Activator.CreateInstance(
                fileType,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                args: new object[] { fileInfo },
                culture: null)!;
        }
    }
}
