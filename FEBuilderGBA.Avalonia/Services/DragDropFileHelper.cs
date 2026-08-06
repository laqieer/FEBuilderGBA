// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using global::Avalonia.Input;

namespace FEBuilderGBA.Avalonia.Services
{
    internal static class DragDropFileHelper
    {
        internal static readonly string[] ImageExtensions = new[] { ".png", ".bmp" };
        internal static readonly string[] RomPatchExtensions = new[] { ".gba", ".ups" };

        internal static bool HasAcceptedFile(IDataTransfer? dataTransfer, IReadOnlyCollection<string> acceptedExtensions)
            => GetFirstAcceptedPath(dataTransfer, acceptedExtensions) != null;

        internal static string? GetFirstAcceptedPath(IDataTransfer? dataTransfer, IReadOnlyCollection<string> acceptedExtensions)
        {
            if (dataTransfer == null || acceptedExtensions.Count == 0)
                return null;
            if (!dataTransfer.Contains(DataFormat.File))
                return null;

            var files = dataTransfer.TryGetFiles();
            if (files == null)
                return null;

            foreach (var file in files)
            {
                string path = file.Path.LocalPath;
                if (string.IsNullOrEmpty(path))
                    continue;

                string ext = Path.GetExtension(path).ToLowerInvariant();
                foreach (string accepted in acceptedExtensions)
                {
                    if (string.Equals(ext, accepted, StringComparison.OrdinalIgnoreCase))
                        return path;
                }
            }

            return null;
        }
    }
}
