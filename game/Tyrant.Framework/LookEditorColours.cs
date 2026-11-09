using System.Collections.Generic;
using HarmonyLib;
using Tyrant.Framework.Core;
using UnityEngine;
using static Tyrant.Framework.GameUi;

namespace Tyrant.Framework
{
    /// <summary>
    /// The Iridescence tab's Shimmer colours (the ramp's stops): a strip of the gradient, a swatch per colour with + and −, a
    /// colour square (saturation × brightness at the selected colour's hue) with a hue strip beside it, and a hex box. The
    /// square and strip take PointerDown and Drag from EventTrigger entries (<see cref="OnPointer"/>, as the title and the
    /// resize grip do): each move sets the selected stop and goes live without a redraw, and the swatches, strip and markers
    /// are updated in place. The window edits the first <see cref="ShimmerColours.MaxShown"/>; any more are kept.
    /// </summary>
    internal static partial class LookEditorWindow
    {
        private const int SquareTexels = 64, GradientTexels = 32;
        private const float SquareSize = 128f, HueWidth = 18f, PickerGap = 8f;

        private static int _stop; // the selected colour, kept through redraws (clamped to the ones shown)
        private static Hsv? _hsv; // the selected colour as the square and strip show it (a grey keeps its hue)
        private static float _squareHue = -1f; // the hue the square's texture was drawn at
        private static Texture2D? _squareTexture, _hueTexture, _gradientTexture, _swatchTexture;
        private static Sprite? _ring;
        private static Color32[]? _squarePixels, _swatchPixels; // kept: a drag redraws them on every move
        private static byte[]? _gradientPixels;
        private static Picker? _picker;
        private static System.Reflection.MethodInfo? _onScroll; // ScrollRect.OnScroll, looked up once

        /// <summary>The pieces a drag updates in place (null while no colour rows are shown).</summary>
        private sealed class Picker
        {
            public RectTransform SquareMark = null!, HueMark = null!;
            public Component? Hex;
        }

        /// <summary>The stops the tab shows: the draft's own ramp, else the one the animal wears; null when it is not stops.</summary>
        private static List<Rgb>? ShownStops() => (_draft?.Current.Iridescence?.Ramp ?? _below?.Iridescence?.Ramp)?.Stops;

        /// <summary>
        /// The Colours choice: the stops in effect stay (a lower layer's are pinned in the draft, all of them); a thin film gives
        /// its colours; anything else Microraptor's blues.
        /// </summary>
        private static void PickColours(RampSource? own, RampSource? below)
        {
            if (!(ShimmerColours.Picked(own, below) is List<Rgb> stops)) return;
            if (LookEditing.RampKindOf(own ?? below) != RampKind.Stops) // the same colours keep their selection
            {
                _stop = 0;
                _hsv = null;
            }
            _draft!.SetRamp(new RampSource { Stops = stops });
            Changed(rebuild: true);
        }

