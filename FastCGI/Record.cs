﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.IO;
using System.Globalization;

namespace FastCGI
{
    /// <summary>
    /// A FastCGI Record.
    /// </summary>
    /// <remarks>
    /// See section 3.3 of the FastCGI Specification for details.
    /// </remarks>
    public class Record
    {
        /// <summary>
        /// Record types, used in the 'type' field of Record.
        /// </summary>
        /// <remarks>
        /// Described in the FastCGI Specification section 8.
        /// </remarks>
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
        public enum RecordType : byte
        {
            BeginRequest = Constants.FCGI_BEGIN_REQUEST,
            AbortRequest,
            EndRequest,
            Params,
            Stdin,
            Stdout,
            Stderr,
            Data,
            GetValues,
            GetValuesResult,
            UnknownType = Constants.FCGI_UNKNOWN_TYPE,
            MaxType = Constants.FCGI_MAXTYPE
        }

        /// <summary>
        /// Protocol status used for requests.
        /// Described in the FastCGI Specification section 8.
        /// </summary>
        public enum ProtocolStatus : byte
        {
            RequestComplete = Constants.FCGI_REQUEST_COMPLETE,
            CantMpxConn = Constants.FCGI_CANT_MPX_CONN,
            Overloaded = Constants.FCGI_OVERLOADED,
            UnknownRole = Constants.FCGI_UNKNOWN_ROLE
        }
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member

        /// <summary>
        /// The version byte. Should always equal <see cref="Constants.FCGI_VERSION_1"/>.
        /// </summary>
        public byte Version = Constants.FCGI_VERSION_1;

        /// <summary>
        /// The <see cref="RecordType"/> of this record.
        /// </summary>
        public RecordType Type;

        /// <summary>
        /// The request id associated with this record.
        /// </summary>
        public int RequestId;

        /// <summary>
        /// The length of <see cref="ContentData"/>.
        /// </summary>
        public int ContentLength = 0;

        /// <summary>
        /// The data contained in this record.
        /// </summary>
        public byte[] ContentData;

        /// <summary>
        /// Tries to read a dictionary of name-value pairs from the given stream
        /// </summary>
        /// <remarks>
        /// This method does not make any attempt to make sure whether this record actually contains a set of name-value pairs.
        /// It will return nonsense or throw an EndOfStreamException if the record content does not contain valid name-value pairs.
        /// </remarks>
        public static Dictionary<string, byte[]> ReadNameValuePairs(Stream stream)
        {
            var nameValuePairs = new Dictionary<string, byte[]>();

            while (stream.Position < stream.Length)
            {
                uint nameLength = ReadVarLength(stream);
                uint valueLength = ReadVarLength(stream);

                // .NET does not allow objects larger than 2GB
                // (see https://msdn.microsoft.com/en-us/library/ms241064(VS.80).aspx )
                // We do not make the effort to workaround this,
                // but simply throw an error if we encounter sizes beyond this limit.
                if (nameLength >= Int32.MaxValue ||
                   valueLength >= Int32.MaxValue)
                {
                    throw new InvalidDataException("Cannot process values larger than 2GB.");
                }

                byte[] name = new byte[nameLength];

                stream.Read(name, 0, (int)nameLength);

                byte[] value = new byte[valueLength];

                stream.Read(value, 0, (int)valueLength);

                nameValuePairs.Add(Encoding.ASCII.GetString(name), value);
            }

            return nameValuePairs;
        }

        /// <summary>
        /// Tries to read a dictionary of name-value pairs from the <see cref="ContentData"/>
        /// </summary>
        public Dictionary<string, byte[]> GetNameValuePairs()
        {
            return ReadNameValuePairs(new MemoryStream(ContentData));
        }

        /// <summary>
        /// Sets the record <see cref="ContentData"/> to a given dictionary of name-value pairs.
        /// </summary>
        public void SetNameValuePairs(Dictionary<string, byte[]> nameValuePairs)
        {
            MemoryStream stream = new MemoryStream();
            stream.Capacity = 4096;

            // Write names
            foreach (var nameValuePair in nameValuePairs)
            {
                string name = nameValuePair.Key;
                byte[] nameBuf = Encoding.ASCII.GetBytes(name);
                byte[] value = nameValuePair.Value;

                WriteVarLength(stream, (uint)nameBuf.Length);
                WriteVarLength(stream, (uint)value.Length);

                stream.Write(nameBuf, 0, nameBuf.Length);
                stream.Write(value, 0, value.Length);
            }

            ContentData = new byte[stream.Length];
            ContentLength = (int)stream.Length;
            stream.Seek(0, SeekOrigin.Begin);
            stream.Read(ContentData, 0, (int)stream.Length);
        }

