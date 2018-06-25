﻿using System;
using System.Collections;

namespace NBitcoin
{
	/// <summary> Provides a view of an array of bits as a stream of bits. </summary>
	class BitStream
	{
		private byte[] _buffer;
		private int _remainCount;

		public BitStream()
			: this(new byte[0])
		{
		}

		public BitStream(byte[] buffer)
		{
			var newBuffer = new byte[buffer.Length];
			Buffer.BlockCopy(buffer, 0, newBuffer, 0, buffer.Length);
			_buffer = newBuffer;
			_remainCount = buffer.Length == 0 ? 0 : 8;
		}

		private void AddZeroByte()
		{
			var newBuffer = new byte[_buffer.Length + 1];
			Buffer.BlockCopy(_buffer, 0, newBuffer, 0, _buffer.Length);
			_buffer = newBuffer;
		}

		private void EnsureCapacity()
		{
			if (_remainCount == 0)
			{
				AddZeroByte();
				_remainCount = 8;
			}
		}

		public void WriteBit(bool bit)
		{
			EnsureCapacity();
			if (bit)
			{
				var lastIndex = _buffer.Length - 1;
				_buffer[lastIndex] |= (byte)(1 << (_remainCount - 1));
			}
			_remainCount--;
		}

        public void WriteBits(ulong data, byte count)
        {
			data <<= (64 - count);
			while(count >= 8)
			{
				var b = (byte)(data >> (64 - 8));
				WriteByte(b);
				data <<= 8;
				count -= 8;
			}

			while(count > 0)
			{
				var bit = data >> (64 - 1);
				WriteBit(bit == 1);
				data <<= 1;
				count--;
			}
		}

		public void WriteByte(byte b)
		{
			EnsureCapacity();

			var lastIndex = _buffer.Length - 1;
			_buffer[lastIndex] |= (byte)(b >> (8 - _remainCount));

			AddZeroByte();
			_buffer[lastIndex + 1] = (byte)(b << _remainCount);
		}

		public bool TryReadBit(out bool bit)
		{
			bit = false;
			if (_buffer.Length == 0){
				return false;
			}

			if(_remainCount == 0)
			{
				if (_buffer.Length == 1)
					return false;
				
				var newBuffer = new byte[_buffer.Length - 1];
				Buffer.BlockCopy(_buffer, 1, newBuffer, 0, _buffer.Length - 1);
				_buffer = newBuffer;
				_remainCount = 8;
			}

			var curbit = _buffer[0] & 0x80;
			_buffer[0] <<= 1;
			_remainCount--;

			bit = curbit != 0;
			return true;
		}

		public bool TryReadBits(int count, out ulong bits)
		{
			var val = 0UL;
			while(count >= 8)
			{
				val <<= 8;
				if(!TryReadByte(out var readedByte)){
					bits = 0U;
					return false;
				}
				val |= (ulong)readedByte;
				count -= 8;
			}

			while(count > 0)
			{
				val <<= 1;
				if(TryReadBit(out var bit)){
					val |= bit ? 1UL : 0UL;
					count--;
				}
				else
				{
					bits = 0U;
					return false;
				}
			}
			bits = val;
			return true;
		}

		public bool TryReadByte(out byte b)
		{
			b = 0;
			if (_buffer.Length == 0)
				return false;

			if (_remainCount == 0)
			{
				if (_buffer.Length == 1)
				{
					return false;
				}
				var newBuffer = new byte[_buffer.Length - 1];
				Buffer.BlockCopy(_buffer, 1, newBuffer, 0, _buffer.Length - 1);
				_buffer = newBuffer;
				_remainCount = 8;
			}

			b = _buffer[0];
			var newBuffer1 = new byte[_buffer.Length - 1];
			Buffer.BlockCopy(_buffer, 1, newBuffer1, 0, _buffer.Length - 1);
			_buffer = newBuffer1;
			if (_remainCount == 8)
			{
				return true;
			}

			if (_buffer.Length == 0)
			{
				b = 0;
				return false;
			}

			b |= (byte)(_buffer[0] >> _remainCount);
			_buffer[0] <<= (8 - _remainCount);
			return true;
		}

		public byte[] ToByteArray()
		{
			return _buffer;
		}
    }


	internal class GRCodedStreamWriter
	{
		private readonly BitStream _stream;
		private readonly byte _p;
		private readonly ulong _modP;
		private ulong _lastValue;

		public GRCodedStreamWriter(BitStream stream, byte p)
		{
			_stream = stream;
			_p = p;
			_modP = (1UL << p);
			_lastValue = 0UL;
		}

		public void Write(ulong value)
		{
			var diff = value - _lastValue;

			var remainder = diff & (_modP - 1);
			var quotient = (diff - remainder) >> _p;

			while (quotient > 0)
			{
				_stream.WriteBit(true);
				quotient--;
			}
			_stream.WriteBit(false);
			_stream.WriteBits(remainder, _p);
			_lastValue = value;
		}
	}

	internal class GRCodedStreamReader
	{
		private readonly BitStream _stream;
		private readonly byte _p;
		private readonly ulong _modP;
		private ulong _lastValue;

		public GRCodedStreamReader(BitStream stream, byte p, ulong lastValue)
		{
			_stream = stream;
			_p = p;
			_modP = (1UL << p);
			_lastValue = lastValue;
		}

		public bool TryRead(out ulong value)
		{
			if(TryReadUInt64(out var readedValue)){
				var currentValue = _lastValue + readedValue;
				_lastValue = currentValue;
				value = currentValue;
				return true;
			}

			value = 0;
			return false;
		}

		private bool TryReadUInt64(out ulong value)
		{
			value = 0U;
			var count = 0UL;
			if(!_stream.TryReadBit(out var bit))
				return false;

			while (bit)
			{
				count++;
				if(!_stream.TryReadBit(out bit))
					return false;
			}

			if(_stream.TryReadBits(_p, out var remainder))
			{
				value = (count * _modP) + remainder;
				return true;
			}

			return false;
		}
	}
}
