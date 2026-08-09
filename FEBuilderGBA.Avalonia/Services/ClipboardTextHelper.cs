// SPDX-License-Identifier: GPL-3.0-or-later
using System.Threading.Tasks;
using global::Avalonia.Input.Platform;

namespace FEBuilderGBA.Avalonia.Services
{
    internal static class ClipboardTextHelper
    {
        internal static Task<string?> TryGetTextAsync(IClipboard? clipboard)
        {
            return clipboard == null
                ? Task.FromResult<string?>(null)
                : clipboard.TryGetTextAsync();
        }
    }
}
