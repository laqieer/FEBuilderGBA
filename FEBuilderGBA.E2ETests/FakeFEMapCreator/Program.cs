using System.Text.Json;

static string? Option(string[] args, string name)
{
    for (int i = 0; i + 1 < args.Length; i++)
    {
        if (args[i] == name)
            return args[i + 1];
    }
    return null;
}

static uint Crc(byte[] data, int offset, int count)
{
    uint crc = 0xFFFFFFFFu;
    for (int i = 0; i < count; i++)
    {
        crc ^= data[offset + i];
        for (int bit = 0; bit < 8; bit++)
            crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
    }
    return crc ^ 0xFFFFFFFFu;
}

static void WriteU32(byte[] data, int offset, uint value)
{
    data[offset] = (byte)(value >> 24);
    data[offset + 1] = (byte)(value >> 16);
    data[offset + 2] = (byte)(value >> 8);
    data[offset + 3] = (byte)value;
}

static void EnsureAssets(string assetsRoot)
{
    Directory.CreateDirectory(assetsRoot);
    string pngPath = Path.Combine(assetsRoot, "e2e.png");
    if (!File.Exists(pngPath))
    {
        byte[] header = new byte[33];
        byte[] prefix =
        {
            137, 80, 78, 71, 13, 10, 26, 10,
            0, 0, 0, 13,
            (byte)'I', (byte)'H', (byte)'D', (byte)'R',
        };
        Array.Copy(prefix, header, prefix.Length);
        WriteU32(header, 16, 512);
        WriteU32(header, 20, 16);
        header[24] = 8;
        header[25] = 6;
        WriteU32(header, 29, Crc(header, 12, 17));
        File.WriteAllBytes(pngPath, header);
    }
    File.WriteAllBytes(Path.Combine(assetsRoot, "e2e.dat"), new byte[] { 0 });
}

string mode = Environment.GetEnvironmentVariable(
    "FEBUILDERGBA_FAKE_FEMAPCREATOR_MODE") ?? "success";
string assetsRoot = Path.GetFullPath(
    Option(args, "--assets-dir") ?? Path.Combine(AppContext.BaseDirectory, "assets"));

if (args.Length >= 2 && args[0] == "tilesets" && args[1] == "list")
{
    EnsureAssets(assetsRoot);
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        assetsRoot,
        tilesets = new[]
        {
            new
            {
                name = "E2E Tileset",
                imagePath = Path.Combine(assetsRoot, "e2e.png"),
                generationDataPath = Path.Combine(assetsRoot, "e2e.dat"),
                hasImage = true,
                hasGenerationData = true,
                diagnostic = "",
            },
        },
    }));
    return 0;
}

if (args.Length >= 1 && args[0] == "generate")
{
    string? recordPath = Environment.GetEnvironmentVariable(
        "FEBUILDERGBA_FAKE_FEMAPCREATOR_RECORD");
    if (!string.IsNullOrWhiteSpace(recordPath))
        File.WriteAllText(recordPath, Environment.CurrentDirectory);

    if (mode == "nonzero")
    {
        Console.Error.WriteLine("intentional external failure");
        return 7;
    }

    if (mode == "missing")
        return 0;

    if (!int.TryParse(Option(args, "--width"), out int width)
        || !int.TryParse(Option(args, "--height"), out int height)
        || width <= 0 || height <= 0)
    {
        return 2;
    }

    string? output = Option(args, "--output");
    if (string.IsNullOrWhiteSpace(output))
        return 3;

    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
    using var stream = new FileStream(output, FileMode.Create, FileAccess.Write, FileShare.None);
    using var writer = new BinaryWriter(stream);
    for (int i = 0; i < width * height; i++)
    {
        short raw = mode == "malformed" && i == 0
            ? (short)1
            : (short)((i % 16) * 32);
        writer.Write(raw);
    }
    return 0;
}

return 4;
