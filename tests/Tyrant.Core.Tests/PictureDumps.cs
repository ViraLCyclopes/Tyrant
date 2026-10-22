using Tyrant.Core.Assets;
using Tyrant.Core.Workspaces;
using Tyrant.Dumper.Serialization;

namespace Tyrant.Core.Tests;

/// <summary>
/// Two species' Nursery and Paleopedia pictures as the game links them (names only, no game content): AnimalData's
/// heroRender Sprite and infoSign canvas in the dump, and the objects they lead to in the index, wired up in a fake reader.
/// Allosaurus Anax's sprite and Background image lead to their textures by reference (a Texture2D named like the sprite is
/// a decoy); Parasaurolophus Cyrtocristatus' sprite has no texture reference (its texture is found by name), and its
/// Paleopedia texture is misspelled and lives in another built-in file.
/// </summary>
public static class PictureDumps
{
    public const string Shared = "@data/sharedassets0.assets";
    public const string Other = "@data/sharedassets1.assets";

    public static readonly AssetRecord AlloHeroSprite = new(Shared, 100, "Sprite", "Hero_Allosaurus_A", null, null, null);
    public static readonly AssetRecord AlloHeroTexture = new(Shared, 200, "Texture2D", "Hero_Allosaurus_A_packed", null, null, null);
    public static readonly AssetRecord AlloHeroDecoy = new(Shared, 201, "Texture2D", "Hero_Allosaurus_A", null, null, null);
    public static readonly AssetRecord ParaHeroSprite = new(Shared, 101, "Sprite", "Hero_Parasaurolophus_C", null, null, null);
    public static readonly AssetRecord ParaHeroTexture = new(Shared, 202, "Texture2D", "Hero_Parasaurolophus_C", null, null, null);

    public static readonly AssetRecord AlloSign = new(Shared, 300, "GameObject", "AnimalInfoSignCanvas_Allosaurus Anax", null, null, null);
    public static readonly AssetRecord AlloPicture = new(Shared, 308, "Texture2D", "Allosaurus Anax", null, null, null);
    public static readonly AssetRecord ParaSign = new(Shared, 400, "GameObject", "AnimalInfoSignCanvas_Parasaurolophus Cyrtocristatus", null, null, null);
    public static readonly AssetRecord ParaPicture = new(Other, 7, "Texture2D", "Parasarulophus Cyrtocristatus", null, null, null);
    public static readonly AssetRecord ParaPictureDecoy = new(Shared, 499, "Texture2D", "Parasaurolophus Cyrtocristatus", null, null, null);

    private static readonly List<AssetRecord> SignObjects = [];
    private static readonly Dictionary<string, AssetInspection> Inspections = new(StringComparer.Ordinal);

    static PictureDumps()
    {
        Inspections[AlloHeroSprite.Ref] = Refs(("m_RD.texture", 0, 200));
        Sign(300, "Allosaurus Anax", picture: ("m_RD.texture", 0, 308), externals: []);
        Sign(400, "Parasarulophus Cyrtocristatus", picture: ("m_RD.texture", 1, 7), externals: ["sharedassets1.assets"]);
    }

    /// <summary>
    /// A canvas GameObject (id) whose RectTransform has two children: a Title first, then BackgroundImg whose Image shows a
    /// Sprite leading to the picture (as the game's are; Background is not always first).
    /// </summary>
    private static void Sign(long id, string spriteName, (string Field, int File, long PathId) picture, IReadOnlyList<string> externals)
    {
        AssetRecord Add(long offset, string type, string name, string? script = null)
        {
            var record = new AssetRecord(Shared, id + offset, type, name, null, null, script);
            if (offset > 0) SignObjects.Add(record);
            return record;
        }
        var canvas = id == 300 ? AlloSign : ParaSign;
        var root = Add(1, "RectTransform", "");
        var title = Add(10, "RectTransform", "");
        var titleObject = Add(11, "GameObject", "Title");
        var background = Add(2, "RectTransform", "");
        var backgroundObject = Add(3, "GameObject", "BackgroundImg");
        var renderer = Add(4, "CanvasRenderer", "");
        var image = Add(6, "MonoBehaviour", "", "Image");
        var sprite = Add(7, "Sprite", spriteName);
        Inspections[canvas.Ref] = Refs(("m_Component[0].component", 0, root.PathId), ("m_Component[1].component", 0, id + 90));
        Inspections[root.Ref] = Refs(("m_GameObject", 0, canvas.PathId), ("m_Children[0]", 0, title.PathId), ("m_Children[1]", 0, background.PathId));
        Inspections[title.Ref] = Refs(("m_GameObject", 0, titleObject.PathId), ("m_Father", 0, root.PathId));
        Inspections[background.Ref] = Refs(("m_GameObject", 0, backgroundObject.PathId), ("m_Father", 0, root.PathId));
        Inspections[backgroundObject.Ref] = Refs(("m_Component[0].component", 0, background.PathId), ("m_Component[1].component", 0, renderer.PathId),
            ("m_Component[2].component", 0, image.PathId));
        Inspections[image.Ref] = Refs(("m_GameObject", 0, backgroundObject.PathId), ("m_Material", 0, 999_999), ("m_Sprite", 0, sprite.PathId));
        Inspections[sprite.Ref] = new AssetInspection(128, "{}", [new AssetReference(picture.Field, picture.File, picture.PathId)], externals);
    }