        /// <summary>
        /// Reads a length from the given stream, which is encoded in one or four bytes.
        /// </summary>
        /// <remarks>
        /// See section 3.4 of the FastCGI specification for details.
        /// </remarks>
        public static UInt32 ReadVarLength(Stream stream)
        {
            byte firstByte = ReadByte(stream);
            // length values < 127 are encoded in a single byte
            if (firstByte <= 127)
            {
                return firstByte;
            }
            else
            {
                byte b2 = ReadByte(stream);
                byte b1 = ReadByte(stream);
                byte b0 = ReadByte(stream);
                return (uint)(16777216 * (0x7f & firstByte) + 65536 * b2 + 256 * b1 + b0);
            }
        }


        /// <summary>
        /// Writes a length from the given stream, which is encoded in one or four bytes.
        /// </summary>
        /// <remarks>
        /// See section 3.4 of the FastCGI specification for details.
        /// </remarks>
        public static void WriteVarLength(Stream stream, UInt32 len)
        {
            if (len <= 127)
                stream.WriteByte((byte)len);
            else
            {
                stream.WriteByte((byte)(0x80 | len / 16777216));
                stream.WriteByte((byte)(len / 65536));
                stream.WriteByte((byte)(len / 256));
                stream.WriteByte((byte)(len));
            }
        }

        /// <summary>
        /// Reads a single byte from the given stream.
        /// </summary>
        static byte ReadByte(Stream stream)
        {
            int result = stream.ReadByte();
            if (result < 0)
                throw new EndOfStreamException();
            return (byte)result;
        }

        /// <summary>
        /// Reads a 16-bit integer from the given stream.
        /// </summary>
        public static UInt16 ReadInt16(Stream stream)
        {
            byte h = ReadByte(stream);
            byte l = ReadByte(stream);
            return (UInt16)(h * 256 + l);
        }

        /// <summary>
        /// Writes a 16-bit integer to the given stream.
        /// </summary>
        public static void WriteInt16(Stream stream, UInt16 v)
        {
            var b1 = (byte)(v / 256);
            var b2 = (byte)(v);
            stream.WriteByte(b1);
            stream.WriteByte(b2);
        }

        /// <summary>
        /// Reads a single Record from the given stream.
        /// </summary>
        /// <remarks>
        /// Returns the retreived record or null if no record could be read.
        /// Will block if a partial record is on the stream, until the full record has arrived or a timeout occurs.
        /// </remarks>
        public static Record ReadRecord(Stream stream)
        {
            Record r = new Record();

            try
            {
                // Try to read a byte from the stream
                int firstByte = stream.ReadByte();

                // Reached end of stream?
                if (firstByte == -1)
                    return null;

                // Otherwise, that first byte should be the version byte of a record.
                // We now assume that we can safely read the rest of the record.
                // If the rest of the record has not yet been received, it should follow soon.
                r.Version = (byte)firstByte;

                if (r.Version != Constants.FCGI_VERSION_1)
                    throw new InvalidDataException("Invalid version number in FastCGI record header. Possibly corrupted data.");

                r.Type = (Record.RecordType)ReadByte(stream);
                r.RequestId = ReadInt16(stream);
                r.ContentLength = ReadInt16(stream);
                byte paddingLength = ReadByte(stream);

                // Skip reserved byte
                ReadByte(stream);

                r.ContentData = new byte[r.ContentLength];

                // Read content
                if(r.ContentLength > 0)
                    stream.Read(r.ContentData, 0, r.ContentLength);

                // Skip padding data
                if (paddingLength > 0)
                {
                    byte[] ignoredBuf = new byte[paddingLength];
                    stream.Read(ignoredBuf, 0, paddingLength);
                }

            }
            catch (EndOfStreamException)
            {
                throw new InvalidDataException("Unexpected end of stream. Incomplete record transmitted or corrupted data.");
            }
            catch (IOException)
            {
                // Connection has been closed, or an other error occured whie reading a record. Return a null record.
                return null;
            }

            return r;
        }
        
