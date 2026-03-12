using System;
using System.Collections.Generic;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class TextViewerViewModel : ViewModelBase
    {
        uint _currentId;
        string _decodedText = "";
        string _editText = "";
        bool _canWrite;

        public uint CurrentId { get => _currentId; set => SetField(ref _currentId, value); }
        public string DecodedText { get => _decodedText; set => SetField(ref _decodedText, value); }
        public string EditText { get => _editText; set => SetField(ref _editText, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }

        public List<AddrResult> LoadTextList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.text_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x2000; i++) // reasonable max
            {
                uint entryAddr = (uint)(baseAddr + i * 4);
                if (entryAddr + 3 >= (uint)rom.Data.Length) break;

                uint textPtr = rom.u32(entryAddr);
                if (!U.isPointerOrNULL(textPtr)) break;

                string preview;
                try
                {
                    string decoded = FETextDecode.Direct(i);
                    if (decoded != null)
                        decoded = ConvertEscapeToFEditor(EscapeRawControlChars(decoded));
                    if (decoded != null)
                        decoded = StripControlChars(decoded);
                    if (decoded != null && decoded.Length > 40)
                        decoded = decoded.Substring(0, 40) + "...";
                    preview = decoded ?? "";
                }
                catch
                {
                    preview = "";
                }

                string name = U.ToHexString(i) + " " + preview;
                result.Add(new AddrResult(entryAddr, name, i));
            }
            return result;
        }

        public void LoadText(uint id)
        {
            CurrentId = id;
            try
            {
                string raw = FETextDecode.Direct(id) ?? "(empty)";
                DecodedText = ConvertEscapeToFEditor(EscapeRawControlChars(raw));
            }
            catch
            {
                DecodedText = "(decode error)";
            }
            CanWrite = true;
        }

        /// <summary>
        /// Convert @XXXX escape codes to human-readable [Name] format.
        /// Mirrors WinForms TextForm.ConvertEscapeToFEditor().
        /// </summary>
        static string ConvertEscapeToFEditor(string str)
        {
            // Handle @0010@0XXX (LoadFace with parameter) before table_replace
            str = RegexCache.Replace(str, @"@0010@0([0-9A-F][0-9A-F][0-9A-F])", "[LoadFace][0x$1]");
            // Convert known codes via table
            if (CoreState.TextEscape != null)
                str = CoreState.TextEscape.table_replace(str);
            // Convert remaining unknown @XXXX codes
            str = RegexCache.Replace(str, @"@([0-9A-F][0-9A-F][0-9A-F][0-9A-F])", "[0x$1]");
            return str;
        }

        /// <summary>
        /// Strip raw non-printable control characters (0x00-0x1F) that weren't
        /// converted to @XXXX escape codes by FETextDecode.
        /// Preserves \n and \r for display.
        /// </summary>
        static string StripControlChars(string str)
        {
            if (string.IsNullOrEmpty(str)) return str;
            var sb = new System.Text.StringBuilder(str.Length);
            foreach (char c in str)
            {
                if (c < 0x20 && c != '\n' && c != '\r')
                    continue; // skip raw control chars
                sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Convert raw control characters (0x00-0x1F) to @XXXX escape format
        /// so they can be processed by ConvertEscapeToFEditor and get proper
        /// names from the TextEscape table (e.g., 0x1F → @001F → [.]).
        /// </summary>
        static string EscapeRawControlChars(string str)
        {
            if (string.IsNullOrEmpty(str)) return str;
            var sb = new System.Text.StringBuilder(str.Length);
            foreach (char c in str)
            {
                if (c < 0x20 && c != '\n' && c != '\r')
                    sb.Append($"@{(int)c:X04}");
                else
                    sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Convert FEditor [Name] codes back to @XXXX escape format.
        /// Reverse of ConvertEscapeToFEditor(). Mirrors WinForms TextForm.ConvertFEditorToEscape().
        /// </summary>
        public static string ConvertFEditorToEscape(string str)
        {
            // Handle [LoadFace][0xXXX] → @0010@0XXX
            str = RegexCache.Replace(str, @"\[LoadFace\]\[0x00([0-9A-F][0-9A-F][0-9A-F])\]", "@0010@0$1");
            str = RegexCache.Replace(str, @"\[LoadFace\]\[0x([0-9A-F][0-9A-F][0-9A-F])\]", "@0010@0$1");
            // Convert named codes back via table
            if (CoreState.TextEscape != null)
                str = CoreState.TextEscape.table_replace_rev(str);
            // Strip [N] and [X] markers
            str = str.Replace("[N]", "");
            str = str.Replace("[X]", "");
            // Convert remaining [0xXXXX] codes back to @XXXX
            str = RegexCache.Replace(str, @"\[0x([0-9A-F])\]", "@000$1");
            str = RegexCache.Replace(str, @"\[0x([0-9A-F][0-9A-F])\]", "@00$1");
            str = RegexCache.Replace(str, @"\[0x([0-9A-F][0-9A-F][0-9A-F])\]", "@0$1");
            str = RegexCache.Replace(str, @"\[0x([0-9A-F][0-9A-F][0-9A-F][0-9A-F])\]", "@$1");
            return str;
        }

        /// <summary>
        /// Write edited text back to ROM for the given text ID.
        /// Converts FEditor format to escape codes, Huffman-encodes, and writes to ROM.
        /// </summary>
        public void WriteText(uint id, string text)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null)
                throw new InvalidOperationException("No ROM loaded.");
            if (CoreState.FETextEncoder == null)
                throw new InvalidOperationException("Text encoder not initialized.");

            // Convert FEditor display format back to internal escape codes
            string escaped = ConvertFEditorToEscape(text);

            // Huffman-encode
            byte[] encoded;
            bool useUnHuffman = false;
            string error = CoreState.FETextEncoder.Encode(escaped, out encoded);
            if (error != null && error.Length > 0)
            {
                // Huffman encoding failed — try UnHuffman fallback
                CoreState.FETextEncoder.UnHuffmanEncode(escaped, out encoded);
                useUnHuffman = true;
            }

            uint textBase = rom.p32(rom.RomInfo.text_pointer);
            if (!U.isSafetyOffset(textBase, rom))
                throw new InvalidOperationException("Invalid text pointer table.");

            uint writePointer = textBase + (id * 4);
            if (!U.isSafetyOffset(writePointer, rom))
                throw new InvalidOperationException($"Text ID 0x{id:X} out of range.");

            if (encoded == null || encoded.Length == 0)
            {
                // Empty text: point to same as text ID 0
                uint text0Pointer = rom.u32(textBase);
                rom.write_u32(writePointer, text0Pointer);
                return;
            }

            // Get original data size by decoding current text
            uint currentPointerValue = rom.u32(writePointer);
            uint originalSize = 0;
            bool currentIsUnHuffman = FETextEncode.IsUnHuffmanPatchPointer(currentPointerValue);
            uint currentDataAddr;
            if (currentIsUnHuffman)
                currentDataAddr = U.toOffset(FETextEncode.ConvertUnHuffmanPatchToPointer(currentPointerValue));
            else if (U.isPointer(currentPointerValue))
                currentDataAddr = U.toOffset(currentPointerValue);
            else
                currentDataAddr = 0;

            if (currentDataAddr > 0 && U.isSafetyOffset(currentDataAddr, rom))
            {
                try
                {
                    var decoder = new FETextDecode(rom, CoreState.SystemTextEncoder);
                    int dataSize;
                    if (currentIsUnHuffman)
                        decoder.UnHffmanPatchDecode(currentDataAddr, out dataSize);
                    else
                        decoder.Decode(id, out dataSize);
                    originalSize = (uint)Math.Max(0, dataSize);
                }
                catch
                {
                    originalSize = 0;
                }
            }

            if (originalSize > 20000)
            {
                originalSize = 0;
                currentDataAddr = 0;
            }

            if (currentDataAddr > 0 && originalSize >= (uint)encoded.Length)
            {
                // Fits in original space — overwrite in place
                rom.write_range(currentDataAddr, encoded);
                if (originalSize > (uint)encoded.Length)
                    rom.write_fill(currentDataAddr + (uint)encoded.Length, originalSize - (uint)encoded.Length, 0x00);
                if (useUnHuffman)
                    rom.write_u32(writePointer, FETextEncode.ConvertPointerToUnHuffmanPatchPointer(U.toPointer(currentDataAddr)));
                else
                    rom.write_u32(writePointer, U.toPointer(currentDataAddr));
            }
            else
            {
                // Need new space — append to ROM end
                uint paddedSize = U.Padding4((uint)encoded.Length) + 4;
                uint newAddr = U.Padding4((uint)rom.Data.Length);

                if (newAddr + paddedSize >= 0x02000000)
                    throw new InvalidOperationException("ROM would exceed 32MB limit.");

                rom.write_resize_data(newAddr + paddedSize);

                // Clear old data if valid
                if (currentDataAddr > 0 && originalSize > 0 && U.isSafetyOffset(currentDataAddr, rom))
                    rom.write_fill(currentDataAddr, originalSize, 0x00);

                rom.write_range(newAddr, encoded);

                if (useUnHuffman)
                    rom.write_u32(writePointer, FETextEncode.ConvertPointerToUnHuffmanPatchPointer(U.toPointer(newAddr)));
                else
                    rom.write_p32(writePointer, newAddr);
            }
        }

        /// <summary>
        /// Search all text entries for content containing the given query.
        /// Returns a filtered list of matching entries.
        /// </summary>
        public List<AddrResult> SearchTexts(string query)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null || string.IsNullOrWhiteSpace(query))
                return new List<AddrResult>();

            uint ptr = rom.RomInfo.text_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x2000; i++)
            {
                uint entryAddr = (uint)(baseAddr + i * 4);
                if (entryAddr + 3 >= (uint)rom.Data.Length) break;

                uint textPtr = rom.u32(entryAddr);
                if (!U.isPointerOrNULL(textPtr)) break;

                try
                {
                    string decoded = FETextDecode.Direct(i);
                    if (decoded == null) continue;
                    decoded = ConvertEscapeToFEditor(EscapeRawControlChars(decoded));

                    if (decoded.Contains(query, StringComparison.OrdinalIgnoreCase))
                    {
                        string preview = StripControlChars(decoded);
                        if (preview != null && preview.Length > 40)
                            preview = preview.Substring(0, 40) + "...";
                        result.Add(new AddrResult(entryAddr, $"{U.ToHexString(i)} {preview}", i));
                    }
                }
                catch { }
            }
            return result;
        }

        public int GetListCount() => LoadTextList().Count;

        /// <summary>
        /// Export all ROM texts to a TSV file via TranslateCore.
        /// </summary>
        public int ExportAllTexts(string path)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return 0;

            var entries = TranslateCore.DumpTexts(rom);
            TranslateCore.ExportToTSV(entries, path);
            return entries.Count;
        }

        /// <summary>
        /// Import texts from a TSV file and write them back to ROM.
        /// </summary>
        public int ImportAllTexts(string path)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return 0;

            var entries = TranslateCore.ImportFromTSV(path);
            if (entries.Count == 0) return 0;

            return TranslateCore.WriteTexts(rom, entries);
        }
    }
}
