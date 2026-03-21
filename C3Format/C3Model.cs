using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ConquerMono.C3Format
{
    /// <summary>
    /// Top-level .c3 file loader.
    ///
    /// PHY REPLACEMENT SYSTEM:
    ///   Some PHY slots are placeholders (zero geometry) that the game fills at
    ///   runtime.  Two motion modes exist:
    ///
    ///   KeepExistingMotion (default):
    ///     New geometry inherits the CURRENT model's motion track for that slot.
    ///     Used for equipment swaps (weapons, armour) where the skeleton is shared.
    ///
    ///   UseSourceMotion:
    ///     New geometry brings its OWN motion from the source file.
    ///     Used for pets / mounts that have independent skeletons.
    ///     The source motion's bone local matrices are initialised from the OLD
    ///     motion's current pose so the attachment is seamless:
    ///       newMotion.BoneMatrix[n] = GetBoneMatrix(oldMotion, bone=0)
    ///                               × oldMotion.BoneMatrix[0]
    ///     This mirrors the original ChangePhyPartByMeshName "v_pet" block exactly.
    /// </summary>
    public class C3Model
    {
        private const string ExpectedVersion = "MAXFILE C3 00001";
        private const int MaxPhys = 16;
        private const int MaxMotions = 16;

        public string SourcePath { get; private set; }

        public List<C3Phy> Phys { get; } = new List<C3Phy>();
        public List<C3Motion> Motions { get; } = new List<C3Motion>();
        public List<C3Omni> Omnis { get; } = new List<C3Omni>();
        public List<C3Ptcl> Ptcls { get; } = new List<C3Ptcl>();
        public List<C3Scene> Scenes { get; } = new List<C3Scene>();
        public List<C3Shape> Shapes { get; } = new List<C3Shape>();

        private readonly List<C3SMotion> _pendingMotions = new List<C3SMotion>();

        /// <summary>Raised when a PHY slot is replaced. Arg = slot index.</summary>
        public event Action<int> PhyReplaced;

        // ------------------------------------------------------------------
        // Load
        // ------------------------------------------------------------------
        public static C3Model Load(string filePath,
                                   bool loadTextures = true,
                                   GraphicsDevice gd = null)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"C3 file not found: {filePath}");

            var model = new C3Model { SourcePath = filePath };

            using var fs = File.OpenRead(filePath);
            using var br = new BinaryReader(fs, Encoding.ASCII, leaveOpen: true);

            string version = Encoding.ASCII.GetString(br.ReadBytes(16)).TrimEnd('\0');
            if (version != ExpectedVersion)
                throw new InvalidDataException(
                    $"Unsupported C3 version '{version}'. Expected '{ExpectedVersion}'.");

            long fileSize = fs.Length;

            while (fs.Position < fileSize)
            {
                if (fileSize - fs.Position < 8) break;

                var chunk = ChunkHeader.Read(br);
                long chunkEnd = fs.Position + chunk.ChunkSize;

                switch (chunk.Tag)
                {
                    case "PHY ":
                    case "PHY3":
                    case "PHY4":
                        if (model.Phys.Count < MaxPhys)
                            model.Phys.Add(C3Phy.Load(br, chunk.Tag));
                        else
                            fs.Seek(chunk.ChunkSize, SeekOrigin.Current);
                        break;

                    case "MOTI":
                        if (model.Motions.Count < MaxMotions)
                            model.Motions.Add(C3Motion.Load(br));
                        else
                            fs.Seek(chunk.ChunkSize, SeekOrigin.Current);
                        break;

                    case "OMNI":
                        {
                            var omni = new C3Omni();
                            uint nLen = br.ReadUInt32();
                            omni.Name = Encoding.ASCII.GetString(br.ReadBytes((int)nLen)).TrimEnd('\0');
                            omni.Position = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                            omni.Color = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                            model.Omnis.Add(omni);
                            break;
                        }

                    case "PTCL":
                        model.Ptcls.Add(C3Ptcl.Load(br, loadTextures));
                        break;

                    case "SCEN":
                        {
                            var scene = ReadSceneBlock(br, loadTextures, gd);
                            if (scene != null) model.Scenes.Add(scene);
                            break;
                        }

                    case "SHAP":
                        model.Shapes.Add(C3Shape.Load(br, loadTextures));
                        break;

                    case "SMOT":
                        model._pendingMotions.Add(C3SMotion.Load(br));
                        break;

                    default:
                        fs.Seek(chunk.ChunkSize, SeekOrigin.Current);
                        break;
                }

                if (fs.Position > chunkEnd) fs.Seek(chunkEnd, SeekOrigin.Begin);
            }

            BindPhyMotions(model);

            for (int i = 0; i < model.Shapes.Count; i++)
                if (i < model._pendingMotions.Count)
                    model.Shapes[i].Motion = model._pendingMotions[i];

            return model;
        }

        // ------------------------------------------------------------------
        // ReplacePhy
        //
        // Replaces the PHY named 'targetName' with geometry from 'sourcePath'.
        //
        // Parameters:
        //   targetName     Name of the slot in this model to fill (e.g. "v_pet")
        //   sourcePath     .c3 file containing the replacement geometry
        //   sourceMeshName Which PHY to use from the source. null = first PHY.
        //                  Original engine uses "v_body" as the source mesh name.
        //   texturePath    Explicit texture. null = use TexIndex from source file.
        //   useSourceMotion
        //     false (default): keep THIS model's existing motion for the slot.
        //       → Equipment / armour swaps. Skeleton is shared.
        //     true: use the SOURCE file's motion for the new part.
        //       → Pet / mount swaps. Independent skeleton.
        //       The source motion's BoneMatrix[n] is primed from the OLD motion's
        //       current pose so the attach point matches visually.
        // ------------------------------------------------------------------
        public bool ReplacePhy(string targetName,
                               string sourcePath,
                               string sourceMeshName = null,
                               string texturePath = null,
                               bool useSourceMotion = false)
        {
            // --- Find target slot ---
            int slot = Phys.FindIndex(p =>
                string.Equals(p.Name, targetName, StringComparison.OrdinalIgnoreCase));
            if (slot == -1)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[C3Model] ReplacePhy: '{targetName}' not found in this model.");
                return false;
            }

            // --- Load source file ---
            C3Model src;
            try { src = Load(sourcePath, loadTextures: false); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[C3Model] ReplacePhy: failed to load '{sourcePath}': {ex.Message}");
                return false;
            }

            // --- Find source PHY ---
            // Original ChangePhyPartByMeshName always looks for "v_body" as the
            // source mesh.  We default to first PHY when no name is specified.
            int srcPhyIndex;
            C3Phy newPhy;

            if (sourceMeshName == null)
            {
                if (src.Phys.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[C3Model] ReplacePhy: no PHY in '{sourcePath}'.");
                    return false;
                }
                srcPhyIndex = 0;
                newPhy = src.Phys[0];
            }
            else
            {
                srcPhyIndex = src.Phys.FindIndex(p =>
                    string.Equals(p.Name, sourceMeshName, StringComparison.OrdinalIgnoreCase));
                if (srcPhyIndex == -1)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[C3Model] ReplacePhy: mesh '{sourceMeshName}' not found in '{sourcePath}'.");
                    return false;
                }
                newPhy = src.Phys[srcPhyIndex];
            }

            // --- Rename to target slot identity ---
            newPhy.Name = targetName;

            // --- Texture ---
            if (texturePath != null)
                newPhy.TexIndex = C3Texture.Texture_Load(texturePath);

            // --- Motion binding ---
            C3Motion oldMotion = Phys[slot].Motion;   // save before overwrite

            if (!useSourceMotion || srcPhyIndex >= src.Motions.Count)
            {
                // Keep existing motion (equipment / armour mode)
                newPhy.Motion = oldMotion;
            }
            else
            {
                // Use source motion (pet / mount mode)
                // Mirrors ChangePhyPartByMeshName "v_pet" block:
                //
                //   newMotion.matrix[n] = Motion_GetMatrix(oldMotion, bone=0)
                //                       × oldMotion.matrix[0]
                //
                // This primes the source skeleton's bone local matrices with
                // the character's current pose at bone 0, aligning the attach point.
                C3Motion newMotion = src.Motions[srcPhyIndex];
                newPhy.Motion = newMotion;

                if (oldMotion != null)
                {
                    // GetBoneMatrix at current frame, bone index 0
                    Matrix attachPose = oldMotion.GetBoneMatrix(0);
                    Matrix attachLocal = oldMotion.BoneMatrix.Count > 0
                                       ? oldMotion.BoneMatrix[0]
                                       : Matrix.Identity;
                    Matrix combined = attachPose * attachLocal;

                    for (int n = 0; n < newMotion.BoneMatrix.Count; n++)
                        newMotion.BoneMatrix[n] = combined;
                }
            }            
            // --- Swap slot ---
            Phys[slot] = newPhy;

            // Initial skinning pass so OutputVertices are valid before first draw
            newPhy.Calculate();

            // Notify renderer to rebuild GPU buffers
            PhyReplaced?.Invoke(slot);

            return true;
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        public C3Phy FindPhy(string name) =>
            Phys.Find(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

        public bool IsPlaceholder(string name)
        {
            var phy = FindPhy(name);
            return phy == null || phy.TotalIndexCount == 0;
        }

        // ------------------------------------------------------------------
        // ChangeMotion
        // ------------------------------------------------------------------
        public void ChangeMotion(string motionFilePath, Matrix rotationMatrix)
        {
            Motions.Clear();
            foreach (var phy in Phys) phy.Motion = null;

            var src = Load(motionFilePath);
            foreach (var m in src.Motions) Motions.Add(m);

            BindPhyMotions(this, rotationMatrix);
        }

        // ------------------------------------------------------------------
        // Per-frame helpers
        // ------------------------------------------------------------------
        public void AdvanceFrame(int step = 1)
        {
            foreach (var p in Phys) p.Motion?.NextFrame(step);
            foreach (var p in Ptcls) p.NextFrame(step);
            foreach (var s in Scenes) s.NextFrame(step);
            foreach (var s in Shapes) s.NextFrame(step);
        }

        public void SetFrame(int frame)
        {
            foreach (var p in Phys) p.Motion?.SetFrame(frame);
            foreach (var p in Ptcls) p.SetFrame(frame);
            foreach (var s in Shapes) s.SetFrame(frame);
        }

        public void Calculate() { foreach (var p in Phys) p.Calculate(); }
        public void UpdateShapes(bool bLocal = false) { foreach (var s in Shapes) s.Update(bLocal); }

        public int MaxFrameCount
        {
            get { int m = 0; foreach (var mo in Motions) if (mo.FrameCount > m) m = mo.FrameCount; return m; }
        }

        // ------------------------------------------------------------------
        // Private: read SCEN block from open stream
        // ------------------------------------------------------------------
        private static C3Scene ReadSceneBlock(BinaryReader br,
                                              bool loadTextures, GraphicsDevice gd)
        {
            var scene = new C3Scene();

            uint nameLen = br.ReadUInt32();
            scene.Name = Encoding.ASCII.GetString(br.ReadBytes((int)nameLen)).TrimEnd('\0');

            uint vecCount = br.ReadUInt32();
            scene.Vertices = new SceneVertex[vecCount];
            for (int i = 0; i < (int)vecCount; i++)
            {
                scene.Vertices[i].Position = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                scene.Vertices[i].Normal = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                scene.Vertices[i].UV0 = new Vector2(br.ReadSingle(), br.ReadSingle());
                scene.Vertices[i].UV1 = new Vector2(br.ReadSingle(), br.ReadSingle());
            }

            uint triCount = br.ReadUInt32();
            scene.Indices = new ushort[triCount * 3];
            for (int i = 0; i < (int)(triCount * 3); i++)
                scene.Indices[i] = br.ReadUInt16();

            uint texLen = br.ReadUInt32();
            scene.TexName = Encoding.ASCII.GetString(br.ReadBytes((int)texLen)).TrimEnd('\0');
            if (loadTextures) scene.TexIndex = C3Texture.Texture_Load(scene.TexName);

            uint lmLen = br.ReadUInt32();
            if (lmLen > 0)
            {
                scene.LightTexName = Encoding.ASCII.GetString(br.ReadBytes((int)lmLen)).TrimEnd('\0');
                if (loadTextures) scene.LightTexIndex = C3Texture.Texture_Load(scene.LightTexName);
            }

            uint frameCount = br.ReadUInt32();
            scene.Frames = new Matrix[frameCount];
            for (int i = 0; i < (int)frameCount; i++)
                scene.Frames[i] = C3Motion.ReadMatrix(br);

            if (gd != null) scene.UploadGPU(gd);
            return scene;
        }

        // ------------------------------------------------------------------
        // Private: bind MOTI → PHY
        // ------------------------------------------------------------------
        private static void BindPhyMotions(C3Model model, Matrix? rotation = null)
        {
            for (int i = 0; i < model.Phys.Count; i++)
            {
                if (i < model.Motions.Count)
                    model.Phys[i].Motion = model.Motions[i];
                else
                {
                    var stub = new C3Motion { BoneCount = 1, FrameCount = 1 };
                    stub.BoneMatrix.Add(Matrix.Identity);
                    stub.KeyFrames.Add(new C3KeyFrame
                    { Pos = 0, BoneMatrices = { Matrix.Identity } });
                    model.Phys[i].Motion = stub;
                }

                if (rotation.HasValue)
                {
                    model.Phys[i].Motion.ClearMatrix();
                    model.Phys[i].Motion.Multiply(-1, rotation.Value);
                }
            }
        }
    }
}