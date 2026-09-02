using System;

namespace FISHHWB.MeshyImporter.Editor
{
    /// <summary>
    /// VP8 boolean (arithmetic) entropy decoder. Ported faithfully from libwebp's
    /// src/utils/bit_reader_utils.c / bit_reader_inl_utils.h (BSD licensed,
    /// https://github.com/webmproject/libwebp), using its BITS=24 windowed-load
    /// configuration. VP8 decode is bit-exact by spec (RFC 6386), so this must
    /// match the reference implementation's output exactly, not just approximately.
    /// </summary>
    internal sealed class Vp8BitReader
    {
        private const int Bits = 24; // window size in bits (matches libwebp's 32-bit default path)

        private byte[] _buf;
        private int _pos;      // next byte to read
        private int _bufEnd;   // one past the last valid byte
        private int _bufMax;   // last position from which a full 4-byte windowed read is safe

        private uint _value;   // bit_t (32-bit is enough for BITS=24)
        private uint _range;   // current range MINUS 1, kept in roughly [126,253]
        private int _bits;     // number of valid bits currently loaded beyond the read position
        private bool _eof;

        public void Init(byte[] buf, int start, int size)
        {
            _buf = buf;
            _pos = start;
            _bufEnd = start + size;
            _bufMax = (size >= 4) ? start + size - 4 + 1 : start;

            _range = 255 - 1;
            _value = 0;
            _bits = -8; // to load the very first 8 bits
            _eof = false;

            LoadNewBytes();
        }

        private void LoadNewBytes()
        {
            if (_pos < _bufMax)
            {
                uint b0 = _buf[_pos];
                uint b1 = _buf[_pos + 1];
                uint b2 = _buf[_pos + 2];
                // Reference reads 4 bytes (needs the margin) but only consumes 3 -- the
                // windowed 24-bit big-endian value formed from the first 3 bytes.
                uint bits24 = (b0 << 16) | (b1 << 8) | b2;
                _pos += Bits >> 3; // 3

                _value = bits24 | (_value << Bits);
                _bits += Bits;
            }
            else
            {
                LoadFinalBytes();
            }
        }

        private void LoadFinalBytes()
        {
            if (_pos < _bufEnd)
            {
                _bits += 8;
                _value = (uint)(_buf[_pos++]) | (_value << 8);
            }
            else if (!_eof)
            {
                _value <<= 8;
                _bits += 8;
                _eof = true;
            }
            else
            {
                _bits = 0;
            }
        }

        private static int BitsLog2Floor(uint n)
        {
            int result = 0;
            while (n > 1) { n >>= 1; result++; }
            return result;
        }

        /// <summary>Read one bit with the given probability (0..255) of being 0.</summary>
        public int GetBit(int prob)
        {
            uint range = _range;
            if (_bits < 0) LoadNewBytes();

            int pos = _bits;
            uint split = (range * (uint)prob) >> 8;
            uint value = _value >> pos;
            int bit = value > split ? 1 : 0;
            if (bit != 0)
            {
                range -= split;
                _value -= (split + 1) << pos;
            }
            else
            {
                range = split + 1;
            }

            int shift = 7 ^ BitsLog2Floor(range);
            range <<= shift;
            _bits -= shift;

            _range = range - 1;
            return bit;
        }

        /// <summary>Simplified GetBit for prob==0x80 (used for sign / literal bits).</summary>
        public int GetSigned(int v)
        {
            if (_bits < 0) LoadNewBytes();

            int pos = _bits;
            uint split = _range >> 1;
            uint value = _value >> pos;
            int mask = unchecked((int)(split - value)) >> 31; // -1 or 0

            _bits -= 1;
            _range = unchecked(_range + (uint)mask);
            _range |= 1;
            _value -= unchecked((split + 1) & (uint)mask) << pos;

            return (v ^ mask) - mask;
        }

        public uint GetValue(int bits)
        {
            uint v = 0;
            while (bits-- > 0) v |= (uint)GetBit(0x80) << bits;
            return v;
        }

        public int GetSignedValue(int bits)
        {
            int value = (int)GetValue(bits);
            return GetBit(0x80) != 0 ? -value : value;
        }
    }
}
