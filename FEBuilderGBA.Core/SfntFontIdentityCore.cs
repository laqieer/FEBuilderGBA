// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Text;

namespace FEBuilderGBA.Core
{
    public sealed class SfntFontIdentity
    {
        public string Family { get; set; } = "";
        public string Version { get; set; } = "";
    }

    /// <summary>
    /// Bounded, allocation-light reader for the OpenType/TrueType <c>name</c>
    /// table.  It is used only to pin a declared family/version to the exact
    /// verified bytes; it never resolves or opens a platform font face.
    /// </summary>
    public static class SfntFontIdentityCore
    {
        const int MaxNameRecords = 4096;
        const int MaxNameBytes = 4096;
        static readonly Encoding Utf16Be =
            new UnicodeEncoding(true, false, true);
        static readonly Encoding MacintoshRoman =
            CreateMacintoshRoman();

        public static bool TryRead(
            byte[] fontBytes,
            out SfntFontIdentity identity,
            out string error)
        {
            identity = null;
            error = "";
            try
            {
                if (fontBytes == null || fontBytes.Length < 12)
                    throw new FormatException("Font bytes are truncated.");
                uint signature = U32(fontBytes, 0);
                if (signature != 0x00010000u
                    && signature != 0x4F54544Fu // OTTO
                    && signature != 0x74727565u // true
                    && signature != 0x74797031u) // typ1
                    throw new FormatException(
                        "Unsupported SFNT signature (collections/fallbacks are forbidden).");
                int tableCount = U16(fontBytes, 4);
                if (tableCount < 1 || tableCount > 4096
                    || 12L + tableCount * 16L > fontBytes.Length)
                    throw new FormatException("SFNT table directory is invalid.");

                int nameOffset = -1;
                int nameLength = 0;
                for (int i = 0; i < tableCount; i++)
                {
                    int record = 12 + i * 16;
                    if (U32(fontBytes, record) != 0x6E616D65u) continue;
                    uint offset = U32(fontBytes, record + 8);
                    uint length = U32(fontBytes, record + 12);
                    if (length < 6 || offset > int.MaxValue
                        || length > int.MaxValue
                        || (long)offset + length > fontBytes.Length)
                        throw new FormatException("SFNT name table is invalid.");
                    nameOffset = (int)offset;
                    nameLength = (int)length;
                    break;
                }
                if (nameOffset < 0)
                    throw new FormatException("SFNT name table is missing.");

                int count = U16(fontBytes, nameOffset + 2);
                int stringOffset = U16(fontBytes, nameOffset + 4);
                if (count < 1 || count > MaxNameRecords
                    || 6L + count * 12L > nameLength
                    || stringOffset < 6 || stringOffset > nameLength)
                    throw new FormatException("SFNT name records are invalid.");

                var families16 = new List<NameCandidate>();
                var families1 = new List<NameCandidate>();
                var versions = new List<NameCandidate>();
                for (int i = 0; i < count; i++)
                {
                    int record = nameOffset + 6 + i * 12;
                    int nameId = U16(fontBytes, record + 6);
                    if (nameId != 1 && nameId != 5 && nameId != 16)
                        continue;
                    int length = U16(fontBytes, record + 8);
                    int relativeOffset = U16(fontBytes, record + 10);
                    if (length < 1 || length > MaxNameBytes)
                        continue;
                    long start = (long)nameOffset
                        + stringOffset + relativeOffset;
                    if (start < nameOffset
                        || start + length > (long)nameOffset + nameLength)
                        throw new FormatException(
                            "SFNT name string is out of bounds.");
                    int platform = U16(fontBytes, record);
                    int encoding = U16(fontBytes, record + 2);
                    int language = U16(fontBytes, record + 4);
                    string value = Decode(
                        fontBytes, (int)start, length, platform, encoding);
                    if (string.IsNullOrWhiteSpace(value)) continue;
                    value = value.TrimEnd('\0');
                    var candidate = new NameCandidate(
                        value, Preference(platform, language), i);
                    if (nameId == 16) families16.Add(candidate);
                    else if (nameId == 1) families1.Add(candidate);
                    else versions.Add(candidate);
                }

                string family = Pick(
                    families16.Count != 0 ? families16 : families1);
                string version = Pick(versions);
                if (string.IsNullOrWhiteSpace(family)
                    || string.IsNullOrWhiteSpace(version))
                    throw new FormatException(
                        "SFNT family/version names are not technically obtainable.");
                identity = new SfntFontIdentity
                {
                    Family = family,
                    Version = version,
                };
                return true;
            }
            catch (Exception ex) when (
                ex is FormatException
                || ex is ArgumentException
                || ex is DecoderFallbackException
                || ex is OverflowException)
            {
                error = ex.Message;
                identity = null;
                return false;
            }
        }

        static string Decode(
            byte[] data,
            int offset,
            int length,
            int platform,
            int encoding)
        {
            if (platform == 0
                || (platform == 3
                    && (encoding == 0 || encoding == 1 || encoding == 10)))
            {
                if ((length & 1) != 0)
                    throw new FormatException(
                        "SFNT UTF-16 name has odd length.");
                return Utf16Be.GetString(data, offset, length);
            }
            if (platform == 1 && encoding == 0)
                return MacintoshRoman.GetString(data, offset, length);
            return "";
        }

        static Encoding CreateMacintoshRoman()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(
                10000,
                EncoderFallback.ExceptionFallback,
                DecoderFallback.ExceptionFallback);
        }

        static int Preference(int platform, int language)
        {
            if (platform == 3 && language == 0x0409) return 0;
            if (platform == 0) return 1;
            if (platform == 3) return 2;
            if (platform == 1) return 3;
            return 4;
        }

        static string Pick(List<NameCandidate> values)
        {
            values.Sort((a, b) =>
            {
                int byPreference = a.Preference.CompareTo(b.Preference);
                return byPreference != 0
                    ? byPreference : a.Ordinal.CompareTo(b.Ordinal);
            });
            return values.Count == 0 ? "" : values[0].Value;
        }

        static int U16(byte[] data, int offset)
        {
            if (offset < 0 || offset + 2 > data.Length)
                throw new FormatException("SFNT read is out of bounds.");
            return (data[offset] << 8) | data[offset + 1];
        }

        static uint U32(byte[] data, int offset)
        {
            if (offset < 0 || offset + 4 > data.Length)
                throw new FormatException("SFNT read is out of bounds.");
            return ((uint)data[offset] << 24)
                | ((uint)data[offset + 1] << 16)
                | ((uint)data[offset + 2] << 8)
                | data[offset + 3];
        }

        readonly struct NameCandidate
        {
            internal NameCandidate(
                string value,
                int preference,
                int ordinal)
            {
                Value = value;
                Preference = preference;
                Ordinal = ordinal;
            }

            internal string Value { get; }
            internal int Preference { get; }
            internal int Ordinal { get; }
        }
    }
}
