using System.IO;
using System.Text;

namespace ConquerMono.C3Format
{
    /// <summary>
    /// 8-byte block header preceding every chunk in a .c3 file.
    /// Layout: 4-byte ASCII tag + 4-byte little-endian uint32 body size.
    /// </summary>
    public class ChunkHeader
    {
        public byte[] ChunkID   { get; private set; } = new byte[4];
        public uint   ChunkSize { get; private set; }

        public string Tag => Encoding.ASCII.GetString(ChunkID);

        public static ChunkHeader Read(BinaryReader br)
        {
            var h       = new ChunkHeader();
            h.ChunkID   = br.ReadBytes(4);
            h.ChunkSize = br.ReadUInt32();
            return h;
        }

        public override string ToString() => $"{Tag} (size={ChunkSize})";
    }
}