    private static AssetInspection Refs(params (string Field, int File, long PathId)[] refs) =>
        new(128, "{}", refs.Select(r => new AssetReference(r.Field, r.File, r.PathId)).ToList(), []);

    public static IReadOnlyList<AssetRecord> Records =>
        [AlloHeroSprite, AlloHeroTexture, AlloHeroDecoy, ParaHeroSprite, ParaHeroTexture, AlloSign, AlloPicture, ParaSign, ParaPicture, ParaPictureDecoy, .. SignObjects];

    /// <summary>The index holding every picture object, with the second built-in file in its archive map.</summary>
    public static AssetIndex Index()
    {
        var index = new AssetIndex { Assets = [.. Records] };
        index.Archives["sharedassets0.assets"] = Shared;
        index.Archives["sharedassets1.assets"] = Other;
        return index;
    }

    /// <summary>A fake reader that answers each picture object's references, and the pictures' sizes as the game's.</summary>
    public static FakeAssetReader Reader(FakeAssetReader? reader = null)
    {
        reader ??= new FakeAssetReader();
        foreach (var (key, inspection) in Inspections) reader.InspectionOf[key] = inspection;
        reader.TextureFactsOf[AlloHeroTexture.Ref] = new TextureFacts(512, 256, "DXT5", 10);
        reader.TextureFactsOf[ParaHeroTexture.Ref] = new TextureFacts(512, 256, "DXT5", 10);
        reader.TextureFactsOf[AlloPicture.Ref] = new TextureFacts(2048, 1152, "DXT1", 12);
        reader.TextureFactsOf[ParaPicture.Ref] = new TextureFacts(2048, 1152, "DXT1", 12);
        return reader;
    }

    /// <summary>Writes the two species' AnimalData (heroRender and infoSign references, as the dump writes them) into dataDir.</summary>
    public static void Write(string dataDir)
    {
        var result = new DumpResult();
        void Animal(string file, long id, string species, string hero, string sign) =>
            result.Objects.Add(new DumpObject(typeof(object), new EngineObjectInfo("PrehistoricKingdom.AnimalData", file, id), $$$$"""
                {"$type":"PrehistoricKingdom.AnimalData","$name":"{{{{file}}}}","$id":{{{{id}}}},"speciesID":"{{{{species}}}}",
                 "heroRender":{"$ref":{"type":"UnityEngine.Sprite","name":"{{{{hero}}}}","id":{{{{54000 + id}}}}}},
                 "infoSign":{"$ref":{"type":"PrehistoricKingdom.AnimalInfoSign.AnimalInfoSignCanvas","name":"{{{{sign}}}}","id":{{{{226000 + id}}}}}}}
                """));
        Animal("Allosaurus Anax", 1, "Allosaurus Anax", AlloHeroSprite.Name, AlloSign.Name);
        Animal("Parasaurolophus Cyrtocristatus", 2, "Parasaurolophus Cyrtocristatus", ParaHeroSprite.Name, ParaSign.Name);
        DumpWriter.Write(dataDir, result, [], new DumpManifest { RequestId = "r1", BuildGuid = "b" });
    }

    /// <summary>Dump and index written into the workspace.</summary>
    public static void WriteAll(Workspace ws)
    {
        Write(ws.DataDir);
        Index().Save(AssetIndex.PathIn(ws));
    }
}
