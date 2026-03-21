using System;
using System.IO;
using System.Text;
using Microsoft.Xna.Framework;

namespace ConquerMono.C3Format
{
    // =========================================================================
    // C3Omni  –  point-light definition stored in a .c3 file
    //
    // Binary chunk tag: "OMNI"
    // Fields: name (len+bytes), pos (Vector3), color (Vector3 RGB 0-1)
    //
    // The original engine also had fRadius and fAttenuation but they were never
    // written to the file (only set in Omni_Clear defaults).
    // =========================================================================
    public class C3Omni
    {
        public string  Name        { get; set; } = string.Empty;
        public Vector3 Position    { get; set; } = Vector3.Zero;
        public Vector3 Color       { get; set; } = Vector3.Zero;  // RGB 0–1
        public float   Radius      { get; set; } = 10f;
        public float   Attenuation { get; set; } = 1f;

        // ------------------------------------------------------------------
        // Omni_Load: find the dwIndex-th "OMNI" chunk in a .c3 file
        // ------------------------------------------------------------------
        public static C3Omni Load(string filePath, uint dwIndex = 0)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Omni file not found: {filePath}");

            using var fs = File.OpenRead(filePath);
            using var br = new BinaryReader(fs, Encoding.ASCII, leaveOpen: true);

            // Version header (16 bytes)
            string version = Encoding.ASCII.GetString(br.ReadBytes(16)).TrimEnd('\0');
            // (version check optional – original showed a message box on mismatch)

            uint found = 0;
            long fileSize = fs.Length;

            while (fs.Position < fileSize)
            {
                if (fileSize - fs.Position < 8) break;

                var chunk = ChunkHeader.Read(br);
                long chunkEnd = fs.Position + chunk.ChunkSize;

                if (chunk.Tag == "OMNI")
                {
                    if (found < dwIndex)
                    {
                        found++;
                        fs.Seek(chunk.ChunkSize, SeekOrigin.Current);
                        continue;
                    }

                    var omni = new C3Omni();

                    uint nameLen = br.ReadUInt32();
                    omni.Name = Encoding.ASCII.GetString(br.ReadBytes((int)nameLen)).TrimEnd('\0');

                    omni.Position = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                    omni.Color    = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());

                    return omni;
                }
                else
                    fs.Seek(chunk.ChunkSize, SeekOrigin.Current);

                if (fs.Position > chunkEnd) fs.Seek(chunkEnd, SeekOrigin.Begin);
            }

            throw new InvalidDataException(
                $"OMNI chunk index {dwIndex} not found in '{filePath}'.");
        }
    }
}
