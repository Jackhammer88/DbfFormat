﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Linq;

namespace Dbf
{
    public static class DbfFileFormat
    {
        const char arabicYa01 = '\u0649';
        const char arabicYa02 = '\u064A';
        const char arabicKaf = '\u0643';

        const char farsiYa = '\u06cc';
        const char farsiKaf = '\u06A9';

        public static string ArabicToFarsi(string arabicString)
        {
            return arabicString
                .Replace(arabicYa01, farsiYa)
                .Replace(arabicYa02, farsiYa)
                .Replace(arabicKaf, farsiKaf);
        }

        private static Func<string, string> defaultCorrection = (i) => { return ArabicToFarsi(i); };

        static bool correctFarsiCharacters = true;

        private static Dictionary<char, Func<byte[], object>> MapFunction;

        private static bool? ConvertDbfLogicalValueToBoolean(byte[] buffer)
        {
            string tempValue = Encoding.ASCII.GetString(buffer);

            if (tempValue.ToUpper().Equals("T") || tempValue.ToUpper().Equals("Y"))
            {
                return true;
            }
            else if (tempValue.ToUpper().Equals("F") || tempValue.ToUpper().Equals("N"))
            {
                return false;
            }
            else
            {
                return null;
            }
        }

        private static string ConvertDbfGeneralToString(byte[] buffer, Encoding encoding)
        {
            if (correctFarsiCharacters)
            {
                return defaultCorrection(encoding.GetString(buffer).Replace('\0', ' ').Trim());
            }
            else
            {
                return encoding.GetString(buffer).Replace('\0', ' ').Trim();
            }

        }

        private const int arabicWindowsEncoding = 1256;

        public static Encoding ArabicEncoding = Encoding.GetEncoding(arabicWindowsEncoding);

        private static Encoding currentEncoding = Encoding.GetEncoding(arabicWindowsEncoding);

        private static Encoding fieldsEncoding = Encoding.GetEncoding(arabicWindowsEncoding);

        public static void ChangeEncoding(Encoding newEncoding)
        {
            currentEncoding = newEncoding;

            InitializeMapFunctions();
        }

        static DbfFileFormat()
        {
            InitializeMapFunctions();
        }

        private static readonly Func<byte[], object> ToDouble =
            (input) =>
            {
                double value;
                return double.TryParse(Encoding.ASCII.GetString(input), out value) ? (object)value : DBNull.Value;
            };

        private static readonly Func<byte[], object> ToInt =
            (input) =>
            {
                string value = Encoding.ASCII.GetString(input);
                return string.IsNullOrEmpty(value) ? DBNull.Value : (object)int.Parse(value);
            };

        private static readonly Func<byte[], object> ToDecimal =
            (input) =>
            {
                string value = Encoding.ASCII.GetString(input);
                return string.IsNullOrEmpty(value) ? DBNull.Value : (object)decimal.Parse(value);
            };

        private static void InitializeMapFunctions()
        {
            MapFunction = new Dictionary<char, Func<byte[], object>>();

            MapFunction.Add('F', ToDouble);

            MapFunction.Add('O', ToDouble);
            MapFunction.Add('+', ToDouble);
            MapFunction.Add('I', ToInt);
            MapFunction.Add('Y', ToDecimal);

            MapFunction.Add('L', (input) => ConvertDbfLogicalValueToBoolean(input));

            MapFunction.Add('D', (input) => new DateTime(int.Parse(new string(Encoding.ASCII.GetChars(input, 0, 4))),
                                                            int.Parse(new string(Encoding.ASCII.GetChars(input, 4, 2))),
                                                            int.Parse(new string(Encoding.ASCII.GetChars(input, 6, 2)))));

            MapFunction.Add('M', (input) => input);

            MapFunction.Add('B', (input) => input);

            MapFunction.Add('P', (input) => input);

            MapFunction.Add('N', ToDouble);

            MapFunction.Add('C', (input) => ConvertDbfGeneralToString(input, currentEncoding));

            MapFunction.Add('G', (input) => ConvertDbfGeneralToString(input, currentEncoding));

            MapFunction.Add('V', (input) => ConvertDbfGeneralToString(input, currentEncoding));
        }

        public static void Write(string fileName, int numberOfRecords)
        {
            List<int> attributes = Enumerable.Range(0, numberOfRecords).ToList();

            Write(fileName,
                attributes,
                new List<Func<int, object>>() { i => i },
                new List<DbfFieldDescriptor>() { DbfFieldDescriptors.GetIntegerField("Id") },
                Encoding.ASCII);

        }

