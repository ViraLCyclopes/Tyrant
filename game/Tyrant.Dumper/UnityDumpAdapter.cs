using Tyrant.Dumper.Serialization;
using UnityEngine;

namespace Tyrant.Dumper
{
    /// <summary>Unity specifics for the serializer: object identity, which assets are data, and engine value types.</summary>
    internal sealed class UnityDumpAdapter : IDumpAdapter
    {
        private const string LocalizationAssetType = "PrehistoricKingdom.PKLocalizationAsset";

        public bool IsEngineObject(object value) => value is Object;

        public bool IsEngineBaseType(System.Type type)
        {
            var ns = type.Namespace;
            return ns != null && (ns.StartsWith("UnityEngine", System.StringComparison.Ordinal) || ns.StartsWith("Sirenix", System.StringComparison.Ordinal));
        }

        public EngineObjectInfo Describe(object engineObject)
        {
            var type = engineObject.GetType().FullName ?? "?";
            var unityObject = (Object)engineObject;
            // Unity's overloaded null check also detects destroyed objects.
            return unityObject != null
                ? new EngineObjectInfo(type, unityObject.name, unityObject.GetInstanceID())
                : new EngineObjectInfo(type, "(destroyed)", 0);
        }

        public bool IsDumpable(object engineObject) => engineObject is ScriptableObject so && so != null;

        public bool IsInline(object engineObject) => engineObject.GetType().FullName == LocalizationAssetType && (Object)engineObject != null;

        public bool TryWriteSpecial(object value, JsonWriter w)
        {
            switch (value)
            {
                case AnimationCurve curve:
                    w.StartObject();
                    w.Name("keys"); w.StartArray();
                    foreach (var k in curve.keys)
                    {
                        w.StartObject();
                        w.Name("time"); w.Number(k.time);
                        w.Name("value"); w.Number(k.value);
                        w.Name("inTangent"); w.Number(k.inTangent);
                        w.Name("outTangent"); w.Number(k.outTangent);
                        w.Name("inWeight"); w.Number(k.inWeight);
                        w.Name("outWeight"); w.Number(k.outWeight);
                        w.Name("weightedMode"); w.String(k.weightedMode.ToString());
                        w.EndObject();
                    }
                    w.EndArray();
                    w.Name("preWrapMode"); w.String(curve.preWrapMode.ToString());
                    w.Name("postWrapMode"); w.String(curve.postWrapMode.ToString());
                    w.EndObject();
                    return true;
                case Gradient gradient:
                    w.StartObject();
                    w.Name("mode"); w.String(gradient.mode.ToString());
                    w.Name("colorKeys"); w.StartArray();
                    foreach (var k in gradient.colorKeys)
                    {
                        w.StartObject();
                        w.Name("time"); w.Number(k.time);
                        w.Name("r"); w.Number(k.color.r); w.Name("g"); w.Number(k.color.g); w.Name("b"); w.Number(k.color.b); w.Name("a"); w.Number(k.color.a);
                        w.EndObject();
                    }
                    w.EndArray();
                    w.Name("alphaKeys"); w.StartArray();
                    foreach (var k in gradient.alphaKeys)
                    {
                        w.StartObject();
                        w.Name("time"); w.Number(k.time);
                        w.Name("alpha"); w.Number(k.alpha);
                        w.EndObject();
                    }
                    w.EndArray();
                    w.EndObject();
                    return true;
                case Rect r:
                    w.StartObject();
                    w.Name("x"); w.Number(r.x); w.Name("y"); w.Number(r.y); w.Name("width"); w.Number(r.width); w.Name("height"); w.Number(r.height);
                    w.EndObject();
                    return true;
                case Bounds b:
                    w.StartObject();
                    w.Name("center"); Vector(w, b.center);
                    w.Name("size"); Vector(w, b.size);
                    w.EndObject();
                    return true;
                case LayerMask mask:
                    w.Number((long)mask.value);
                    return true;
                // The integer types keep their numbers in private fields (m_X…), so the field walk writes them as {}.
                // Names are their properties', which the species record editor sets.
                case Vector2Int v:
                    w.StartObject();
                    w.Name("x"); w.Number((long)v.x); w.Name("y"); w.Number((long)v.y);
                    w.EndObject();
                    return true;
                case Vector3Int v:
                    IntVector(w, v);
                    return true;
                case RectInt r:
                    w.StartObject();
                    w.Name("x"); w.Number((long)r.x); w.Name("y"); w.Number((long)r.y); w.Name("width"); w.Number((long)r.width); w.Name("height"); w.Number((long)r.height);
                    w.EndObject();
                    return true;
                case BoundsInt b:
                    w.StartObject();
                    w.Name("position"); IntVector(w, b.position);
                    w.Name("size"); IntVector(w, b.size);
                    w.EndObject();
                    return true;
                default:
                    return false;
            }
        }

        private static void Vector(JsonWriter w, Vector3 v)
        {
            w.StartObject();
            w.Name("x"); w.Number(v.x); w.Name("y"); w.Number(v.y); w.Name("z"); w.Number(v.z);
            w.EndObject();
        }

        private static void IntVector(JsonWriter w, Vector3Int v)
        {
            w.StartObject();
            w.Name("x"); w.Number((long)v.x); w.Name("y"); w.Number((long)v.y); w.Name("z"); w.Number((long)v.z);
            w.EndObject();
        }
    }
}
