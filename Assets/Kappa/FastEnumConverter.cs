using System;
using System.Runtime.InteropServices;

namespace Kappa
{
    [StructLayout(LayoutKind.Explicit)]
    struct FastEnumConverter<T> where T : IConvertible
    {
        [FieldOffset(0)] public T Raw;
        [FieldOffset(0)] public sbyte AsSByte;
        [FieldOffset(0)] public byte AsByte;
        [FieldOffset(0)] public short AsShort;
        [FieldOffset(0)] public ushort AsUShort;
        [FieldOffset(0)] public int AsInt;
        [FieldOffset(0)] public uint AsUInt;
        [FieldOffset(0)] public long AsLong;
        [FieldOffset(0)] public ulong AsULong;
    }
     
    public static class FastEnumConvert
    {
        public static sbyte ToSByte<T>(T value) where T : IConvertible { return new FastEnumConverter<T> { Raw = value }.AsSByte; }
        public static byte ToByte<T>(T value) where T : IConvertible { return new FastEnumConverter<T> { Raw = value }.AsByte; }
        public static short ToShort<T>(T value) where T : IConvertible { return new FastEnumConverter<T> { Raw = value }.AsShort; }
        public static ushort ToUShort<T>(T value) where T : IConvertible { return new FastEnumConverter<T> { Raw = value }.AsUShort; }
        public static int ToInt32<T>(T value) where T : IConvertible { return new FastEnumConverter<T> { Raw = value }.AsInt; }
        public static uint ToUInt32<T>(T value) where T : IConvertible { return new FastEnumConverter<T> { Raw = value }.AsUInt; }
        public static long ToLong<T>(T value) where T : IConvertible { return new FastEnumConverter<T> { Raw = value }.AsLong; }
        public static ulong ToULong<T>(T value) where T : IConvertible { return new FastEnumConverter<T> { Raw = value }.AsULong; }
     
        public static T ToEnum<T>(sbyte value) where T : IConvertible { return new FastEnumConverter<T> { AsSByte = value }.Raw; }
        public static T ToEnum<T>(byte value) where T : IConvertible { return new FastEnumConverter<T> { AsByte = value }.Raw; }
        public static T ToEnum<T>(short value) where T : IConvertible { return new FastEnumConverter<T> { AsShort = value }.Raw; }
        public static T ToEnum<T>(ushort value) where T : IConvertible { return new FastEnumConverter<T> { AsUShort = value }.Raw; }
        public static T ToEnum<T>(int value) where T : IConvertible { return new FastEnumConverter<T> { AsInt = value }.Raw; }
        public static T ToEnum<T>(uint value) where T : IConvertible { return new FastEnumConverter<T> { AsUInt = value }.Raw; }
        public static T ToEnum<T>(long value) where T : IConvertible { return new FastEnumConverter<T> { AsLong = value }.Raw; }
        public static T ToEnum<T>(ulong value) where T : IConvertible { return new FastEnumConverter<T> { AsULong = value }.Raw; }
    }
}