        public static System.Data.DataTable Read(string dbfFileName, string tableName, Encoding dataEncoding, Encoding fieldHeaderEncoding, bool correctFarsiCharacters)
        {
            ChangeEncoding(dataEncoding);

            DbfFileFormat.fieldsEncoding = fieldHeaderEncoding;

            DbfFileFormat.correctFarsiCharacters = correctFarsiCharacters;

            return Read(dbfFileName, tableName);
        }

        public static System.Data.DataTable Read(string dbfFileName, string tableName)
        {
            System.IO.Stream stream = new System.IO.FileStream(dbfFileName, System.IO.FileMode.Open);

            System.IO.BinaryReader reader = new System.IO.BinaryReader(stream);

            byte[] buffer = reader.ReadBytes(Marshal.SizeOf(typeof(DbfHeader)));

            DbfHeader header = IRI.Ket.IO.BinaryStream.ByteArrayToStructure<DbfHeader>(buffer);

            List<DbfFieldDescriptor> columns = new List<DbfFieldDescriptor>();

            if ((header.LengthOfHeader - 33) % 32 != 0) { throw new NotImplementedException(); }

            int numberOfFields = (header.LengthOfHeader - 33) / 32;

            for (int i = 0; i < numberOfFields; i++)
            {
                buffer = reader.ReadBytes(Marshal.SizeOf(typeof(DbfFieldDescriptor)));

                columns.Add(DbfFieldDescriptor.Parse(buffer, DbfFileFormat.fieldsEncoding));
            }

            System.Data.DataTable result = MakeTableSchema(tableName, columns);

            ((FileStream)reader.BaseStream).Seek(header.LengthOfHeader, SeekOrigin.Begin);

            for (int i = 0; i < header.NumberOfRecords; i++)
            {
                // First we'll read the entire record into a buffer and then read each field from the buffer
                // This helps account for any extra space at the end of each record and probably performs better
                buffer = reader.ReadBytes(header.LengthOfEachRecord);
                BinaryReader recordReader = new BinaryReader(new MemoryStream(buffer));

                // All dbf field records begin with a deleted flag field. Deleted - 0x2A (asterisk) else 0x20 (space)
                if (recordReader.ReadChar() == '*')
                {
                    continue;
                }

                object[] values = new object[columns.Count];

                for (int j = 0; j < columns.Count; j++)
                {
                    int fieldLenth = columns[j].Length;

                    values[j] = MapFunction[columns[j].Type](recordReader.ReadBytes(fieldLenth));
                }

                recordReader.Close();

                result.Rows.Add(values);
            }

            reader.Close();

            stream.Close();

            return result;
        }


        public static object[][] ReadToObject(string dbfFileName, string tableName, Encoding dataEncoding, Encoding fieldHeaderEncoding, bool correctFarsiCharacters)
        {
            ChangeEncoding(dataEncoding);

            DbfFileFormat.fieldsEncoding = fieldHeaderEncoding;

            DbfFileFormat.correctFarsiCharacters = correctFarsiCharacters;

            return ReadToObject(dbfFileName, tableName);
        }

        public static object[][] ReadToObject(string dbfFileName, string tableName)
        {
            System.IO.Stream stream = new System.IO.FileStream(dbfFileName, System.IO.FileMode.Open);

            System.IO.BinaryReader reader = new System.IO.BinaryReader(stream);

            byte[] buffer = reader.ReadBytes(Marshal.SizeOf(typeof(DbfHeader)));

            DbfHeader header = IRI.Ket.IO.BinaryStream.ByteArrayToStructure<DbfHeader>(buffer);

            List<DbfFieldDescriptor> columns = new List<DbfFieldDescriptor>();

            if ((header.LengthOfHeader - 33) % 32 != 0) { throw new NotImplementedException(); }

            int numberOfFields = (header.LengthOfHeader - 33) / 32;

            for (int i = 0; i < numberOfFields; i++)
            {
                buffer = reader.ReadBytes(Marshal.SizeOf(typeof(DbfFieldDescriptor)));

                columns.Add(DbfFieldDescriptor.Parse(buffer, DbfFileFormat.fieldsEncoding));
            }

            var result = new object[header.NumberOfRecords][];

            ((FileStream)reader.BaseStream).Seek(header.LengthOfHeader, SeekOrigin.Begin);

