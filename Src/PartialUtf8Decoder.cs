using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using RT.Util.ExtensionMethods;

namespace RunLogged
{
    class PartialUtf8Decoder
    {
        private class ByteRange { public byte[] Bytes; public int Offset; public int Length; }

        private ByteRange _undecoded1, _undecoded2;

        public bool HasPendingBytes
        {
            get
            {
                return _undecoded1 != null || _undecoded2 != null;
            }
        }

        public string AppendBytes(byte[] bytes)
        {
            return AppendBytes(bytes, 0, bytes.Length);
        }

        public string AppendBytes(byte[] bytes, int offset, int length)
        {
            if (length == 0)
                return "";
            _undecoded2 = new ByteRange() { Bytes = bytes, Offset = offset, Length = length };

            var builder = new StringBuilder();
            byte[] deconsume = null;

            while (true)
            {
                byte byte1;
                if (!consumeByte(out byte1))
                    break;

                if ((byte1 & 0x80) == 0)
                {
                    // Length 1
                    builder.Append((char) byte1);
                }
                else if ((byte1 & 0xE0) == 0xC0)
                {
                    // Length 2
                    byte byte2;
                    if (!consumeByte(out byte2))
                    {
                        deconsume = new byte[] { byte1 };
                        break;
                    }
                    if ((byte2 & 0xC0) != 0x80)
                    {
                        builder.Append((char) 0xFFFD);
                        builder.Append((char) 0xFFFD);
                        continue;
                    }

                    builder.Append((char) (((byte1 & 0x1F) << 6) + (byte2 & 0x3F)));
                }
                else if ((byte1 & 0xF0) == 0xE0)
                {
                    // Length 3
                    byte byte2;
                    if (!consumeByte(out byte2))
                    {
                        deconsume = new byte[] { byte1 };
                        break;
                    }
                    if ((byte2 & 0xC0) != 0x80)
                    {
                        builder.Append((char) 0xFFFD);
                        builder.Append((char) 0xFFFD);
                        continue;
                    }

                    byte byte3;
                    if (!consumeByte(out byte3))
                    {
                        deconsume = new byte[] { byte1, byte2 };
                        break;
                    }
                    if ((byte3 & 0xC0) != 0x80)
                    {
                        builder.Append((char) 0xFFFD);
                        builder.Append((char) 0xFFFD);
                        builder.Append((char) 0xFFFD);
                        continue;
                    }

                    builder.Append((char) (((byte1 & 0x0F) << 12) + ((byte2 & 0x3F) << 6) + (byte3 & 0x3F)));
                }
                else if ((byte1 & 0xF8) == 0xF0)
                {
                    // Length 4
                    byte byte2;
                    if (!consumeByte(out byte2))
                    {
                        deconsume = new byte[] { byte1 };
                        break;
                    }
                    if ((byte2 & 0xC0) != 0x80)
                    {
                        builder.Append((char) 0xFFFD);
                        builder.Append((char) 0xFFFD);
                        continue;
                    }

                    byte byte3;
                    if (!consumeByte(out byte3))
                    {
                        deconsume = new byte[] { byte1, byte2 };
                        break;
                    }
                    if ((byte3 & 0xC0) != 0x80)
                    {
                        builder.Append((char) 0xFFFD);
                        builder.Append((char) 0xFFFD);
                        builder.Append((char) 0xFFFD);
                        continue;
                    }

                    byte byte4;
                    if (!consumeByte(out byte4))
                    {
                        deconsume = new byte[] { byte1, byte2, byte3 };
                        break;
                    }
                    if ((byte4 & 0xC0) != 0x80)
                    {
                        builder.Append((char) 0xFFFD);
                        builder.Append((char) 0xFFFD);
                        builder.Append((char) 0xFFFD);
                        builder.Append((char) 0xFFFD);
                        continue;
                    }

                    int codepoint = (((byte1 & 0x07) << 18) + ((byte2 & 0x3F) << 12) + ((byte3 & 0x3F) << 6) + (byte4 & 0x3F));
                    // Now encode into UTF-16
                    codepoint -= 0x10000;
                    builder.Append((char) (0xD800 + (codepoint >> 10)));
                    builder.Append((char) (0xDC00 + (codepoint & 0x3FF)));
                }
                else
                {
                    // Invalid
                    builder.Append((char) 0xFFFD);
                }
            }

            var newbytes = new List<byte>();
            if (deconsume != null)
                newbytes.AddRange(deconsume);
            if (_undecoded1 != null)
                newbytes.AddRange(_undecoded1.Bytes.Skip(_undecoded1.Offset).Take(_undecoded1.Length));
            if (_undecoded2 != null)
                newbytes.AddRange(_undecoded2.Bytes.Skip(_undecoded2.Offset).Take(_undecoded2.Length));

            _undecoded1 = newbytes.Count == 0 ? null : new ByteRange() { Bytes = newbytes.ToArray(), Offset = 0, Length = newbytes.Count };
            _undecoded2 = null;

            return builder.ToString();
        }

        private bool consumeByte(out byte result)
        {
            if (_undecoded1 == null && _undecoded2 == null)
            {
                result = 0;
                return false;
            }
            else if (_undecoded1 == null)
            {
                _undecoded1 = _undecoded2;
                _undecoded2 = null;
            }

            result = _undecoded1.Bytes[_undecoded1.Offset];
            _undecoded1.Offset++;
            _undecoded1.Length--;
            if (_undecoded1.Length == 0)
                _undecoded1 = null;
            return true;
        }
    }

    static class TestPartialUtf8Decoder
    {
        public static void RunTests()
        {
            TestPartialUtf8("test");
            Console.WriteLine();
            Console.WriteLine();
            TestPartialUtf8("тест");
            Console.WriteLine();
            Console.WriteLine();
            TestPartialUtf8("-\U0001D11E-");
        }

        public static void TestPartialUtf8(string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            foreach (var addends in enumAllAddends2(bytes.Length))
            {
                var partial = new PartialUtf8Decoder();
                var str = new StringBuilder();
                int start = 0;
                foreach (int length in addends)
                {
                    byte[] partbytes = bytes.Subarray(start, length);
                    string partstring = partial.AppendBytes(partbytes);
                    str.Append(partstring);
                    start += length;
                    Console.Write("{0} => \"{1}\"      ".Fmt(BitConverter.ToString(partbytes), partstring));
                }
                Console.WriteLine();
                Debug.Assert(str.ToString() == text);
                Debug.Assert(!partial.HasPendingBytes);
            }
        }

        private static IEnumerable<int[]> enumAllAddends1(int sum)
        {
            for (int n = 1; n < sum; n++)
            {
                foreach (var subaddends in enumAllAddends1(sum - n))
                    yield return n.Concat(subaddends).ToArray();
            }
            yield return new[] { sum };
        }

        private static IEnumerable<List<int>> enumAllAddends2(int sum)
        {
            for (int n = 1; n < sum; n++)
            {
                foreach (var subaddends in enumAllAddends2(sum - n))
                {
                    subaddends.Add(n);
                    yield return subaddends;
                }
            }
            yield return new List<int> { sum };
        }
    }
}
