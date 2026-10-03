using System.Numerics;
using PK.Core.Species;
using PK.Core.Workspaces;
using SharpGLTF.Schema2;

namespace PK.Core.Tests;

[Trait("Category", "Integration")]
public class ModelIntegrationTests(RealGameIndex real) : IClassFixture<RealGameIndex>
{
    private SpeciesPackResult Pack(string name)
    {
        var ws = Workspace.Create(Path.Combine(Path.GetTempPath(), "pk-tests", Guid.NewGuid().ToString("N")), real.Install);
        var species = SpeciesCatalog.Find(SpeciesCatalog.FromIndex(real.Index), name);
        return new SpeciesPackExporter().Export(real.Install, ws, real.Index, species, null, CancellationToken.None);
    }

    [SkippableFact]
    public void Catalog_lists_park_and_vivarium_species()
    {
        Skip.If(RealGameIndex.GameDir is null, "PK_GAME_DIR not set");
        var species = SpeciesCatalog.FromIndex(real.Index);

        Assert.True(species.Count >= 75, $"only {species.Count} species");
        Assert.False(species.Single(s => s.Key == "stegosaurusstenops").Vivarium);
        Assert.True(species.Single(s => s.Key == "titanoboa").Vivarium);
    }

    [SkippableFact]
    public void Stegosaurus_pack_has_rigged_lods_growth_morphs_skeleton_and_textures()
    {
        Skip.If(RealGameIndex.GameDir is null, "PK_GAME_DIR not set");
        var result = Pack("Stegosaurus Stenops");

        Assert.True(result.Models.Count(m => m.Success) >= 3);
        Assert.DoesNotContain(result.Models, m => m.Name.Contains("LOD") && !m.Success);
        var lod0 = result.Models.Single(m => m.Name.EndsWith("LOD00")).OutputPath;
        var model = ModelRoot.Load(lod0);
        Assert.Equal(106, Assert.Single(model.LogicalSkins).JointsCount);
        Assert.Equal(2, model.LogicalMeshes[0].Primitives[0].MorphTargetsCount);

        var targets = SpeciesTargets.Load(result.TargetsPath);
        Assert.Equal(3, targets.Models.Count(m => m.Mesh.Contains("LOD")));
        Assert.Equal(new[] { "Adolescent", "Infant" }, targets.Models.Single(m => m.Mesh.EndsWith("LOD00")).BlendShapes);
        Assert.True(targets.Skeleton.Count >= 106);
        Assert.True(targets.Textures.Count > 10);
        Assert.All(targets.Textures, t => Assert.True(File.Exists(Path.Combine(result.Directory, t.File))));
        Assert.NotNull(targets.Prefab.Guid);
    }

    [SkippableFact]
    public void Real_mesh_faces_agree_with_their_normals()
    {
        Skip.If(RealGameIndex.GameDir is null, "PK_GAME_DIR not set");
        var lod0 = Pack("Stegosaurus Stenops").Models.Single(m => m.Name.EndsWith("LOD00")).OutputPath;
        var primitive = ModelRoot.Load(lod0).LogicalMeshes[0].Primitives[0];
        var p = primitive.GetVertexAccessor("POSITION").AsVector3Array();
        var n = primitive.GetVertexAccessor("NORMAL").AsVector3Array();
        var idx = primitive.GetIndices();

        int agree = 0, total = 0;
        for (var k = 0; k + 2 < idx.Count; k += 3)
        {
            var face = Vector3.Cross(p[(int)idx[k + 1]] - p[(int)idx[k]], p[(int)idx[k + 2]] - p[(int)idx[k]]);
            var normal = n[(int)idx[k]] + n[(int)idx[k + 1]] + n[(int)idx[k + 2]];
            if (face.LengthSquared() < 1e-12f) continue;
            total++;
            if (Vector3.Dot(face, normal) > 0) agree++;
        }
        Assert.True(agree > total * 0.9, $"{agree}/{total} faces agree with their normals");
    }

    [SkippableFact]
    public void Compressed_gallimimus_meshes_decode_and_export()
    {
        Skip.If(RealGameIndex.GameDir is null, "PK_GAME_DIR not set");
        var result = Pack("Gallimimus");

        Assert.DoesNotContain(result.Models, m => m.Name.Contains("LOD") && !m.Success);
        var targets = SpeciesTargets.Load(result.TargetsPath);
        var lod0 = targets.Models.Single(m => m.Mesh.EndsWith("LOD00"));
        Assert.True(lod0.VertexCount > 20_000, $"{lod0.VertexCount} vertices");
        Assert.Equal(new[] { "Adolescent", "Infant" }, lod0.BlendShapes);
        Assert.True(lod0.Bones.Count > 50);
    }
}