        private static void ColourRows(List<Rgb> stops)
        {
            _picker = null;
            _stop = ShimmerColours.Selection(_stop, stops.Count);
            var selected = stops[_stop];
            if (!(_hsv is Hsv hsv && ShimmerColours.ToRgb(hsv).Equals(selected))) _hsv = ShimmerColours.ToHsv(selected, _hsv); // Reset, a typed colour, another stop
            var shown = Mathf.Min(stops.Count, ShimmerColours.MaxShown);

            // The gradient as the animal gets it.
            var gradient = Child(Row(14f), "Gradient", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            Picture(gradient, PickerTexture(ref _gradientTexture, GradientTexels, GradientTexels, FilterMode.Bilinear), false);

            // A swatch per colour (the selected one outlined), then + and −.
            var swatches = Row(24f);
            var slot = 0.76f / ShimmerColours.MaxShown;
            for (var i = 0; i < shown; i++)
            {
                var index = i;
                var frame = Child(swatches, "Swatch", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                Place(frame, slot * i, slot * (i + 1), 1f, -1f);
                var outline = frame.gameObject.AddComponent(Image!);
                SetColor(outline, i == _stop ? Color.white : Idle);
                var button = frame.gameObject.AddComponent(Button!);
                Traverse.Create(button).Property("targetGraphic").SetValue(outline); // tints on hover and press like the other buttons
                OnClick(frame.gameObject, Safe(() =>
                {
                    _stop = index;
                    Rebuild();
                }));
                var colour = Child(frame, "Colour", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                colour.offsetMin = new Vector2(2f, 2f);
                colour.offsetMax = new Vector2(-2f, -2f);
                if (Picture(colour, PickerTexture(ref _swatchTexture, ShimmerColours.MaxShown, 1, FilterMode.Point), false) is Component picture)
                    Traverse.Create(picture).Property("uvRect").SetValue(new Rect(i / (float)ShimmerColours.MaxShown, 0f, 1f / ShimmerColours.MaxShown, 1f));
            }
            var add = FlatButton(swatches, _font!, "+", Idle, Safe(() =>
            {
                if (!(ShownStops() is List<Rgb> now) || !ShimmerColours.CanAdd(now)) return;
                var at = _stop;
                _stop = at + 1;
                SetStops(ShimmerColours.AddAfter(now, at), rebuild: true);
            }));
            Place((RectTransform)add.Button.transform, 0.78f, 0.89f, 0f, -2f);
            Enable(add, ShimmerColours.CanAdd(stops));
            var remove = FlatButton(swatches, _font!, "-", Idle, Safe(() =>
            {
                if (!(ShownStops() is List<Rgb> now) || !ShimmerColours.CanRemove(now)) return;
                SetStops(ShimmerColours.Remove(now, _stop), rebuild: true); // the next colour takes its place (or the last)
            }));
            Place((RectTransform)remove.Button.transform, 0.89f, 1f, 2f, 0f);
            Enable(remove, ShimmerColours.CanRemove(stops));

            // The colour square, the hue strip and the hex box.
            var picker = new Picker();
            var row = Row(SquareSize);
            var square = Fixed(row, "Square", 0f, SquareSize);
            Picture(square, PickerTexture(ref _squareTexture, SquareTexels, SquareTexels, FilterMode.Bilinear), true);
            OnPointer(square.gameObject, "PointerDown", Safe<object>(data =>
            {
                NoDragThreshold(data); // the marker follows from the first pixel
                DragSquare(square, data);
            }));
            OnPointer(square.gameObject, "Drag", Safe<object>(data => DragSquare(square, data)));
            OnPointer(square.gameObject, "Scroll", Safe<object>(ForwardScroll)); // an EventTrigger takes the wheel from the scroll view
            picker.SquareMark = Mark(square, new Vector2(16f, 16f), new Vector2(3f, 3f), _ring != null ? _ring : _ring = Ring());

            var hue = Fixed(row, "Hue", SquareSize + PickerGap, HueWidth);
            Picture(hue, HueTexture(), true);
            OnPointer(hue.gameObject, "PointerDown", Safe<object>(data =>
            {
                NoDragThreshold(data);
                DragHue(hue, data);
            }));
            OnPointer(hue.gameObject, "Drag", Safe<object>(data => DragHue(hue, data)));
            OnPointer(hue.gameObject, "Scroll", Safe<object>(ForwardScroll));
            picker.HueMark = Mark(hue, new Vector2(6f, 5f), new Vector2(2f, 2f), null);
            picker.HueMark.anchorMin = new Vector2(0f, picker.HueMark.anchorMin.y);
            picker.HueMark.anchorMax = new Vector2(1f, picker.HueMark.anchorMax.y);

            var side = Child(row, "Side", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            side.offsetMin = new Vector2(SquareSize + PickerGap + HueWidth + PickerGap, 0f);
            var which = Child(side, "Which", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 18f));
            Label(which, _font!, $"Colour {_stop + 1} of {stops.Count}", 11f);
            var box = Child(side, "Hex", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -22f), new Vector2(0f, 24f));
            picker.Hex = NewInput(box, _inputTemplate, _font!, Safe<string>(typed =>
            {
                if (ShownStops() is List<Rgb> now && _stop < now.Count && ShimmerColours.TryParseHex(typed, out var colour) && !colour.Equals(now[_stop]))
                {
                    _hsv = ShimmerColours.ToHsv(colour, _hsv);
                    SetStops(ShimmerColours.SetAt(now, _stop, colour), rebuild: false);
                }
                if (ShownStops() is List<Rgb> after && _stop < after.Count) SetInputText(picker.Hex, ShimmerColours.Hex(after[_stop])); // a bad entry shows the colour again
            }));
            var help = Child(side, "Help", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            help.offsetMax = new Vector2(0f, -52f);
            var note = Label(help, _font!, stops.Count > ShimmerColours.MaxShown
                ? $"The window edits the first {ShimmerColours.MaxShown}; the other {stops.Count - ShimmerColours.MaxShown} stay."
                : "Drag the square for saturation and brightness, the strip for the hue.", 10f, "TopLeft");
            Traverse.Create(note).Property("enableWordWrapping").SetValue(true);
            Traverse.Create(note).Property("color").SetValue(Faint);

            _picker = picker;
            Refresh(stops);
        }

        private static void Enable((GameObject Button, Component Label) button, bool on)
        {
            SetInteractable(button.Button.GetComponent(Button!), on);
            if (!on) Traverse.Create(button.Label).Property("color").SetValue(Faint);
        }

        /// <summary>A rect of a fixed width from the row's left edge, full height.</summary>
        private static RectTransform Fixed(RectTransform row, string name, float x, float width)
        {
            var rect = Child(row, name, Vector2.zero, new Vector2(0f, 1f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
            rect.offsetMin = new Vector2(x, 0f);
            rect.offsetMax = new Vector2(x + width, 0f);
            return rect;
        }

        /// <summary>A marker: a black shape with a white one inside (<paramref name="inset"/> smaller; on the square a ring), not catching clicks.</summary>
        private static RectTransform Mark(RectTransform parent, Vector2 outer, Vector2 inset, Sprite? sprite)
        {
            var mark = Child(parent, "Mark", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, outer);
            var black = mark.gameObject.AddComponent(Image!);
            var white = Child(mark, "Inner", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, -inset).gameObject.AddComponent(Image!);
            foreach (var (image, colour) in new[] { (black, Color.black), (white, Color.white) })
            {
                var t = Traverse.Create(image);
                if (sprite != null) t.Property("sprite").SetValue(sprite);
                t.Property("raycastTarget").SetValue(false);
                SetColor(image, colour);
            }
            return mark;
        }

        private static Sprite Ring() => DrawIcon(32, (x, y) =>
        {
            var r = Mathf.Sqrt((x - 15.5f) * (x - 15.5f) + (y - 15.5f) * (y - 15.5f));
            return r >= 10f && r <= 15.5f;
        }, "Tyrant colour ring");

        // ---- dragging ----

        /// <summary>Where the pointer is in a rect, 0–1 each way (kept inside while a drag leaves it); null without a position.</summary>
        private static Vector2? Inside(RectTransform rect, object data)
        {
            if (!(PointerPosition(data) is Vector2 screen)
                || !RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, screen, null, out var local)) // an overlay canvas: no camera
                return null;
            var r = rect.rect;
            if (r.width <= 0f || r.height <= 0f) return null;
            return new Vector2(Mathf.Clamp01((local.x - r.xMin) / r.width), Mathf.Clamp01((local.y - r.yMin) / r.height));
        }

        private static void DragSquare(RectTransform square, object data)
        {
            if (_hsv is Hsv hsv && Inside(square, data) is Vector2 at) SetSelected(new Hsv(hsv.H, at.x, at.y));
        }

        private static void DragHue(RectTransform hue, object data)
        {
            if (_hsv is Hsv hsv && Inside(hue, data) is Vector2 at) SetSelected(new Hsv(Mathf.Min(at.y, 0.999f), hsv.S, hsv.V)); // the top is red again, as the bottom
        }

        /// <summary>The selected colour from the square or strip: set in the draft and live, the rows updated in place.</summary>
        private static void SetSelected(Hsv hsv)
        {
            _hsv = hsv;
            if (!(ShownStops() is List<Rgb> stops) || _stop >= stops.Count) return;
            var colour = ShimmerColours.Quantize(ShimmerColours.ToRgb(hsv)); // what Save writes
            if (colour.Equals(stops[_stop])) Refresh(stops); // the markers move; the look is the same
            else SetStops(ShimmerColours.SetAt(stops, _stop, colour), rebuild: false);
        }

        private static void SetStops(List<Rgb> stops, bool rebuild)
        {
            _draft!.SetRamp(new RampSource { Stops = stops }); // a new ramp object: its texture is made again, the old one swept
            Changed(rebuild);
            if (!rebuild) Refresh(stops);
        }

        /// <summary>The wheel over the square or strip scrolls the tab, as it does over the other rows.</summary>
        private static void ForwardScroll(object data)
        {
            if (_content == null || ScrollRect == null || _content.parent == null) return;
            var scroll = _content.parent.GetComponent(ScrollRect);
            if (scroll != null) (_onScroll ??= AccessTools.Method(ScrollRect, "OnScroll"))?.Invoke(scroll, new[] { data });
        }

        // ---- drawing (textures are kept and redrawn in place; a redraw of the tab makes none) ----

        private static void Refresh(List<Rgb> stops)
        {
            if (_picker == null || !(_hsv is Hsv hsv)) return;
            // Every row of the gradient is the same: one row is worked out (the ramp's own pixels) and copied into a kept buffer.
            var gradient = PickerTexture(ref _gradientTexture, GradientTexels, GradientTexels, FilterMode.Bilinear);
            var row = IridescenceRamp.Pixels(stops, GradientTexels, 1);
            _gradientPixels ??= new byte[GradientTexels * GradientTexels * 4];
            for (var y = 0; y < GradientTexels; y++) System.Buffer.BlockCopy(row, 0, _gradientPixels, y * row.Length, row.Length);
            gradient.LoadRawTextureData(_gradientPixels);
            gradient.Apply(false, false);

            var swatches = PickerTexture(ref _swatchTexture, ShimmerColours.MaxShown, 1, FilterMode.Point);
            var pixels = _swatchPixels ??= new Color32[ShimmerColours.MaxShown];
            for (var i = 0; i < pixels.Length; i++) pixels[i] = i < stops.Count ? Bytes(stops[i]) : default;
            swatches.SetPixels32(pixels);
            swatches.Apply(false, false);

            if (hsv.H != _squareHue) DrawSquare(hsv.H);
            if (_picker.SquareMark != null) _picker.SquareMark.anchorMin = _picker.SquareMark.anchorMax = new Vector2(hsv.S, hsv.V);
            if (_picker.HueMark != null)
            {
                _picker.HueMark.anchorMin = new Vector2(0f, hsv.H);
                _picker.HueMark.anchorMax = new Vector2(1f, hsv.H);
            }
            if (_stop < stops.Count && !IsFocused(_picker.Hex)) SetInputText(_picker.Hex, ShimmerColours.Hex(stops[_stop]));
        }

        /// <summary>The square at one hue: saturation across, brightness up.</summary>
        private static void DrawSquare(float hue)
        {
            var texture = PickerTexture(ref _squareTexture, SquareTexels, SquareTexels, FilterMode.Bilinear);
            _squarePixels ??= new Color32[SquareTexels * SquareTexels];
            for (var y = 0; y < SquareTexels; y++)
            for (var x = 0; x < SquareTexels; x++)
                _squarePixels[y * SquareTexels + x] = Bytes(ShimmerColours.ToRgb(new Hsv(hue, x / (SquareTexels - 1f), y / (SquareTexels - 1f))));
            texture.SetPixels32(_squarePixels);
            texture.Apply(false, false);
            _squareHue = hue;
        }

        private static Texture2D HueTexture()
        {
            if (_hueTexture != null) return _hueTexture;
            var texture = PickerTexture(ref _hueTexture, 1, SquareTexels, FilterMode.Bilinear);
            var pixels = new Color32[SquareTexels];
            for (var y = 0; y < SquareTexels; y++) pixels[y] = Bytes(ShimmerColours.ToRgb(new Hsv(y / (SquareTexels - 1f) * 0.999f, 1f, 1f)));
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }

        /// <summary>
        /// One of the picker's textures, made once (sRGB like the game's ramp, so a swatch shows what the animal gets); a texture
        /// Unity destroyed is made again (the square is then redrawn).
        /// </summary>
        private static Texture2D PickerTexture(ref Texture2D? field, int width, int height, FilterMode filter)
        {
            if (field != null) return field;
            _squareHue = -1f; // the square is drawn again on the next refresh (it may be the one made here)
            field = new Texture2D(width, height, TextureFormat.RGBA32, false, false)
            {
                name = "Tyrant shimmer colours",
                filterMode = filter,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontUnloadUnusedAsset,
            };
            return field;
        }

        private static Color32 Bytes(Rgb c) =>
            new Color32((byte)Mathf.RoundToInt(Mathf.Clamp01(c.R) * 255f), (byte)Mathf.RoundToInt(Mathf.Clamp01(c.G) * 255f), (byte)Mathf.RoundToInt(Mathf.Clamp01(c.B) * 255f), 255);
    }
}