            for (int i = 0; i < header.NumberOfRecords; i++)
            {
                // First we'll read the entire record into a buffer and then read each field from the buffer
                // This helps account for any extra space at the end of each record and probably performs better
                buffer = reader.ReadBytes(header.LengthOfEachRecord);
                BinaryReader recordReader = new BinaryReader(new MemoryStream(buffer));

                // All dbf field records begin with a deleted flag field. Deleted - 0x2A (asterisk) else 0x20 (space)
                if (recordReader.ReadChar() == '*')
                {
                    continue;
                }

                object[] values = new object[columns.Count];

                for (int j = 0; j < columns.Count; j++)
                {
                    int fieldLenth = columns[j].Length;

                    values[j] = MapFunction[columns[j].Type](recordReader.ReadBytes(fieldLenth));
                }

                recordReader.Close();

                result[i] = values;
            }

            reader.Close();

            stream.Close();

            return result;
        }





        public static List<DbfFieldDescriptor> GetDbfSchema(string dbfFileName)
        {
            System.IO.Stream stream = new System.IO.FileStream(dbfFileName, System.IO.FileMode.Open);

            System.IO.BinaryReader reader = new System.IO.BinaryReader(stream);

            byte[] buffer = reader.ReadBytes(Marshal.SizeOf(typeof(DbfHeader)));

            DbfHeader header = IRI.Ket.IO.BinaryStream.ByteArrayToStructure<DbfHeader>(buffer);

            List<DbfFieldDescriptor> columns = new List<DbfFieldDescriptor>();

            if ((header.LengthOfHeader - 33) % 32 != 0) { throw new NotImplementedException(); }

            int numberOfFields = (header.LengthOfHeader - 33) / 32;

            for (int i = 0; i < numberOfFields; i++)
            {
                buffer = reader.ReadBytes(Marshal.SizeOf(typeof(DbfFieldDescriptor)));

                columns.Add(IRI.Ket.IO.BinaryStream.ParseToStructure<DbfFieldDescriptor>(buffer));
            }

            reader.Close();

            stream.Close();

            return columns;
        }

        public static List<DbfFieldDescriptor> GetDbfSchema(string dbfFileName, Encoding encoding)
        {
            System.IO.Stream stream = new System.IO.FileStream(dbfFileName, System.IO.FileMode.Open);

            System.IO.BinaryReader reader = new System.IO.BinaryReader(stream);

            byte[] buffer = reader.ReadBytes(Marshal.SizeOf(typeof(DbfHeader)));

            DbfHeader header = IRI.Ket.IO.BinaryStream.ByteArrayToStructure<DbfHeader>(buffer);

            List<DbfFieldDescriptor> columns = new List<DbfFieldDescriptor>();

            if ((header.LengthOfHeader - 33) % 32 != 0) { throw new NotImplementedException(); }

            int numberOfFields = (header.LengthOfHeader - 33) / 32;

            for (int i = 0; i < numberOfFields; i++)
            {
                buffer = reader.ReadBytes(Marshal.SizeOf(typeof(DbfFieldDescriptor)));

                columns.Add(DbfFieldDescriptor.Parse(buffer, encoding));
            }

            reader.Close();

            stream.Close();

            return columns;
        }

        public static System.Data.DataTable MakeTableSchema(string tableName, List<DbfFieldDescriptor> columns)
        {
            System.Data.DataTable result = new System.Data.DataTable(tableName);

            foreach (DbfFieldDescriptor item in columns)
            {
                switch (char.ToUpper(item.Type))
                {
                    case 'F':
                    case 'O':
                    case '+':
                        result.Columns.Add(item.Name, typeof(double));
                        break;

                    case 'I':
                        result.Columns.Add(item.Name, typeof(int));
                        break;

                    case 'Y':
                        result.Columns.Add(item.Name, typeof(decimal));
                        break;

                    case 'L':
                        result.Columns.Add(item.Name, typeof(bool));
                        break;

                    case 'D':
                    case 'T':
                    case '@':
                        result.Columns.Add(item.Name, typeof(DateTime));
                        break;

                    case 'M':
                    case 'B':
                    case 'P':
                        result.Columns.Add(item.Name, typeof(byte[]));
                        break;

                    case 'N':
                        if (item.DecimalCount == 0)
                            result.Columns.Add(item.Name, typeof(int));
                        else
                            result.Columns.Add(item.Name, typeof(double));
                        break;

                    case 'C':
                    case 'G':
                    case 'V':
                    case 'X':
                    default:
                        result.Columns.Add(item.Name, typeof(string));
                        break;
                }
            }

            return result;
        }

