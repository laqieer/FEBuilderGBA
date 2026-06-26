using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace FEBuilderGBA
{
    /// <summary>
    /// Pure (GUI-free) clipboard serialization + structural-edit byte helpers for the
    /// shared address-list editors (#1539). Ports the WinForms
    /// <c>InputFormRef.CopyToClipbord</c> / <c>ClipbordToPaste</c> / <c>SwapData</c> /
    /// <c>ClearData</c> data shapes so the Avalonia <c>AddressListControl</c>'s opt-in
    /// structural-edit context menu (Copy / Paste / Swap / Invalidate) is byte-for-byte
    /// interoperable with WinForms — a row copied in one app can be pasted in the other.
    ///
    /// <para>
    /// Clipboard wire format (verbatim WinForms): the first space-separated token is the
    /// identity header <c>"{listName}@{formName}"</c>, followed by exactly <c>blockSize</c>
    /// hex byte tokens written with <c>ToString("X")</c> (uppercase, NO leading zero —
    /// e.g. <c>0x0F</c> is <c>"F"</c>, not <c>"0F"</c>). Paste validates the token count
    /// (<c>blockSize + 1</c>) and the exact header before accepting.
    /// </para>
    /// </summary>
    public static class AddressListClipboardCore
    {
        /// <summary>
        /// Serialize a row's <paramref name="block"/> bytes to the WinForms clipboard
        /// format: <c>"{listName}@{formName}"</c> then one space-prefixed
        /// <c>ToString("X")</c> token per byte. Mirrors
        /// <c>InputFormRef.CopyToClipbord</c> exactly.
        /// </summary>
        public static string Serialize(string listName, string formName, byte[] block)
        {
            var sb = new StringBuilder();
            sb.Append(listName ?? string.Empty);
            sb.Append('@');
            sb.Append(formName ?? string.Empty);
            if (block != null)
            {
                for (int i = 0; i < block.Length; i++)
                {
                    sb.Append(' ');
                    // ToString("X") — uppercase, no leading zero (WF parity, NOT "X2").
                    sb.Append(block[i].ToString("X"));
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// The exact identity header (<c>"{listName}@{formName}"</c>) that
        /// <see cref="TryParse"/> requires as the clipboard text's first token.
        /// </summary>
        public static string BuildHeader(string listName, string formName)
            => (listName ?? string.Empty) + "@" + (formName ?? string.Empty);

        /// <summary>
        /// Parse clipboard <paramref name="text"/> produced by <see cref="Serialize"/>
        /// (or by WinForms). Succeeds only when:
        /// <list type="bullet">
        ///   <item>the text splits into exactly <c>blockSize + 1</c> space tokens
        ///         (mirrors WF <c>sp.Length != BlockSize + 1</c> reject);</item>
        ///   <item>the first token equals <c>"{listName}@{formName}"</c>;</item>
        ///   <item>every remaining token is a valid hex byte (0..0xFF).</item>
        /// </list>
        /// Unlike WinForms (which uses <c>U.atoh</c> and silently maps invalid/overflow
        /// tokens to 0 / truncates), this rejects non-hex and &gt;0xFF tokens outright —
        /// returning <c>false</c> with no <paramref name="block"/> — while still accepting
        /// any valid WF output (Copilot plan review #5). Never throws.
        /// </summary>
        public static bool TryParse(string text, string listName, string formName, int blockSize, out byte[] block)
        {
            block = Array.Empty<byte>();
            if (text == null || blockSize < 0)
                return false;

            string[] sp = text.Split(' ');
            // WF: reject unless token count is exactly blockSize + 1 (header + N bytes).
            if (sp.Length != blockSize + 1)
                return false;

            if (sp[0] != BuildHeader(listName, formName))
                return false;

            var data = new byte[blockSize];
            for (int i = 0; i < blockSize; i++)
            {
                string token = sp[i + 1];
                // Strict: each token must be a hex byte in [0, 0xFF]. byte.TryParse with
                // HexNumber rejects non-hex chars, empty strings, and values > 0xFF —
                // closing the U.atoh silent-truncation gap.
                if (!byte.TryParse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
                    return false;
                data[i] = b;
            }
            block = data;
            return true;
        }

        /// <summary>
        /// The zero-filled block written by the Invalidate (Clear) action — mirrors WF
        /// <c>ClearData</c>'s <c>new byte[this.BlockSize]</c> (the cleared row becomes the
        /// list terminator). Returns an empty array for non-positive sizes.
        /// </summary>
        public static byte[] BuildCleared(int blockSize)
        {
            if (blockSize <= 0)
                return Array.Empty<byte>();
            return new byte[blockSize];
        }

        /// <summary>
        /// Apply the WinForms <c>SwapData</c> ROM mutation: write block <paramref name="b"/>'s
        /// bytes over address <paramref name="addrA"/> and block <paramref name="a"/>'s bytes
        /// over address <paramref name="addrB"/> (a crossed write of two equal-length blocks),
        /// using the supplied write delegate so this stays GUI/ROM-agnostic and unit-testable.
        /// Returns false (no writes) when the inputs are null/length-mismatched.
        /// </summary>
        public static bool BuildSwap(byte[] a, byte[] b, out byte[] newAtAddrA, out byte[] newAtAddrB)
        {
            newAtAddrA = Array.Empty<byte>();
            newAtAddrB = Array.Empty<byte>();
            if (a == null || b == null || a.Length != b.Length || a.Length == 0)
                return false;
            // After the swap, addrA holds b's old bytes and addrB holds a's old bytes.
            newAtAddrA = (byte[])b.Clone();
            newAtAddrB = (byte[])a.Clone();
            return true;
        }
    }
}
