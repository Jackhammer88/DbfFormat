using System;
using System.Runtime.Serialization.Formatters.Binary;
using System.IO;
using System.Runtime.InteropServices;

namespace IRI.Ket.IO
{
    public static class BinaryStream
    {
        private const int Int32Length = 4;

        private const int DoubleLength = 8;


        public static void Serialize(object value, string path)
        {
            BinaryFormatter formatter = new BinaryFormatter();

            using (Stream fStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                formatter.Serialize(fStream, value);
            }
        }

        public static object Deserialize(string path)
        {
            if (!System.IO.File.Exists(path))
            {
                throw new InvalidOperationException($"File {path} doesn't exist");
            }

            BinaryFormatter formatter = new BinaryFormatter();

            using (Stream fStream = File.OpenRead(path))
            {
                return formatter.Deserialize(fStream);
            }
        }

        public static byte[] Serialize<T>(T structure)
        {
            using (System.IO.MemoryStream stream = new System.IO.MemoryStream())
            {
                BinaryFormatter formatter = new BinaryFormatter();

                formatter.Serialize(stream, structure);

                stream.Flush();

                return stream.ToArray();
            }
        }

        public static T Deserialize<T>(byte[] buffer)
        {
            BinaryFormatter formatter = new BinaryFormatter();

            using (System.IO.MemoryStream stream = new System.IO.MemoryStream(buffer))
            {
                return (T)formatter.Deserialize(stream);
            }
        }

        public static byte[] StructureToByteArray<T>(T structure)
        {
            int len = Marshal.SizeOf(structure);
            byte[] result = new byte[len];

            IntPtr ptr = Marshal.AllocHGlobal(len);

            try
            {
                Marshal.StructureToPtr(structure, ptr, false);
                Marshal.Copy(ptr, result, 0, len);
            }
            finally
            {
                Marshal.DestroyStructure(ptr, typeof(T));
                Marshal.FreeHGlobal(ptr);
            }

            return result;
        }

        public static T ByteArrayToStructure<T>(byte[] buffer)
        {
            int length = buffer.Length;
            IntPtr i = Marshal.AllocHGlobal(length);

            try
            {
                Marshal.Copy(buffer, 0, i, length);
                return (T)Marshal.PtrToStructure(i, typeof(T));
            }
            finally
            {
                Marshal.FreeHGlobal(i);
            }
        }

        public static T ParseToStructure<T>(byte[] buffer) where T : struct
        {
            GCHandle handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);

            T result = (T)Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(T));

            handle.Free();

            return result;
        }



        /// <summary>
        /// 
        /// </summary>
        /// <param name="byteRepresentation"></param>
        /// <param name="startIndex">A 32bit integer that represents the index in the sourceArray at which the copying begins.</param>
        /// <param name="offset"></param>
        /// <returns></returns>
        public static int BigEndianOrderedBytesToInt32(byte[] bigEndianOrderedBytesRepresentation)
        {
            Array.Reverse(bigEndianOrderedBytesRepresentation);

            return System.BitConverter.ToInt32(bigEndianOrderedBytesRepresentation, 0);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="byteRepresentation"></param>
        /// <param name="startIndex">A 32bit integer that represents the index in the sourceArray at which the copying begins.</param>
        /// <returns></returns>
        public static double BigEndianOrderedBytesToDouble(byte[] bigEndianOrderedBytesRepresentation)
        {
            Array.Reverse(bigEndianOrderedBytesRepresentation);

            return System.BitConverter.ToDouble(bigEndianOrderedBytesRepresentation, 0);
        }

        public static byte[] Int32ToBigEndianOrderedBytes(int int32Representation)
        {
            byte[] result = System.BitConverter.GetBytes(int32Representation);

            Array.Reverse(result);

            return result;
        }

        public static byte[] DoubleToBigEndianOrderedBytes(double doubleRepresentation)
        {
            byte[] result = System.BitConverter.GetBytes(doubleRepresentation);

            Array.Reverse(result);

            return result;
        }

        public static int LittleEndianOrderedBytesToInt32(byte[] source, int startIndex)
        {
            byte[] temp = new byte[Int32Length];

            Array.ConstrainedCopy(source, startIndex, temp, 0, Int32Length);

            return System.BitConverter.ToInt32(temp, 0);
        }

        public static double LittleEndianOrderedBytesToDouble(byte[] source, int startIndex)
        {
            byte[] temp = new byte[DoubleLength];

            Array.ConstrainedCopy(source, startIndex, temp, 0, DoubleLength);

            return System.BitConverter.ToDouble(temp, 0);
        }
    }
}
