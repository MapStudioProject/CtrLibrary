using IONET;
using IONET.Core;
using IONET.Core.Model;
using IONET.Core.Skeleton;
using SPICA.Formats.Common;
using SPICA.Formats.CtrH3D.Model;
using SPICA.Formats.CtrH3D.Model.Material;
using SPICA.Formats.CtrH3D.Model.Mesh;
using SPICA.PICA.Commands;
using SPICA.PICA.Converters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CtrLibrary.Files.BCH
{
    public class BchModelExporter
    {
        public static void Export(H3DModel model, string filePath)
        {
            IOScene ioscene = new();
            IOModel iomodel = new();
            ioscene.Models.Add(iomodel);

            foreach (H3DMaterial material in model.Materials)
            {
                IOMaterial iomaterial = new()
                {
                    Name = material.Name,
                    Label = material.Name,
                };
                if (!string.IsNullOrEmpty(material.Texture0Name))
                {
                    string folder = Path.GetDirectoryName(filePath);
                    string path = Path.Combine(folder, material.Texture0Name + ".png");
                    if (File.Exists(path))
                    {
                        iomaterial.DiffuseMap = new()
                        {
                            FilePath = path,
                            WrapS = ConvertWrap(material.TextureMappers[0].WrapU),
                            WrapT = ConvertWrap(material.TextureMappers[0].WrapV),
                        };
                    }
                }
                ioscene.Materials.Add(iomaterial);
            }

            List<IOBone> bones = new();
            foreach (var bone in model.Skeleton)
            {
                IOBone iobone = new()
                {
                    Name = bone.Name,
                    Translation = bone.Translation,
                    Scale = bone.Scale,
                    RotationEuler = bone.Rotation,
                };
                bones.Add(iobone);
            }
            for (int i = 0; i < bones.Count; i++) 
                if (model.Skeleton[i].ParentIndex != -1)
                    bones[i].Parent = bones[model.Skeleton[i].ParentIndex];

            foreach (IOBone bone in bones.Where(x => x.Parent == null))
                iomodel.Skeleton.RootBones.Add(bone);

            foreach (var mesh in model.Meshes)
            {
                PICAVertex[] vertices = MeshTransform.GetWorldSpaceVertices(model.Skeleton, mesh);
                if (vertices.Length == 0)
                    continue;

                List<ushort> indices = new();
                foreach (var subMesh in mesh.SubMeshes)
                    indices.AddRange(subMesh.Indices);

                //Sub mesh culling
                if (mesh.SubMeshes.Count == 0)
                {
                    var idx = model.Meshes.IndexOf(mesh);

                    var meshCulling = model.SubMeshCullings[idx];
                    foreach (var subMesh in meshCulling.SubMeshes)
                        indices.AddRange(subMesh.Indices);
                }

                if (indices.Count == 0)
                    continue;

                IOMesh iomesh = new()
                {
                    Name = $"Mesh{iomodel.Meshes.Count}",
                };
                iomodel.Meshes.Add(iomesh);



                IOPolygon iopoly = new()
                {
                    MaterialName = model.Materials[mesh.MaterialIndex].Name
                };
                iomesh.Polygons.Add(iopoly);

                foreach (var ind in indices)
                    iopoly.Indicies.Add(ind);

                // bone/weight list
                bool[] visited = new bool[vertices.Length];

                List<int[]> boneIndices = new List<int[]>();
                foreach (var vert in vertices)
                    boneIndices.Add(new int[4]);

                List<float[]> boneWeights = new List<float[]>();
                foreach (var vert in vertices)
                    boneWeights.Add(new float[4]);

                foreach (var subMesh in mesh.SubMeshes)
                {
                    foreach (var index in subMesh.Indices)
                    {
                        if (visited[index])
                            continue;

                        visited[index] = true;

                        var vertex = vertices[index];
                        if (subMesh.Skinning == H3DSubMeshSkinning.Smooth)
                        {
                            for (int i = 0; i < 4; i++)
                            {
                                int BIndex = vertex.Indices[i];

                                //Real bone index
                                if (BIndex < subMesh.BoneIndices.Length && BIndex > -1)
                                    BIndex = subMesh.BoneIndices[BIndex];

                                boneIndices[index][i] = BIndex;
                                boneWeights[index][i] = vertex.Weights[i];
                            }
                        }
                        else
                        {
                            int BIndex = vertex.Indices[0];

                            //Real bone index
                            if (BIndex < subMesh.BoneIndices.Length && BIndex > -1)
                                BIndex = subMesh.BoneIndices[BIndex];

                            boneIndices[index][0] = BIndex;
                            boneWeights[index][0] = 1f;
                        }
                    }
                }

                List<PICAAttributeName> attributes = new List<PICAAttributeName>();
                if (mesh.Attributes != null)
                    attributes.AddRange(mesh.Attributes.Select(x => x.Name));
                if (mesh.FixedAttributes != null)
                    attributes.AddRange(mesh.FixedAttributes.Select(x => x.Name));

                bool hasTexCoord0 = attributes.Any(x => x == PICAAttributeName.TexCoord0);
                bool hasTexCoord1 = attributes.Any(x => x == PICAAttributeName.TexCoord1);
                bool hasTexCoord2 = attributes.Any(x => x == PICAAttributeName.TexCoord2);
                bool hasColor     = attributes.Any(x => x == PICAAttributeName.Color);
                bool hasTangent   = attributes.Any(x => x == PICAAttributeName.Tangent);
                bool hasBoneIndices = attributes.Any(x => x == PICAAttributeName.BoneIndex);
                bool hasBoneWeights = attributes.Any(x => x == PICAAttributeName.BoneWeight);

                for (int v = 0; v < vertices.Length; v++)
                {
                    var vertex = vertices[v];

                    IOVertex iovertex = new()
                    {
                        Position = new System.Numerics.Vector3()
                        {
                            X = vertex.Position.X,
                            Y = vertex.Position.Y,
                            Z = vertex.Position.Z
                        },
                        Normal = new System.Numerics.Vector3()
                        {
                            X = vertex.Normal.X,
                            Y = vertex.Normal.Y,
                            Z = vertex.Normal.Z
                        },
                        Tangent = new System.Numerics.Vector3()
                        {
                            X = vertex.Tangent.X,
                            Y = vertex.Tangent.Y,
                            Z = vertex.Tangent.Z
                        },
                    };

                    if (hasTexCoord0) iovertex.SetUV(vertex.TexCoord0.X, vertex.TexCoord0.Y, 0);
                    if (hasTexCoord1) iovertex.SetUV(vertex.TexCoord1.X, vertex.TexCoord1.Y, 1);
                    if (hasTexCoord2) iovertex.SetUV(vertex.TexCoord2.X, vertex.TexCoord2.Y, 2);
                    if (hasColor) iovertex.SetColor(vertex.Color.X, vertex.Color.Y, vertex.Color.Z, vertex.Color.W, 0);

                    for (int j = 0; j < boneWeights[v].Length; j++)
                    {
                        var boneIndex = boneIndices[v][j];

                        if (boneWeights[v][j] != 0)
                            iovertex.Envelope.Weights.Add(new IOBoneWeight()
                            {
                                Weight = boneWeights[v][j],
                                BoneName = model.Skeleton[boneIndex].Name,
                                BindMatrix = model.Skeleton[boneIndex].InverseTransform,
                            });
                    }

                    iomesh.Vertices.Add(iovertex);
                };
            }

            IOManager.ExportScene(ioscene, filePath, new ExportSettings());
        }

        static WrapMode ConvertWrap(PICATextureWrap wrap)
        {
            switch (wrap)
            {
                case PICATextureWrap.Repeat: return WrapMode.REPEAT;
                case PICATextureWrap.Mirror: return WrapMode.MIRROR;
                case PICATextureWrap.ClampToEdge: return WrapMode.CLAMP;
                case PICATextureWrap.ClampToBorder: return WrapMode.CLAMP;
                default:
                    return WrapMode.REPEAT;
            }
        }
    }
}