        /// <summary>
        /// Writes this record to the given stream.
        /// </summary>
        /// <returns>Returns the number of bytes written.</returns>
        public int WriteToStream(Stream stream)
        {
            if (ContentLength > 65535)
                throw new InvalidOperationException("Cannot send a record with more that 65535 bytes.");

            stream.WriteByte(Version);
            stream.WriteByte((byte)Type);
            WriteInt16(stream, (UInt16)RequestId);
            WriteInt16(stream, (UInt16)ContentLength);

            // No padding
            stream.WriteByte(0);
            // Reserved byte
            stream.WriteByte(0);

            if(ContentLength > 0)
                stream.Write(ContentData, 0, ContentLength);

            return Constants.FCGI_HEADER_LEN + ContentLength;
        }

        /// <summary>
        /// Creates a Stdout record from the given data and request id
        /// </summary>
        public static Record CreateStdout(byte[] data, int requestId)
        {
            return new Record
            {
                Type = Record.RecordType.Stdout,
                RequestId = requestId,
                ContentLength = data.Length,
                ContentData = data
            };
        }

        /// <summary>
        /// Creates a EndRequest record with the given request id
        /// </summary>
        public static Record CreateEndRequest(int requestId)
        {
            byte[] content = new byte[8];

            // appStatusB3 - appStatusB0
            content[0] = (byte)0;
            content[1] = (byte)0;
            content[2] = (byte)0;
            content[3] = (byte)0;

            // protocolStatus
            content[4] = (byte)ProtocolStatus.RequestComplete;

            // reserved bytes
            content[5] = 0;
            content[6] = 0;
            content[7] = 0;

            return new Record
            {
                Type = Record.RecordType.EndRequest,
                RequestId = requestId,
                ContentLength = content.Length,
                ContentData = content
            };
        }

        /// <summary>
        /// Creates a GetValuesResult record from the given config values.
        /// </summary>
        public static Record CreateGetValuesResult(int maxConnections, int maxRequests, bool multiplexing)
        {
            var nameValuePairs = new Dictionary<string, byte[]>();

            // Names and values are encoded as strings.
            nameValuePairs.Add(Constants.FCGI_MAX_CONNS, Encoding.ASCII.GetBytes(maxConnections.ToString(CultureInfo.InvariantCulture)));
            nameValuePairs.Add(Constants.FCGI_MAX_REQS, Encoding.ASCII.GetBytes(maxRequests.ToString(CultureInfo.InvariantCulture)));
            nameValuePairs.Add(Constants.FCGI_MPXS_CONNS, Encoding.ASCII.GetBytes(multiplexing ? "1" : "0"));

            var record = new Record
            {
                RequestId = 0,
                Type = RecordType.GetValuesResult
            };

            record.SetNameValuePairs(nameValuePairs);

            return record;
        }

        /// <summary>
        /// Used internally. Writes the record to the given stream. Used for sending records to the webserver.
        /// </summary>
        public void Send(Stream stream)
        {
            var memStr = new MemoryStream();
            memStr.Capacity = ContentLength + Constants.FCGI_HEADER_LEN;

            int recordSize = WriteToStream(memStr);
            stream.Write(memStr.GetBuffer(), 0, recordSize);
        }

        /// <summary>
        /// Convert <see cref="Type"/> and <see cref="RequestId"/> to string
        /// </summary>
        public override string ToString()
        {
            return "{Record type: " + Type.ToString() + ", requestId: " + RequestId.ToString() + "}";
        }

        /// <summary>
        /// Check Equals by <see cref="Version"/>, <see cref="Type"/>, <see cref="RequestId"/>, <see cref="ContentLength"/> and <see cref="ContentData"/>
        /// </summary>
        public override bool Equals(System.Object obj)
        {
            if (obj == null)
            {
                return false;
            }

            Record r = obj as Record;
            if (r == null)
            {
                return false;
            }

            return Version == r.Version
                && Type == r.Type
                && RequestId == r.RequestId
                && ContentLength == r.ContentLength
                && ContentData.SequenceEqual(r.ContentData);
        }

        /// <summary>
        /// As in base class. Added for supress compiler warning
        /// </summary>
        public override int GetHashCode()
        {
            return base.GetHashCode();
        }
    }
}
