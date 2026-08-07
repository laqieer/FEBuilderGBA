// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using global::Avalonia.Platform.Storage;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class ImageBGViewDragDropTests : IDisposable
    {
        readonly ROM? _prevRom = CoreState.ROM;
        readonly IAppServices? _prevServices = CoreState.Services;

        public void Dispose()
        {
            CoreState.ROM = _prevRom;
            CoreState.Services = _prevServices;
        }

        sealed class RecordingServices : IAppServices
        {
            public List<string> Errors { get; } = new();
            public void ShowError(string message) => Errors.Add(message);
            public void ShowInfo(string message) { }
            public bool ShowQuestion(string message) => true;
            public bool ShowYesNo(string message) => true;
            public void RunOnUIThread(Action action) => action();
            public bool IsMainThread() => true;
        }

        class FakeNonLocalStorageFileProxy : DispatchProxy
        {
            string? _name;
            Uri? _path;

            public void Initialize(string fileName)
            {
                _name = fileName;
                _path = new Uri($"content://drag-drop/{fileName}");
            }

            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            {
                string name = targetMethod?.Name ?? string.Empty;
                return name switch
                {
                    "get_Name" => _name,
                    "get_Path" => _path,
                    "get_CanBookmark" => false,
                    "GetBasicPropertiesAsync" => Task.FromResult(new StorageItemProperties(null, null, null)),
                    "SaveBookmarkAsync" => Task.FromResult<string?>(string.Empty),
                    "GetParentAsync" => Task.FromResult<IStorageFolder?>(null),
                    "DeleteAsync" => Task.CompletedTask,
                    "MoveAsync" => Task.FromResult<IStorageItem?>(null),
                    "OpenReadAsync" => Task.FromResult<Stream>(Stream.Null),
                    "OpenWriteAsync" => Task.FromResult<Stream>(Stream.Null),
                    _ => null,
                };
            }
        }

        static IDataTransfer MakeTransfer(params IStorageItem[] files)
        {
            var transfer = new DataTransfer();
            foreach (IStorageItem file in files)
                transfer.Add(DataTransferItem.CreateFile(file));
            return transfer;
        }

        static IStorageFile CreateNonLocalStorageFile(string fileName)
        {
            var proxy = DispatchProxy.Create<IStorageFile, FakeNonLocalStorageFileProxy>();
            ((FakeNonLocalStorageFileProxy)(object)proxy).Initialize(fileName);
            return proxy;
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

        static DragEventArgs MakeDragArgs(Interactive target, RoutedEvent<DragEventArgs> routedEvent, IDataTransfer transfer)
            => new(routedEvent, transfer, target, new Point(0, 0), KeyModifiers.None);

        static void InvokeDragHandler(object target, string methodName, DragEventArgs e)
        {
            var method = target.GetType().GetMethod(methodName,
                BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException($"Could not find {methodName}.");
            method.Invoke(target, new object?[] { target, e });
        }

        [AvaloniaFact]
        public void ImageBGView_DragOver_AcceptsLocalAndRejectsNonLocalFiles()
        {
            var view = new ImageBGView();

            var localArgs = MakeDragArgs(view, DragDrop.DragOverEvent,
                MakeTransfer(CreateStorageFile("portrait.png")));
            InvokeDragHandler(view, "OnDragOver", localArgs);
            Assert.Equal(DragDropEffects.Copy, localArgs.DragEffects);

            var nonLocalArgs = MakeDragArgs(view, DragDrop.DragOverEvent,
                MakeTransfer(CreateNonLocalStorageFile("portrait.png")));
            InvokeDragHandler(view, "OnDragOver", nonLocalArgs);
            Assert.Equal(DragDropEffects.None, nonLocalArgs.DragEffects);
        }

        [AvaloniaFact]
        public void ImageBGView_Drop_RejectedNonLocalFile_DoesNotHitPreImportGate()
        {
            var view = new ImageBGView();
            var services = new RecordingServices();
            CoreState.Services = services;
            CoreState.ROM = null;

            var args = MakeDragArgs(view, DragDrop.DropEvent,
                MakeTransfer(CreateNonLocalStorageFile("portrait.png")));
            InvokeDragHandler(view, "OnDrop", args);

            Assert.Empty(services.Errors);
        }
    }
}
