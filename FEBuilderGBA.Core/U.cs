using System;
using System.Collections.Generic;
using System.Text;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace FEBuilderGBA
{
    // Core utility methods — pure logic, no UI/Drawing dependencies.
    // WinForms has its own U class (internal) that shadows this one via CS0436.
    // Tests see this internal U transitively through WinForms → Core reference
    // (InternalsVisibleTo is configured in the Core csproj).
    internal static class U
    {
        public const uint NOT_FOUND = 0xFFFFFFFF;

        // ---- Safe array access ------------------------------------------------
        public static string at(string[] list, uint at, string def = "")
        {
            if (at >= list.Length) return def;
            return list[(int)at];
        }
        public static uint at(uint[] list, int at, uint def = 0)
        {
            if (at >= list.Length || at < 0) return def;
            return list[at];
        }
        public static string at(string[] list, int at, string def = "")
        {
            if (at >= list.Length || at < 0) return def;
            return list[at];
        }
        public static string at(List<string> list, uint at, string def = "")
        {
            if (at >= list.Count) return def;
            return list[(int)at];
        }
        public static string at(List<string> list, int at, string def = "")
        {
            if (at >= list.Count || at < 0) return def;
            return list[at];
        }
        public static string at(Dictionary<string, string> dic, string at, string def = "")
        {
            string a;
            if (!dic.TryGetValue(at, out a)) return def;
            return a;
        }
        public static string at(Dictionary<uint, string> dic, uint at, string def = "")
        {
            string a;
            if (!dic.TryGetValue(at, out a)) return def;
            return a;
        }
        public static string at(Dictionary<uint, string> dic, int at, string def = "")
        {
            return U.at(dic, (uint)at, def);
        }
        public static uint at(Dictionary<uint, uint> dic, uint at, uint def = 0)
        {
            uint a;
            if (!dic.TryGetValue(at, out a)) return def;
            return a;
        }
        [MethodImpl(256)]
        public static byte at(byte[] list, int at, byte def = 0)
        {
            if (at >= list.Length) return def;
            return list[at];
        }
        [MethodImpl(256)]
        public static byte at(byte[] list, uint at, byte def = 0)
        {
            if (at >= list.Length) return def;
            return list[at];
        }
        [MethodImpl(256)]
        public static bool IsEmpty(string str)
        {
            return string.IsNullOrEmpty(str);
        }

        // ---- Number parsing ---------------------------------------------------
        public static double atof(String a)
        {
            for (int i = 0; i < a.Length; i++)
            {
                if (!isnum_f(a[i])) { a = a.Substring(0, i); break; }
            }
            IFormatProvider ifp = System.Globalization.CultureInfo.CreateSpecificCulture("en-GB");
            System.Globalization.NumberStyles ns = System.Globalization.NumberStyles.Float;
            double ret = 0;
            if (!Double.TryParse(a, ns, ifp, out ret)) return 0;
            return ret;
        }
        public static uint atoi(String a)
        {
            for (int i = 0; i < a.Length; i++)
            {
                if (!isnum(a[i])) { a = a.Substring(0, i); break; }
            }
            int ret = 0;
            if (!int.TryParse(a, out ret)) return 0;
            return (uint)ret;
        }
        public static uint atou(String a)
        {
            for (int i = 0; i < a.Length; i++)
            {
                if (!isnum(a[i])) { a = a.Substring(0, i); break; }
            }
            uint ret = 0;
            if (!uint.TryParse(a, out ret)) return 0;
            return ret;
        }
        public static uint atoh(String a)
        {
            for (int i = 0; i < a.Length; i++)
            {
                if (!ishex(a[i])) { a = a.Substring(0, i); break; }
            }
            int ret = 0;
            if (!int.TryParse(a, System.Globalization.NumberStyles.HexNumber, null, out ret)) return 0;
            return (uint)ret;
        }
        public static uint atoi0x(String a)
        {
            if (a.Length >= 2 && a[0] == '0' && a[1] == 'x') return atoh(a.Substring(2));
            if (a.Length >= 1 && a[0] == '$') return atoh(a.Substring(1));
            return atoi(a);
        }

        // ---- Character classification -----------------------------------------
        public static bool isnum_f(byte a) { return isnum_f((char)a); }
        public static bool isnum_f(char a) { return ((a >= '0' && a <= '9') || a == '.'); }
        public static bool isnum(byte a) { return isnum((char)a); }
        public static bool isnum(char a) { return (a >= '0' && a <= '9'); }
        public static bool ishex(byte a) { return ishex((char)a); }
        public static bool ishex(char a) { return (a >= '0' && a <= '9') || (a >= 'a' && a <= 'f') || (a >= 'A' && a <= 'F'); }
        public static bool isalhpa(byte a) { return isalhpa((char)a); }
        public static bool isalhpa(char a) { return ((a >= 'a' && a <= 'z') || (a >= 'A' && a <= 'Z')); }
        public static bool isalhpanum(byte a) { return isalhpanum((char)a); }
        public static bool isalhpanum(char a)
        {
            return (a >= 'a' && a <= 'z') || (a >= 'A' && a <= 'Z') || (a >= '0' && a <= '9');
        }
        public static bool isAscii(byte a) { return (a >= 0x20 && a <= 0x7e); }

        public static bool isAlphaNumString(string str)
        {
            for (int i = 0; i < str.Length; i++)
            {
                if (!((str[i] >= '0' && str[i] <= '9') || (str[i] >= 'a' && str[i] <= 'z') || (str[i] >= 'A' && str[i] <= 'Z') || (str[i] == '\0')))
                    return false;
            }
            return true;
        }
        public static bool isAsciiString(string str)
        {
            for (int i = 0; i < str.Length; i++)
            {
                char c = str[i];
                if (c >= 0x7f) return false;
                if (c >= 0x01 && c <= 0x1f)
                {
                    if (c == 0x09 || c == 0x0a || c == 0x0d) { } else return false;
                }
            }
            return true;
        }
        public static bool isAlphaString(string str)
        {
            for (int i = 0; i < str.Length; i++)
            {
                if (!((str[i] >= 'a' && str[i] <= 'z') || (str[i] >= 'A' && str[i] <= 'Z') || (str[i] == '\0')))
                    return false;
            }
            return true;
        }
        public static bool isHexString(string str)
        {
            for (int i = 0; i < str.Length; i++)
            {
                if (!((str[i] >= '0' && str[i] <= '9') || (str[i] >= 'a' && str[i] <= 'f') || (str[i] >= 'A' && str[i] <= 'F') || (str[i] == '\0')))
                    return false;
            }
            return true;
        }
        public static bool isNumString(string str)
        {
            for (int i = 0; i < str.Length; i++)
            {
                if (!((str[i] >= '0' && str[i] <= '9') || (str[i] == '\0'))) return false;
            }
            return true;
        }
        public static bool stringbool(string s)
        {
            string ss = s.ToLower().Trim();
            if (ss == "false") return false;
            if (ss == "0") return false;
            if (ss == "no") return false;
            return true;
        }
        public static bool IsValueOdd(uint addr)
        {
            return (addr & 0x1) == 0x1;
        }
        public static bool isEven(int size) { return (size & 1) == 0; }
        public static bool isEven(uint size) { return (size & 1) == 0; }

        // ---- Hex formatting ---------------------------------------------------
        public static string ToHexString(decimal a) { return ToHexString((uint)a); }
        public static string ToHexString(int a)
        {
            if (a <= 0xff) return a.ToString("X02");
            if (a <= 0xffff) return a.ToString("X04");
            if (a <= 0x7fffffff) return a.ToString("X08");
            return "???";
        }
        public static string ToHexString(uint a)
        {
            if (a <= 0xff) return a.ToString("X02");
            if (a <= 0xffff) return a.ToString("X04");
            if (a <= 0xffffff) return a.ToString("X06");
            if (a <= 0xffffffff) return a.ToString("X08");
            return "???";
        }
        public static string ToHexString8(int a) { return a.ToString("X08"); }
        public static string ToHexString8(uint a) { return a.ToString("X08"); }
        public static string ToHexString4(int a) { return a.ToString("X04"); }
        public static string ToHexString4(uint a) { return a.ToString("X04"); }
        public static string ToHexString2(int a) { return a.ToString("X02"); }
        public static string ToHexString2(uint a) { return a.ToString("X02"); }
        public static string To0xHexString(uint a) { return "0x" + ToHexString(a); }
        public static string To0xHexString(int a) { return "0x" + ToHexString(a); }
        public static string ToHexStringTrim0(uint a) { return a.ToString("X08").TrimStart(new char[] { '0' }); }

        // ---- Padding / Alignment ----------------------------------------------
        [MethodImpl(256)]
        public static bool isPadding4(uint a) { return a % 4 == 0; }
        public static uint SubPadding4(uint p)
        {
            uint mod = p % 4;
            return mod == 0 ? p : p - mod;
        }
        [MethodImpl(256)]
        public static uint Padding2(uint p) { return ((p & 0x01) == 0x01) ? p + 1 : p; }
        [MethodImpl(256)]
        public static uint Padding2Before(uint p) { return ((p & 0x01) == 0x01) ? p - 1 : p; }
        [MethodImpl(256)]
        public static uint Padding4(uint p) { uint mod = p % 4; return mod == 0 ? p : p + (4 - mod); }
        [MethodImpl(256)]
        public static int Padding4(int p) { int mod = p % 4; return mod == 0 ? p : p + (4 - mod); }
        public static uint Padding8(uint p) { uint mod = p % 8; return mod == 0 ? p : p + (8 - mod); }
        public static int Padding8(int p) { int mod = p % 8; return mod == 0 ? p : p + (8 - mod); }
        public static uint Padding16(uint p) { uint mod = p % 16; return mod == 0 ? p : p + (16 - mod); }
        public static int Padding16(int p) { int mod = p % 16; return mod == 0 ? p : p + (16 - mod); }

        // ---- Byte array read (little-endian) ----------------------------------
        [MethodImpl(256)]
        public static uint u32(byte[] data, uint addr)
        {
            check_safety(data, addr + 4);
            return data[addr] + ((uint)data[addr + 1] << 8) + ((uint)data[addr + 2] << 16) + ((uint)data[addr + 3] << 24);
        }
        public static uint u24(byte[] data, uint addr)
        {
            check_safety(data, addr + 3);
            return data[addr] + ((uint)data[addr + 1] << 8) + ((uint)data[addr + 2] << 16);
        }
        [MethodImpl(256)]
        public static uint u16(byte[] data, uint addr)
        {
            check_safety(data, addr + 2);
            return data[addr] + ((uint)data[addr + 1] << 8);
        }
        [MethodImpl(256)]
        public static uint u8(byte[] data, uint addr)
        {
            check_safety(data, addr + 1);
            return data[addr];
        }
        [MethodImpl(256)]
        public static uint u4(byte[] data, uint addr, bool isHigh)
        {
            check_safety(data, addr + 1);
            return isHigh ? (uint)((data[addr] >> 4) & 0xf) : (uint)(data[addr] & 0xf);
        }
        [MethodImpl(256)]
        public static uint p32(byte[] data, uint addr)
        {
            uint a = U.u32(data, addr);
            return U.toOffset(a);
        }

        // ---- Byte array read (big-endian) -------------------------------------
        public static uint big32(byte[] data, uint addr)
        {
            check_safety(data, addr + 4);
            return data[addr + 3] + ((uint)data[addr + 2] << 8) + ((uint)data[addr + 1] << 16) + ((uint)data[addr + 0] << 24);
        }
        public static uint big24(byte[] data, uint addr)
        {
            check_safety(data, addr + 3);
            return data[addr + 2] + ((uint)data[addr + 1] << 8) + ((uint)data[addr + 0] << 16);
        }
        public static uint big16(byte[] data, uint addr)
        {
            check_safety(data, addr + 2);
            return data[addr + 1] + ((uint)data[addr + 0] << 8);
        }
        public static uint big8(byte[] data, uint addr)
        {
            check_safety(data, addr);
            return data[addr];
        }

        // ---- Byte array write (little-endian) ---------------------------------
        public static void write_u32(byte[] data, uint addr, uint a)
        {
            check_safety(data, addr + 4);
            data[addr] = (byte)(a & 0xFF);
            data[addr + 1] = (byte)((a & 0xFF00) >> 8);
            data[addr + 2] = (byte)((a & 0xFF0000) >> 16);
            data[addr + 3] = (byte)((a & 0xFF000000) >> 24);
        }
        public static void write_u24(byte[] data, uint addr, uint a)
        {
            check_safety(data, addr + 3);
            data[addr] = (byte)(a & 0xFF);
            data[addr + 1] = (byte)((a & 0xFF00) >> 8);
            data[addr + 2] = (byte)((a & 0xFF0000) >> 16);
        }
        public static void write_u16(byte[] data, uint addr, uint a)
        {
            check_safety(data, addr + 2);
            data[addr] = (byte)(a & 0xFF);
            data[addr + 1] = (byte)((a & 0xFF00) >> 8);
        }
        public static void write_u8(byte[] data, uint addr, uint a)
        {
            check_safety(data, addr + 1);
            data[addr] = (byte)a;
        }
        public static void write_u4(byte[] data, uint addr, uint a, bool isHigh)
        {
            check_safety(data, addr + 1);
            if (isHigh)
                data[addr] = (byte)((byte)(data[addr] & 0xf) | (byte)((a & 0xf) << 4));
            else
                data[addr] = (byte)((byte)(data[addr] & 0xf0) | (byte)(a & 0xf));
        }
        public static void write_p32(byte[] data, uint addr, uint a)
        {
            write_u32(data, addr, U.toPointer(a));
        }

        // ---- Byte array write (big-endian) ------------------------------------
        public static void write_big32(byte[] data, uint addr, uint a)
        {
            check_safety(data, addr + 4);
            data[addr + 0] = (byte)((a & 0xFF000000) >> 24);
            data[addr + 1] = (byte)((a & 0xFF0000) >> 16);
            data[addr + 2] = (byte)((a & 0xFF00) >> 8);
            data[addr + 3] = (byte)(a & 0xFF);
        }
        public static void write_big24(byte[] data, uint addr, uint a)
        {
            check_safety(data, addr + 3);
            data[addr + 0] = (byte)((a & 0xFF0000) >> 16);
            data[addr + 1] = (byte)((a & 0xFF00) >> 8);
            data[addr + 2] = (byte)(a & 0xFF);
        }
        public static void write_big16(byte[] data, uint addr, uint a)
        {
            check_safety(data, addr + 2);
            data[addr + 0] = (byte)((a & 0xFF00) >> 8);
            data[addr + 1] = (byte)(a & 0xFF);
        }

        // ---- Byte array bulk operations ---------------------------------------
        public static void write_range(byte[] data, uint addr, byte[] write_data)
        {
            check_safety(data, addr + (uint)write_data.Length);
            Array.Copy(write_data, 0, data, addr, write_data.Length);
        }
        public static void write_fill(byte[] data, uint addr, uint length, byte fill = 0x00)
        {
            for (uint i = 0; i < length; i++) data[addr + i] = fill;
        }
        public static byte[] FillArray(uint size, byte fill)
        {
            byte[] b = new byte[size];
            for (int i = 0; i < size; i++) b[i] = fill;
            return b;
        }
        public static byte[] getBinaryData(byte[] data, uint addr, int count)
        {
            if (count < 0)
            {
                Debug.Assert(false);
                return new byte[0];
            }
            return getBinaryData(data, addr, (uint)count);
        }
        public static byte[] getBinaryData(byte[] data, uint addr, uint count)
        {
            if (data.Length <= addr + count)
            {
                if (data.Length == 0) return new byte[0];
                if (addr >= data.Length - 1) addr = (uint)data.Length - 1;
                count = (uint)(data.Length) - addr;
            }
            check_safety(data, addr + count);
            byte[] ret = new byte[count];
            Array.Copy(data, addr, ret, 0, count);
            return ret;
        }

        // ---- Subrange / slice -------------------------------------------------
        public static string[] subrange(string[] data, int s, int e) { return subrange(data, (uint)s, (uint)e); }
        public static string[] subrange(string[] data, uint s, uint e)
        {
            s = Math.Min(s, (uint)data.Length);
            e = Math.Min(e, (uint)data.Length);
            if (e <= s) return new string[0];
            string[] d = new string[e - s];
            Array.Copy(data, s, d, 0, e - s);
            return d;
        }
        public static byte[] subrange(byte[] data, int s, int e) { return subrange(data, (uint)s, (uint)e); }
        public static byte[] subrange(byte[] data, uint s, uint e)
        {
            s = Math.Min(s, (uint)data.Length);
            e = Math.Min(e, (uint)data.Length);
            if (e <= s) return new byte[0];
            byte[] d = new byte[e - s];
            Array.Copy(data, s, d, 0, e - s);
            return d;
        }
        public static List<byte> subrangeToList(byte[] data, uint s, uint e)
        {
            s = Math.Min(s, (uint)data.Length);
            e = Math.Min(e, (uint)data.Length);
            if (e <= s) return new List<byte>();
            List<byte> ret = new List<byte>();
            for (uint i = s; i < e; i++) ret.Add(data[i]);
            return ret;
        }
        public static List<byte> subrange(List<byte> data, uint s, uint e)
        {
            s = Math.Min(s, (uint)data.Count);
            e = Math.Min(e, (uint)data.Count);
            if (e <= s) return new List<byte>();
            List<byte> ret = new List<byte>();
            for (uint i = s; i < e; i++) ret.Add(data[(int)i]);
            return ret;
        }
        public static byte[] del(byte[] data, uint s, uint e)
        {
            s = Math.Min(s, (uint)data.Length);
            e = Math.Min(e, (uint)data.Length);
            Debug.Assert(s < e);
            byte[] d = new byte[data.Length - (e - s)];
            Array.Copy(data, 0, d, 0, s);
            Array.Copy(data, e, d, s, data.Length - e);
            return d;
        }
        public static byte[] ArrayAppend(byte[] a, byte[] b)
        {
            byte[] r = new byte[a.Length + b.Length];
            Array.Copy(a, 0, r, 0, a.Length);
            Array.Copy(b, 0, r, a.Length, b.Length);
            return r;
        }
        public static byte[] ArrayInsert(byte[] a, int pos, byte[] b)
        {
            Debug.Assert(pos < a.Length);
            byte[] r = new byte[a.Length + b.Length];
            Array.Copy(a, 0, r, 0, pos);
            Array.Copy(b, 0, r, pos, b.Length);
            Array.Copy(a, pos, r, pos + b.Length, a.Length - pos);
            return r;
        }

        // ---- Endian conversion ------------------------------------------------
        public static uint ChangeEndian16(uint a)
        {
            return ((uint)(a & 0xFF) << 8) + ((a & 0xFF00) >> 8);
        }
        public static uint ChangeEndian32(uint a)
        {
            return (((a & 0xFF) << 24) + ((a & 0xFF00) << 8) + ((a & 0xFF0000) >> 8) + ((a & 0xFF000000) >> 24));
        }

        // ---- String extraction ------------------------------------------------
        public static String getASCIIString(byte[] data, uint addr, int length)
        {
            if (length <= 0) return "";
            byte[] d = U.getBinaryData(data, addr, length);
            string str = System.Text.Encoding.GetEncoding("ASCII").GetString(d);
            return str.TrimEnd('\0');
        }
        public static string getASCIIString(byte[] data, uint addr)
        {
            for (uint i = addr; i < data.Length; i++)
            {
                if (data[i] == 0) return getASCIIString(data, addr, (int)(i - addr));
            }
            return "";
        }
        public static String convertByteToStringDump(byte[] data)
        {
            String bin = "";
            for (uint i = 0; i < data.Length; i++) bin += u8(data, i).ToString("X02");
            return bin;
        }
        public static byte[] convertStringDumpToByte(string d)
        {
            byte[] r = new byte[d.Length / 2];
            Array.Clear(r, 0, r.Length);
            int length = r.Length * 2;
            for (int len = 0; len < length; len++)
            {
                if ((d[len] >= '0' && d[len] <= '9'))
                    U.write_u4(r, (uint)(len / 2), (uint)(d[len] - '0'), (len % 2) == 0);
                else if ((d[len] >= 'a' && d[len] <= 'f'))
                    U.write_u4(r, (uint)(len / 2), (uint)(d[len] - 'a' + 10), (len % 2) == 0);
                else if ((d[len] >= 'A' && d[len] <= 'F'))
                    U.write_u4(r, (uint)(len / 2), (uint)(d[len] - 'A' + 10), (len % 2) == 0);
                else
                    break;
            }
            return r;
        }

        // ---- GBA Pointer checks -----------------------------------------------
        [MethodImpl(256)]
        public static bool isPointerASM(uint a)
        {
            return (a >= 0x08000000 && a < 0x0A000000) && U.IsValueOdd(a);
        }
        [MethodImpl(256)]
        public static bool isPointerASMOrNull(uint a)
        {
            if (a == 0) return true;
            return (a >= 0x08000000 && a < 0x0A000000) && U.IsValueOdd(a);
        }
        [MethodImpl(256)]
        public static bool isPointer(uint a)
        {
            return (a >= 0x08000000 && a < 0x0A000000);
        }
        [MethodImpl(256)]
        public static bool isPointerOrNULL(uint a)
        {
            return U.isPointer(a) || a == 0x0;
        }
        [MethodImpl(256)]
        public static bool isOffset(uint a)
        {
            return (a < 0x02000000 && a >= 0x00000000);
        }
        [MethodImpl(256)]
        public static uint toOffset(uint a)
        {
            if (a <= 1) return a;
            if (U.isPointer(a)) return a - 0x08000000;
            return a;
        }
        [MethodImpl(256)]
        public static uint toOffset(decimal a) { return toOffset((uint)a); }
        [MethodImpl(256)]
        public static uint toPointer(uint a)
        {
            if (a <= 1) return a;
            if (U.isOffset(a)) return a + 0x08000000;
            return a;
        }
        public static bool is_RAMPointer(uint a) { return is_03RAMPointer(a) || is_02RAMPointer(a); }
        public static bool is_ROMorRAMPointer(uint a) { return isPointer(a) || is_03RAMPointer(a) || is_02RAMPointer(a); }
        public static bool is_ROMorRAMPointerOrNULL(uint a) { return isPointerOrNULL(a) || is_03RAMPointer(a) || is_02RAMPointer(a); }
        public static bool is_03RAMPointer(uint a) { return (a >= 0x03000000 && a < 0x03007FFF); }
        public static bool is_02RAMPointer(uint a) { return (a >= 0x02000000 && a < 0x0203FFFF); }
        public static bool is_0EDiskPointer(uint a) { return (a >= 0x0E000000 && a < 0x0E008000); }
        public static bool isROMPointer(uint a) { return isPointer(a); }

        // Safety checks with explicit ROM parameter (no global state needed)
        [MethodImpl(256)]
        public static bool isSafetyOffset(uint a, ROM rom)
        {
            return (a < 0x02000000 && a >= 0x00000200 && a < rom.Data.Length);
        }
        [MethodImpl(256)]
        public static bool isSafetyPointer(uint a, ROM rom)
        {
            return (a < 0x0A000000 && a >= 0x08000200 && a - 0x08000000 < rom.Data.Length);
        }

        // Safety checks using CoreState.ROM (for backward compat with Program.ROM)
        [MethodImpl(256)]
        public static bool isSafetyOffset(uint a)
        {
            return (a < 0x02000000 && a >= 0x00000200 && a < CoreState.ROM.Data.Length);
        }
        [MethodImpl(256)]
        public static bool isSafetyPointer(uint a)
        {
            return (a < 0x0A000000 && a >= 0x08000200 && a - 0x08000000 < CoreState.ROM.Data.Length);
        }
        [MethodImpl(256)]
        public static bool isSafetyPointerOrNull(uint a)
        {
            return (a == 0) || isSafetyPointer(a);
        }
        public static bool isSafetyZArray(uint a)
        {
            return (a < CoreState.ROM.Data.Length);
        }
        public static bool isSafetyZArray(uint a, byte[] array)
        {
            return (a < array.Length);
        }
        public static bool isSafetyZArray(uint a, List<byte> array)
        {
            return (a < array.Count);
        }
        public static bool isSafetyZArray(int a)
        {
            return (a < CoreState.ROM.Data.Length);
        }
        public static bool isSafetyZArray(int a, byte[] array)
        {
            return (a < array.Length);
        }
        [MethodImpl(256)]
        public static bool isSafetyZArray(int a, List<byte> array)
        {
            return (a < array.Count);
        }

        // ---- ROM search (Grep, GrepPointer, FindROMPointer) -------------------
        public static uint Grep(byte[] data, byte[] need, uint start = 0x100, uint end = 0, uint blocksize = 1)
        {
            if (end == 0 || end == U.NOT_FOUND) end = (uint)data.Length;
            if (need.Length <= 0) return U.NOT_FOUND;
            if (start > end) return U.NOT_FOUND;
            uint length = end;
            if (length < need.Length) return U.NOT_FOUND;
            length -= (uint)need.Length;
            byte needfirst = need[0];
            for (uint i = start; i <= length; i += blocksize)
            {
                if (data[i] != needfirst) continue;
                uint match = (uint)need.Length;
                uint n = 1;
                for (; n < match; n++)
                {
                    if (data[i + n] != need[n]) break;
                }
                if (n >= match) return i;
            }
            return U.NOT_FOUND;
        }
        public static uint GrepPointer(byte[] data, uint needaddr, uint start = 0x100, uint end = 0)
        {
            if (needaddr == 0 || needaddr == U.NOT_FOUND) return U.NOT_FOUND;
            if (end == 0 || end == U.NOT_FOUND) end = (uint)data.Length;
            else end = (uint)Math.Min((uint)data.Length, end);
            if (end < 4) return U.NOT_FOUND;
            end -= 4;
            needaddr = U.toPointer(needaddr);
            for (uint i = start; i <= end; i += 4)
            {
                if (data[i + 3] == 0x08 || data[i + 3] == 0x09)
                {
                    if (U.u32(data, i) == needaddr) return i;
                }
            }
            return U.NOT_FOUND;
        }
        public static uint GrepEnablePointer(byte[] data, uint start = 0x100, uint end = 0)
        {
            if (end == 0 || end == U.NOT_FOUND) end = (uint)data.Length;
            if (start > end) return U.NOT_FOUND;
            end -= 3;
            uint addr;
            for (addr = start; addr < end; addr += 4)
            {
                uint p = U.u32(data, addr);
                if (U.isPointer(p))
                {
                    if (U.toOffset(p) < data.Length) continue;
                }
                break;
            }
            return addr;
        }
        public static uint GrepEnd(byte[] data, byte[] need, uint start = 0x100, uint end = 0, uint blocksize = 1, uint plus = 0, bool needPointer = false)
        {
            uint grepresult = U.Grep(data, need, start, end, blocksize);
            if (grepresult == U.NOT_FOUND) return U.NOT_FOUND;
            uint resultAddr = grepresult + (uint)need.Length + plus;
            if (resultAddr > data.Length) return U.NOT_FOUND;
            if (needPointer)
            {
                if (U.isPointerOrNULL(U.u32(data, resultAddr))) return resultAddr;
                return GrepEnd(data, need, resultAddr, end, blocksize, plus, needPointer);
            }
            return resultAddr;
        }
        public static uint FindROMPointer(ROM rom, uint[] pointers)
        {
            foreach (uint p in pointers)
            {
                if (!U.isSafetyOffset(p, rom)) continue;
                uint a = rom.u32(p);
                if (!U.isSafetyPointer(a, rom)) continue;
                return p;
            }
            return pointers[0];
        }
        public static uint FindROMPointer(ROM rom, uint checkPointer, uint[] pointers)
        {
            foreach (uint p in pointers)
            {
                if (p == U.NOT_FOUND) continue;
                if (!U.isSafetyOffset(p, rom)) continue;
                uint a = rom.u32(p);
                if (!U.isSafetyPointer(a, rom)) continue;
                a = U.toOffset(a);
                if (!U.isSafetyOffset(a + checkPointer + 4, rom)) continue;
                uint checkP = rom.u32(a + checkPointer);
                if (!U.isSafetyPointer(checkP, rom)) continue;
                return p;
            }
            return FindROMPointer(rom, pointers);
        }
        public static uint FindROMPointer(ROM rom, Func<uint, bool> func, uint[] pointers)
        {
            foreach (uint p in pointers)
            {
                if (!U.isSafetyOffset(p, rom)) continue;
                uint a = rom.u32(p);
                if (!U.isSafetyPointer(a, rom)) continue;
                a = U.toOffset(a);
                if (!U.isSafetyOffset(a + 0x100, rom)) continue;
                if (!func(a)) continue;
                return p;
            }
            return FindROMPointer(rom, pointers);
        }

        // ---- Comment parsing --------------------------------------------------
        public static bool IsComment(string line)
        {
            if (line.Length < 1) return true;
            if (line[0] == '#') return true;
            if (line[0] == ';') return true;
            if (line.Length >= 2)
            {
                if (line[0] == '/' && line[1] == '/') return true;
                if (line[0] == '-' && line[1] == '-') return true;
            }
            return false;
        }
        public static bool IsCommentSlashOnly(string line)
        {
            if (line.Length < 1) return true;
            if (line.Length >= 2 && line[0] == '/' && line[1] == '/') return true;
            return false;
        }
        public static int ClipCommentIndexOf(string str, string need)
        {
            int index = str.IndexOf(need);
            if (index < 0) return -1;
            if (index == 0) return 0;
            if (str[index - 1] == ' ' || str[index - 1] == '\t') return index - 1;
            return -1;
        }
        public static string ClipComment(string str)
        {
            int term = ClipCommentIndexOf(str, "{J}");
            if (term >= 0) str = str.Substring(0, term);
            term = ClipCommentIndexOf(str, "{U}");
            if (term >= 0) str = str.Substring(0, term);
            term = ClipCommentIndexOf(str, "//");
            if (term >= 0) str = str.Substring(0, term);
            return str;
        }
        public static bool OtherLangLine(string line, ROM rom)
        {
            if (rom.RomInfo.is_multibyte)
            {
                if (line.IndexOf("\t{U}") >= 0) return true;
            }
            else
            {
                if (line.IndexOf("\t{J}") >= 0) return true;
            }
            return false;
        }

        // ---- Path / file helpers (pure I/O, no UI) ----------------------------
        public static string ChangeExtFilename(string filename, string ext, string appendname = "")
        {
            string dir = Path.GetDirectoryName(filename);
            string name = Path.GetFileNameWithoutExtension(filename);
            return Path.Combine(dir, name + appendname + ext);
        }
        public static void WriteAllBytes(string path, byte[] bytes)
        {
            try
            {
                File.WriteAllBytes(path, bytes);
            }
            catch (Exception e)
            {
                CoreState.Services.ShowError(e.ToString());
            }
        }
        public static long GetFileSize(string filename)
        {
            try
            {
                FileInfo fi = new FileInfo(filename);
                return fi.Length;
            }
            catch (Exception) { return 0; }
        }
        public static string getVersion()
        {
            var asm = typeof(U).Assembly;
            var ver = asm.GetName().Version;
            var build = ver.Build;
            var revision = ver.Revision;
            var baseDate = new DateTime(2000, 1, 1);
            return baseDate.AddDays(build).AddSeconds(revision * 2).ToString("yyyyMMdd.HH");
        }
        public static int CountLines(string str)
        {
            return str.Split('\n').Length;
        }

        // ---- Memory comparison (pure C#, no P/Invoke) -------------------------
        public static int memcmp(byte[] a, byte[] b)
        {
            if (object.ReferenceEquals(a, b)) return 0;
            if (a == null || b == null || a.Length != b.Length) return -1;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) return a[i] < b[i] ? -1 : 1;
            }
            return 0;
        }

        // ---- Checksum ---------------------------------------------------------
        public static uint CalcCheckSUM(byte[] data)
        {
            uint result = 0;
            foreach (byte c in data) result += c;
            return result;
        }
        public static uint CalcCheckSUMDirect(byte[] data, uint addr, uint count)
        {
            if (data.Length <= addr + count)
            {
                if (data.Length == 0) return 0;
                if (addr >= data.Length - 1) addr = (uint)data.Length - 1;
                count = (uint)(data.Length) - addr;
            }
            uint end = addr + count;
            uint result = 0;
            for (uint i = addr; i < end; i++) result += data[i];
            return result;
        }

        // ---- Hex dump ---------------------------------------------------------
        public static string HexDump(List<byte> bytes) { return HexDump(bytes.ToArray()); }
        public static string HexDump(byte[] bytes)
        {
            StringBuilder r = new StringBuilder();
            for (int i = 0; i < bytes.Length; i += 1)
            {
                if ((i % 16) == 0 && i != 0) r.AppendLine();
                r.Append(" ");
                r.Append(bytes[i].ToString("X02"));
            }
            r.AppendLine();
            return r.ToString();
        }
        public static string HexDumpLiner(byte[] bytes)
        {
            StringBuilder r = new StringBuilder();
            for (int i = 0; i < bytes.Length; i += 1) { r.Append(" "); r.Append(bytes[i].ToString("X02")); }
            return r.ToString();
        }
        public static string HexDumpLiner(byte[] bytes, uint addr, uint length)
        {
            StringBuilder r = new StringBuilder();
            uint max = Math.Min(addr + length, (uint)bytes.Length);
            for (uint i = addr; i < max; i += 1) { r.Append(" "); r.Append(bytes[i].ToString("X02")); }
            return r.ToString();
        }
        public static string HexsToString(byte[] data)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < data.Length; i++) { sb.Append(' '); sb.Append(data[i].ToString("X")); }
            return sb.Length >= 1 ? sb.ToString(1, sb.Length - 1) : sb.ToString();
        }
        public static byte[] StringToHexs(string text)
        {
            string[] sp = text.Split(' ');
            List<byte> data = new List<byte>();
            for (int i = 0; i < sp.Length; i++) data.Add((byte)U.atoh(sp[i]));
            return data.ToArray();
        }

        // ---- Option parsing ---------------------------------------------------
        public static Dictionary<string, string> OptionMap(string[] args, string defautFilenameOption)
        {
            Dictionary<string, string> dic = new Dictionary<string, string>();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].Length <= 2) continue;
                if (args[i][0] == '-' && args[i][1] == '-')
                {
                    int a = args[i].IndexOf('=');
                    if (a <= 0) dic[args[i]] = "";
                    else dic[U.substr(args[i], 0, a)] = args[i].Substring(a + 1);
                }
                else if (File.Exists(args[i]))
                {
                    dic[defautFilenameOption] = args[i];
                }
                else
                {
                    dic[""] = args[i];
                }
            }
            return dic;
        }

        // ---- Misc pure helpers ------------------------------------------------
        public static void Swap<T>(ref T indexA, ref T indexB)
        {
            T tmp = indexA; indexA = indexB; indexB = tmp;
        }
        public static void Swap<T>(IList<T> list, int indexA, int indexB)
        {
            T tmp = list[indexA]; list[indexA] = list[indexB]; list[indexB] = tmp;
        }
        public static List<TYPE> Filter<TYPE>(List<TYPE> list, Func<TYPE, bool> callback)
        {
            List<TYPE> ret = new List<TYPE>();
            for (int i = 0; i < list.Count; i++)
            {
                if (callback(list[i])) ret.Add(list[i]);
            }
            return ret;
        }
        public static List<TYPE> ListMarge<TYPE>(List<TYPE> a, List<TYPE> b)
        {
            List<TYPE> ret = new List<TYPE>();
            ret.AddRange(a);
            for (int i = 0; i < b.Count; i++)
            {
                if (ret.IndexOf(b[i]) < 0) ret.Add(b[i]);
            }
            return ret;
        }
        public static string substr(string str, int start, int count)
        {
            if (str == null) return "";
            if (start >= str.Length) return "";
            if (start + count > str.Length) count = str.Length - start;
            return str.Substring(start, count);
        }
        public static string escape_shell_args(string str)
        {
            if (str.Length > 0 && str[str.Length - 1] == '\\') str = str + "\\ ";
            str = str.Replace("\"", "\\\"");
            return '"' + str + '"';
        }
        public static int BoolToInt(bool b) { return b ? 1 : 0; }
        public static bool IntToBool(int b) { return b != 0; }
        public static string GetBoolString(uint a)
        {
            if (a == 0) return "False";
            if (a == 1) return "True";
            return "";
        }
        public static bool IsEmptyRange(byte[] data, byte fill = 0)
        {
            foreach (byte a in data) { if (a != fill) return false; }
            return true;
        }
        public static bool firstMatchBin(byte[] a, byte[] need)
        {
            if (a.Length < need.Length) return false;
            byte[] bin = U.getBinaryData(a, 0, need.Length);
            return U.memcmp(bin, need) == 0;
        }
        public static string GuessExtension(byte[] bin)
        {
            if (U.firstMatchBin(bin, new byte[] { 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C })) return ".7z";
            if (U.firstMatchBin(bin, new byte[] { 0x89, 0x50, 0x4E, 0x47 })) return ".png";
            if (U.firstMatchBin(bin, new byte[] { 0x50, 0x4B })) return ".zip";
            return "";
        }
        public static double DegreeToRadian(double angle) { return Math.PI * angle / 180.0; }
        public static double RadianToDegree(double angle) { return angle * (180.0 / Math.PI); }
        public static string nl2none(string lines) { return lines.Replace("\r\n", ""); }
        public static string nl2br(string lines) { return lines.Replace("\\r\\n", "\r\n"); }
        public static string br2nl(string lines) { return lines.Replace("\r\n", "\\r\\n"); }
        public static string ToUnicode(uint code) { return code == 0 ? "" : ((char)code).ToString(); }
        public static string Reverse(string str) { char[] arr = str.ToCharArray(); Array.Reverse(arr); return new string(arr); }
        public static uint UpdateCheckBitBox(bool ch, uint a, uint bit)
        {
            return ch ? a | bit : a & ~bit;
        }
        public static bool Base64Encode(string text, out byte[] out_data)
        {
            try { out_data = System.Convert.FromBase64String(text); }
            catch (Exception) { out_data = new byte[0]; return false; }
            return true;
        }

        // ---- Unit position parsing --------------------------------------------
        public static uint ParseUnitGrowAssign(uint unitgrow) { return (unitgrow >> 1) & 0x3; }
        public static uint ParseUnitGrowLV(uint unitgrow) { return (unitgrow >> 3) & 0x1F; }
        public static uint ParsePosY32(uint unitpos) { return (unitpos >> 16) & 0xFFFF; }
        public static uint ParsePosX32(uint unitpos) { return (unitpos) & 0xFFFF; }
        public static uint ParsePosY16(uint unitpos) { return (unitpos >> 8) & 0xFF; }
        public static uint ParsePosX16(uint unitpos) { return (unitpos) & 0xFF; }
        public static uint MakeFe8UnitPos(uint x, uint y, uint ext)
        {
            return (x & 0x3F) | ((y & 0x3F) << 6) | ((ext & 0x7) << 12);
        }

        // ---- Comparer helpers -------------------------------------------------
        public class FunctionalComparer<T> : IComparer<T>
        {
            private Func<T, T, int> comparer;
            public FunctionalComparer(Func<T, T, int> comparer) { this.comparer = comparer; }
            public int Compare(T x, T y) { return comparer(x, y); }
        }
        public class FunctionalComparerOne<T> : IComparer<T>
        {
            private Func<T, int> toInt;
            public FunctionalComparerOne(Func<T, int> toInt) { this.toInt = toInt; }
            public int Compare(T x, T y) { return toInt(x) - toInt(y); }
        }
        public static List<KeyValuePair<TKey, TValue>> OrderBy<TKey, TValue>(
            Dictionary<TKey, TValue> dic, Func<KeyValuePair<TKey, TValue>, int> toInt)
        {
            List<KeyValuePair<TKey, TValue>> list = new List<KeyValuePair<TKey, TValue>>();
            foreach (KeyValuePair<TKey, TValue> pair in dic) list.Add(pair);
            FunctionalComparerOne<KeyValuePair<TKey, TValue>> comp
                = new FunctionalComparerOne<KeyValuePair<TKey, TValue>>(toInt);
            list.Sort(comp);
            return list;
        }

        // ---- AddrResult (used by many Core + UI files) ------------------------
        public class AddrResult
        {
            public uint addr;
            public string name;
            public uint tag;
            public bool isNULL() { return addr == 0 || name == null; }
            public AddrResult(uint addr, string name) { this.addr = addr; this.name = name; }
            public AddrResult(uint addr, string name, uint tag) { this.addr = addr; this.name = name; this.tag = tag; }
            public AddrResult() { }
        }
        public static uint FindList(List<U.AddrResult> list, uint addr)
        {
            int max = list.Count;
            for (int i = 0; i < max; i++) { if (list[i].addr == addr) return (uint)i; }
            return U.NOT_FOUND;
        }
        public static uint FindList(List<U.AddrResult> list, string name)
        {
            int max = list.Count;
            for (int i = 0; i < max; i++) { if (list[i].name == name) return (uint)i; }
            return U.NOT_FOUND;
        }

        // ---- ChangeCurrentDirectory (disposable, pure I/O) --------------------
        public class ChangeCurrentDirectory : IDisposable
        {
            string current_dir;
            public ChangeCurrentDirectory(string dir)
            {
                current_dir = Directory.GetCurrentDirectory();
                Directory.SetCurrentDirectory(Path.GetDirectoryName(dir));
            }
            public void Dispose() { Directory.SetCurrentDirectory(current_dir); }
        }

        // ---- Internal helpers -------------------------------------------------
        [MethodImpl(256)]
        static void check_safety(byte[] data, uint addr)
        {
            if (addr > data.Length)
            {
                throw new System.IndexOutOfRangeException(
                    String.Format("Max length:{0}(0x{1}) Access:{2}(0x{3})",
                        data.Length, U.ToHexString(data.Length), addr, U.ToHexString(addr)));
            }
        }

        // ---- Text encoding helpers (used by SystemTextEncoder, FETextDecode) ----

        [MethodImpl(256)]
        public static bool isSJIS1stCode(byte c)
        {
            return (0x81 <= c && c <= 0x9f) || (0xe0 <= c && c <= 0xfc);
        }

        [MethodImpl(256)]
        public static bool isSJIS2ndCode(byte c)
        {
            return (0x40 <= c && c <= 0x7e) || (0x80 <= c && c <= 0xfc);
        }

        public static bool IsEnglishSPCode(byte code)
        {
            return (code >= 0x80 && code <= 0xFF);
        }

        public static bool IsUTF8_LAT1SpecialFont(byte code, byte code2)
        {
            return code >= 0xC0 && code <= 0xDF && code2 >= 0x80 && code2 <= 0xBF;
        }

        public static bool isUTF8PreCode(byte code, byte code2)
        {
            return code >= 0xE0 && code2 >= 0x80;
        }

        public static int AppendUTF8(List<byte> str, byte[] srcdata, int pos)
        {
            byte code = srcdata[pos];
            if (code >= 0xF0 && pos + 3 < srcdata.Length)
            {
                str.Add(srcdata[pos]);
                str.Add(srcdata[pos + 1]);
                str.Add(srcdata[pos + 2]);
                str.Add(srcdata[pos + 3]);
                return 4;
            }
            else if (code >= 0xE0 && pos + 2 < srcdata.Length)
            {
                str.Add(srcdata[pos]);
                str.Add(srcdata[pos + 1]);
                str.Add(srcdata[pos + 2]);
                return 3;
            }
            else if (code >= 0xC0 && pos + 1 < srcdata.Length)
            {
                str.Add(srcdata[pos]);
                str.Add(srcdata[pos + 1]);
                return 2;
            }
            str.Add(srcdata[pos]);
            return 1;
        }

        public static void append_u16(List<byte> data, uint a)
        {
            data.Add((byte)(a & 0xFF));
            data.Add((byte)((a & 0xFF00) >> 8));
        }

        public static void append_u8(List<byte> data, uint a)
        {
            data.Add((byte)a);
        }

        public static byte[] SkipAtMark(string str, uint pos, Encoding SJISEncoder)
        {
            Debug.Assert(str.Substring((int)pos, 1) == "@");
            uint len = (uint)str.Length;
            if (len - pos > 4)
            {
                len = 5 + pos;
            }
            uint i;
            for (i = pos + 1; i < len; i++)
            {
                char c = str[(int)i];
                if ((c >= '0' && c <= '9') || c >= 'a' && c <= 'f' || c >= 'A' && c <= 'F')
                {
                    continue;
                }
                break;
            }
            string key = str.Substring((int)pos, (int)(i - pos));
            byte[] sjisstr = SJISEncoder.GetBytes(key);
            return sjisstr;
        }

        // ---- Table replacement (used by TextEscape) ----

        public static string table_replace(string target, string[] table)
        {
            if (table == null) { Debug.Assert(false); return target; }
            string r = target;
            for (int i = 0; i < table.Length; i += 2)
            {
                r = r.Replace(table[i], table[i + 1]);
            }
            return r;
        }

        public static string table_replace_rev(string target, string[] table)
        {
            if (table == null) { Debug.Assert(false); return target; }
            string r = target;
            for (int i = 0; i < table.Length; i += 2)
            {
                r = r.Replace(table[i + 1], table[i]);
            }
            return r;
        }

        public static string table_replace(string target, List<string> table)
        {
            if (table == null) { Debug.Assert(false); return target; }
            string r = target;
            for (int i = 0; i < table.Count; i += 2)
            {
                r = r.Replace(table[i], table[i + 1]);
            }
            return r;
        }

        public static string table_replace_rev(string target, List<string> table)
        {
            if (table == null) { Debug.Assert(false); return target; }
            string r = target;
            for (int i = 0; i < table.Count; i += 2)
            {
                r = r.Replace(table[i + 1], table[i]);
            }
            return r;
        }

        // ---- OtherLangLine single-param overload (uses CoreState.ROM) ----

        [MethodImpl(256)]
        public static bool OtherLangLine(string line)
        {
            return OtherLangLine(line, CoreState.ROM);
        }

        // ---- Dictionary helpers ----

        public static uint[] DicKeys(Dictionary<uint, string> dic)
        {
            var k = dic.Keys;
            uint[] keys = new uint[k.Count];
            k.CopyTo(keys, 0);
            return keys;
        }
        public static string[] DicKeys(Dictionary<string, string> dic)
        {
            var k = dic.Keys;
            string[] keys = new string[k.Count];
            k.CopyTo(keys, 0);
            return keys;
        }

        // ---- Path helpers ----

        //aaa.bbb.ccc.gba -> aaa
        public static string GetFirstPeriodFilename(string fullfilename)
        {
            string filename = Path.GetFileName(fullfilename);
            int a = filename.IndexOf('.');
            if (a < 0)
            {
                return fullfilename;
            }
            string dir = Path.GetDirectoryName(filename);
            return Path.Combine(dir, filename.Substring(0, a));
        }

        public static bool mkdir(string dir)
        {
            if (Directory.Exists(dir))
            {
                try
                {
                    Directory.Delete(dir, true);
                }
                catch (Exception e)
                {
                    Log.Error(e.ToString());
                    return false;
                }
            }
            Directory.CreateDirectory(dir);
            return true;
        }

        // ---- File validation helpers ----

        public static bool IsRequiredFileExist(string filename)
        {
            if (!File.Exists(filename))
            {
                if (CoreState.ROM != null && CoreState.ROM.RomInfo.version == 0)
                {
                    return false;
                }
                CoreState.Services.ShowError(string.Format(
                    "Required config file not found. Please re-download.\n{0}",
                    filename.Replace("_ALL.txt", "_*.txt")));
                Debug.Assert(false);
                return false;
            }
            return true;
        }

        public static bool CanWriteFileRetry(string path)
        {
            if (!File.Exists(path))
            {
                return true;
            }
            bool isRetry = true;
            do
            {
                try
                {
                    using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    {
                        isRetry = false;
                    }
                }
                catch (Exception)
                {
                    if (!CoreState.Services.ShowQuestion(
                        string.Format("Cannot write to file. Retry?\nFile: {0}", path)))
                    {
                        return false;
                    }
                }
            }
            while (isRetry);
            return true;
        }

        // ---- Config path construction ----

        public static bool CanSecondLanguageEnglish(string lang)
        {
            if (lang == "en") return false;
            if (lang == "ja") return false;
            return true;
        }

        public static string ConfigDataFilename(string type)
        {
            return ConfigDataFilename(type, CoreState.ROM);
        }

        public static string ConfigDataFilename(string type, ROM rom)
        {
            string lang = CoreState.Language ?? "en";
            bool canSecondLanguageEnglish = CanSecondLanguageEnglish(lang);
            string fullfilename;
            if (rom != null)
            {
                fullfilename = Path.Combine(CoreState.BaseDirectory, "config", "data", type + rom.RomInfo.TitleToFilename + "." + lang + ".txt");
                if (File.Exists(fullfilename)) return fullfilename;
                if (canSecondLanguageEnglish)
                {
                    fullfilename = Path.Combine(CoreState.BaseDirectory, "config", "data", type + rom.RomInfo.TitleToFilename + ".en.txt");
                    if (File.Exists(fullfilename)) return fullfilename;
                }
                fullfilename = Path.Combine(CoreState.BaseDirectory, "config", "data", type + rom.RomInfo.TitleToFilename + ".txt");
                if (File.Exists(fullfilename)) return fullfilename;
            }

            fullfilename = Path.Combine(CoreState.BaseDirectory, "config", "data", type + "ALL." + lang + ".txt");
            if (File.Exists(fullfilename)) return fullfilename;
            if (canSecondLanguageEnglish)
            {
                fullfilename = Path.Combine(CoreState.BaseDirectory, "config", "data", type + "ALL.en.txt");
                if (File.Exists(fullfilename)) return fullfilename;
            }
            fullfilename = Path.Combine(CoreState.BaseDirectory, "config", "data", type + "ALL.txt");
            return fullfilename;
        }

        public static string ConfigEtcFilename(string type, ROM rom)
        {
            string romtitle = "";
            if (rom == null)
            {
                romtitle = "_";
            }
            else if (rom.IsVirtualROM)
            {
                romtitle = "_Virtial_" + rom.RomInfo.VersionToFilename;
            }
            else
            {
                romtitle = U.GetFirstPeriodFilename(rom.Filename);
            }
            return Path.Combine(CoreState.BaseDirectory, "config", "etc", romtitle, type + ".txt");
        }

        public static string ConfigEtcFilename(string type, string romBaseFilename)
        {
            string romtitle = U.GetFirstPeriodFilename(romBaseFilename);
            return Path.Combine(CoreState.BaseDirectory, "config", "etc", romtitle, type + ".txt");
        }

        public static string ConfigEtcFilename(string type)
        {
            return ConfigEtcFilename(type, CoreState.ROM);
        }

        public static string ConfigEtcDir()
        {
            return Path.GetDirectoryName(ConfigEtcFilename("", CoreState.ROM));
        }

        // ---- TSV / dictionary resource loaders ----

        public static Dictionary<uint, string> LoadDicResource(string fullfilename)
        {
            Dictionary<uint, string> dic = new Dictionary<uint, string>();
            if (!U.IsRequiredFileExist(fullfilename))
            {
                return dic;
            }
            try
            {
                using (StreamReader reader = File.OpenText(fullfilename))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (U.IsComment(line) || U.OtherLangLine(line)) continue;
                        line = U.ClipComment(line);
                        if (line == "") continue;
                        string[] sp = line.Split('=');
                        dic[U.atoh(sp[0])] = U.at(sp, 1);
                    }
                }
            }
            catch (Exception e)
            {
                CoreState.Services.ShowError(string.Format(
                    "Cannot read config file.\n{0}\n{1}", fullfilename, e.ToString()));
            }
            return dic;
        }

        public static Dictionary<uint, string> LoadTSVResource1(string fullfilename, bool isRequired = true)
        {
            Dictionary<uint, string> dic = new Dictionary<uint, string>();
            if (isRequired)
            {
                if (!U.IsRequiredFileExist(fullfilename)) return dic;
            }
            else
            {
                if (!File.Exists(fullfilename)) return dic;
            }
            try
            {
                using (StreamReader reader = File.OpenText(fullfilename))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (U.IsComment(line) || U.OtherLangLine(line)) continue;
                        line = U.ClipComment(line);
                        if (line == "") continue;
                        string[] sp = line.Split('\t');
                        if (sp.Length < 2) continue;
                        dic[U.atoh(sp[0])] = sp[1];
                    }
                }
            }
            catch (Exception e)
            {
                CoreState.Services.ShowError(string.Format(
                    "Cannot read config file.\n{0}\n{1}", fullfilename, e.ToString()));
            }
            return dic;
        }

        public static Dictionary<string, string> LoadTSVResourcePair2(string fullfilename, bool isRequired = true)
        {
            Dictionary<string, string> dic = new Dictionary<string, string>();
            if (isRequired)
            {
                if (!U.IsRequiredFileExist(fullfilename)) return dic;
            }
            else
            {
                if (!File.Exists(fullfilename)) return dic;
            }
            try
            {
                using (StreamReader reader = File.OpenText(fullfilename))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (U.IsComment(line) || U.OtherLangLine(line)) continue;
                        line = U.ClipComment(line);
                        if (line == "") continue;
                        string[] sp = line.Split('\t');
                        if (sp.Length < 2) continue;
                        dic[sp[0]] = sp[1];
                    }
                }
            }
            catch (Exception e)
            {
                CoreState.Services.ShowError(string.Format(
                    "Cannot read config file.\n{0}\n{1}", fullfilename, e.ToString()));
            }
            return dic;
        }

        // ---- TSV / dictionary resource savers ----

        public static void SaveTSVResource1(string fullfilename, Dictionary<uint, string> data)
        {
            string dir = Path.GetDirectoryName(fullfilename);
            if (!Directory.Exists(dir))
            {
                U.mkdir(dir);
            }
            if (!U.CanWriteFileRetry(fullfilename))
            {
                return;
            }
            try
            {
                using (StreamWriter w = new StreamWriter(fullfilename))
                {
                    foreach (var pair in data)
                    {
                        string line = U.ToHexString(pair.Key) + "\t" + pair.Value;
                        w.WriteLine(line);
                    }
                }
            }
            catch (Exception e)
            {
                CoreState.Services.ShowError(string.Format(
                    "Cannot write to file.\n{0}\n{1}", fullfilename, e.ToString()));
            }
        }

        public static void SaveTSVResourcePair2(string fullfilename, Dictionary<string, string> data)
        {
            string dir = Path.GetDirectoryName(fullfilename);
            if (!Directory.Exists(dir))
            {
                U.mkdir(dir);
            }
            if (!U.CanWriteFileRetry(fullfilename))
            {
                return;
            }
            try
            {
                using (StreamWriter w = new StreamWriter(fullfilename))
                {
                    foreach (var pair in data)
                    {
                        string line = pair.Key + "\t" + pair.Value;
                        w.WriteLine(line);
                    }
                }
            }
            catch (Exception e)
            {
                CoreState.Services.ShowError(string.Format(
                    "Cannot write to file.\n{0}\n{1}", fullfilename, e.ToString()));
            }
        }

        public static void SaveConfigEtcTSV1(string type, Dictionary<uint, string> dic, string romBaseFilename)
        {
            string fullfilename = U.ConfigEtcFilename(type, romBaseFilename);
            if (dic.Count <= 0)
            {
                if (File.Exists(fullfilename)) File.Delete(fullfilename);
                return;
            }
            U.SaveTSVResource1(fullfilename, dic);
        }

        public static void SaveConfigEtcTSVPair(string type, Dictionary<string, string> dic, string romBaseFilename)
        {
            string fullfilename = U.ConfigEtcFilename(type, romBaseFilename);
            if (dic.Count <= 0)
            {
                if (File.Exists(fullfilename)) File.Delete(fullfilename);
                return;
            }
            U.SaveTSVResourcePair2(fullfilename, dic);
        }

        public static Dictionary<uint, string> LoadConfigEtcTSV1(string type)
        {
            return U.LoadTSVResource1(U.ConfigEtcFilename(type), false);
        }
    }
}
