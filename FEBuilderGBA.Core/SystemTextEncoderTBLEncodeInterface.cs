using System.Collections.Generic;

namespace FEBuilderGBA
{
    public interface SystemTextEncoderTBLEncodeInterface
    {
        string Decode(byte[] str);
        string Decode(byte[] str, int start, int len);
        byte[] Encode(string str);

        Dictionary<string, uint> GetEncodeDicLow();
    }
}
