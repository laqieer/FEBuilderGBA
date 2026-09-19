using System.Security.Cryptography;
using System.Text;

namespace FEBuilderGBA.TestFixtures;

public static class SyntheticFe8URom
{
    public const string Format = "fe8u-synthetic-huffman-v1";
    public const uint TreeBase = 0x1000;
    public const uint RootNode = TreeBase + 8;
    public const uint RootReference = 0x1010;
    private const uint TextTable = 0x1800;
    private const uint EmptyText = 0x2000;
    private const uint SingleAText = 0x2004;

    public static byte[] Create()
    {
        byte[] data = new byte[16 * 1024 * 1024];
        Encoding.ASCII.GetBytes("BE8E01").CopyTo(data, 0xAC);
        var rom = new ROM();
        if (!rom.LoadFromBytes("zipdb-proof.gba", data, out _)
            || rom.RomInfo.VersionToFilename != "FE8U")
            throw new InvalidOperationException("Synthetic FE8U identity was not recognized.");

        // Construct detached fixture bytes without entering the caller's undo transaction.
        U.write_u32(data, TreeBase, 0x80000000);
        U.write_u32(data, TreeBase + 4, 0x80000041);
        U.write_u16(data, RootNode, 0);
        U.write_u16(data, RootNode + 2, 1);
        U.write_p32(data, rom.RomInfo.mask_pointer, TreeBase);
        U.write_p32(data, rom.RomInfo.mask_point_base_pointer, RootReference);
        U.write_p32(data, RootReference, RootNode);
        U.write_p32(data, rom.RomInfo.text_pointer, TextTable);
        U.write_p32(data, TextTable, EmptyText);
        U.write_p32(data, TextTable + 4, SingleAText);
        U.write_u8(data, SingleAText, 1); // LSB-first A followed by terminator.
        return data;
    }

    public sealed record Receipt(string Format, int Length, string Sha256);

    public static Receipt WriteNew(string destination)
    {
        string path = Path.GetFullPath(destination);
        if (!string.Equals(Path.GetFileName(path), "zipdb-proof.gba", StringComparison.Ordinal))
            throw new ArgumentException("Only the synthetic proof filename is permitted.");
        var directory = new DirectoryInfo(Path.GetDirectoryName(path)!);
        if (!directory.Exists)
            throw new DirectoryNotFoundException("The owned output directory must already exist.");
        if (!new[] { "android-patch-import-", "desktop-proof-", "fixture-test-" }
            .Any(prefix => directory.Name.StartsWith(prefix, StringComparison.Ordinal)))
            throw new ArgumentException("The destination must be an owned proof/test directory.");
        for (DirectoryInfo? ancestor = directory; ancestor != null; ancestor = ancestor.Parent)
            if ((ancestor.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Reparse-point output ancestry is not permitted.");
        byte[] data = Create();
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            stream.Write(data);
        return new Receipt(Format, data.Length,
            Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant());
    }
}