        public static List<DbfFieldDescriptor> MakeDbfFields(System.Data.DataColumnCollection columns)
        {
            List<DbfFieldDescriptor> result = new List<DbfFieldDescriptor>();

            foreach (System.Data.DataColumn item in columns)
            {
                result.Add(new DbfFieldDescriptor(item.ColumnName, 'C', 255, 0));
            }

            return result;
        }

        private static short GetRecordLength(List<DbfFieldDescriptor> columns)
        {
            short result = 0;

            foreach (var item in columns)
            {
                result += item.Length;
            }

            result += 1; //Deletion Flag

            return result;
        }

        public static void Write(string fileName, System.Data.DataTable table, Encoding encoding)
        {
            System.IO.Stream stream = new System.IO.FileStream(fileName, System.IO.FileMode.Create);

            System.IO.BinaryWriter writer = new System.IO.BinaryWriter(stream);

            List<DbfFieldDescriptor> columns = MakeDbfFields(table.Columns);

            DbfHeader header = new DbfHeader(table.Rows.Count, table.Columns.Count, GetRecordLength(columns), encoding);

            writer.Write(IRI.Ket.IO.BinaryStream.StructureToByteArray(header));

            foreach (var item in columns)
            {
                writer.Write(IRI.Ket.IO.BinaryStream.StructureToByteArray(item));
            }

            //Terminator
            writer.Write(byte.Parse("0D", System.Globalization.NumberStyles.HexNumber));

            for (int i = 0; i < table.Rows.Count; i++)
            {
                // All dbf field records begin with a deleted flag field. Deleted - 0x2A (asterisk) else 0x20 (space)
                writer.Write(byte.Parse("20", System.Globalization.NumberStyles.HexNumber));

                for (int j = 0; j < table.Columns.Count; j++)
                {
                    byte[] temp = new byte[columns[j].Length];

                    string value = table.Rows[i][j].ToString().Trim();

                    encoding.GetBytes(value, 0, value.Length, temp, 0);

                    writer.Write(temp);
                }
            }

            //End of file
            writer.Write(byte.Parse("1A", System.Globalization.NumberStyles.HexNumber));

            writer.Close();

            stream.Close();
        }

        public static void Write<T>(string fileName,
                                        List<T> values,
                                        List<Func<T, object>> mapping,
                                        List<DbfFieldDescriptor> columns,
                                        Encoding encoding)
        {
            try
            {

                if (columns.Count != mapping.Count)
                {
                    throw new NotImplementedException();
                }

                System.IO.Stream stream = new System.IO.FileStream(fileName, System.IO.FileMode.Create);

                System.IO.BinaryWriter writer = new System.IO.BinaryWriter(stream);

                DbfHeader header = new DbfHeader(values.Count, mapping.Count, GetRecordLength(columns), encoding);

                writer.Write(IRI.Ket.IO.BinaryStream.StructureToByteArray(header));

                foreach (var item in columns)
                {
                    writer.Write(IRI.Ket.IO.BinaryStream.StructureToByteArray(item));
                }

                //Terminator
                writer.Write(byte.Parse("0D", System.Globalization.NumberStyles.HexNumber));

                for (int i = 0; i < values.Count; i++)
                {
                    // All dbf field records begin with a deleted flag field. Deleted - 0x2A (asterisk) else 0x20 (space)
                    writer.Write(byte.Parse("20", System.Globalization.NumberStyles.HexNumber));

                    for (int j = 0; j < mapping.Count; j++)
                    {
                        byte[] temp = new byte[columns[j].Length];

                        object value = mapping[j](values[i]);

                        if (value != null)
                        {
                            encoding.GetBytes(value.ToString(), 0, value.ToString().Length, temp, 0);
                        }

                        string tt = encoding.GetString(temp);
                        var le = tt.Length;
                        writer.Write(temp);
                    }
                }

                //End of file
                writer.Write(byte.Parse("1A", System.Globalization.NumberStyles.HexNumber));

                writer.Close();

                stream.Close();

                System.IO.File.WriteAllText(GetCpgFileName(fileName), encoding.BodyName);

            }
            catch (Exception)
            {
                throw;
            }
        }

        public static string GetCpgFileName(string fileName)
        {
            string directoryName = System.IO.Path.GetDirectoryName(fileName);

            string fileNameWithoutExtension = System.IO.Path.GetFileNameWithoutExtension(fileName);

            return string.Format("{0}\\{1}.cpg", directoryName, fileNameWithoutExtension);
        }

    }



}