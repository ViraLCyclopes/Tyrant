using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Tyrant.Framework.Core;
using UnityEngine;
using static Tyrant.Framework.GameUi;

namespace Tyrant.Framework
{
    /// <summary>
    /// The look editor's Iridescence and Fur tabs. Each control shows what this scope's draft sets, else what the layers under
    /// this scope set (<see cref="MaterialsModule.Below"/>, never a higher layer's), else the game's own; editing it sets it at
    /// this scope. A choice redraws the tab; a slider or a typed value only re-applies. Each tab starts with a Preset row: a
    /// vanilla species to start the look from (<see cref="LookEditing.ApplyPreset(LookDraft, PresetKind, SpeciesPreset)"/>).
    /// </summary>
    internal static partial class LookEditorWindow
    {
        private static readonly MaterialPropertyBlock GameBlock = new MaterialPropertyBlock();
        private static (GameObject On, GameObject Off)? _onOff;
        private static bool _redrawAsked; // a slider row's set changed what other rows show (SliderRow)
        private static Component? _redrawOnRelease; // the slider dragged when a redraw was asked: the tab is redrawn once it is let go
        private static System.Reflection.MethodInfo? _isPressed; // Selectable.IsPressed(), looked up once
        private static bool _isPressedRead;

        /// <summary>Once a frame: the redraw a slider asked for, once the player lets it go.</summary>
        private static void RedrawAfterDrag()
        {
            if (!(_redrawOnRelease is Component slider)) return;
            if (slider != null && Pressed(slider)) return;
            _redrawOnRelease = null;
            Rebuild();
        }

        /// <summary>True while the pointer holds the slider down (Selectable.IsPressed); false when that cannot be read.</summary>
        private static bool Pressed(Component slider)
        {
            if (!_isPressedRead)
            {
                _isPressedRead = true;
                _isPressed = AccessTools.Method(slider.GetType(), "IsPressed", Type.EmptyTypes);
            }
            try
            {
                return _isPressed != null && _isPressed.Invoke(slider, null) is true;
            }
            catch (System.Reflection.TargetInvocationException)
            {
                return false;
            }
        }

        private static void Rebuild()
        {
            if (_content == null || _font == null) return;
            _redrawOnRelease = null; // this is the redraw
            for (var i = _content.childCount - 1; i >= 0; i--)
            {
                var row = _content.GetChild(i).gameObject;
                row.SetActive(false);
                UnityEngine.Object.Destroy(row);
            }
            _onOff = null;
            _picker = null;
            Header();
            if (_draft == null || _animal == null || _context == null)
            {
                Note(_animal != null && _context == null
                    ? "This animal's material cannot be read (it may still be loading); select it again."
                    : "Select an animal in the park. The window stays open while you pick others.");
            }
            else if (_onFurTab) FurRows();
            else IridescenceRows();
            Footer();
        }

        // ---- iridescence ----

        private static void IridescenceRows()
        {
            PresetRow(PresetKind.Iridescence);
            var draft = _draft!;
            var variant = _context!.Variant;
            var own = draft.Current.Iridescence;
            var below = _below?.Iridescence;
            var shown = own?.Strength ?? below?.Strength;
            var strength = shown != null ? LookEditing.StrengthFor(shown, variant) : GameStrength();

            var on = LookEditing.Shimmers(own, below, variant, GameStrength());
            var onOff = Choices("Iridescence", new[]
            {
                Option("On", on, true, () =>
                {
                    if (ShownOn()) return;
                    Switch.On(draft, _below, GameHasRamp());
                    Changed(rebuild: true);
                }),
                Option("Off", !on, true, () =>
                {
                    if (!ShownOn()) return;
                    Switch.Off(draft, _below);
                    Changed(rebuild: true);
                }),
            });
            _onOff = (onOff[0], onOff[1]);

            var perVariant = shown != null && (shown.Albino != null || shown.Melanistic != null || shown.Leucistic != null);
            SliderRow(perVariant && variant != "normal" ? $"Strength ({variant})" : "Strength", strength, 0f, 1f, 0f, 1f, true,
                v => LookEditing.SetStrength(draft, shown, variant, v));
            // A hatchling's strength: the shown one slides from it to Strength as the animal grows (the game's own is 0).
            SliderRow("Baby strength", LookEditing.BabyStrengthOf(own, below), 0f, 1f, 0f, 1f, true, v =>
            {
                var ramp = draft.Current.Iridescence?.Ramp;
                LookEditing.SetBabyStrength(draft, _below, GameHasRamp(), v);
                if (draft.Current.Iridescence?.Ramp != ramp) _redrawAsked = true; // a thin film was started: the colour rows show it (SliderRow redraws)
            });
            Info("babyStrength");
            var variation = own?.Variation ?? below?.Variation ?? (shown != null ? IridescenceSettings.DefaultVariation : 0f);
            SliderRow("Variation between animals", variation, 0f, 1f, 0f, 1f, _scope != LookScope.Animal,
                v => draft.SetVariation(v));
            Info("variation");

            // Shimmer colours: the draft's own choice, else the one the animal wears (marked "from below").
            var ramp = own?.Ramp ?? below?.Ramp;
            var rampKind = LookEditing.RampKindOf(ramp);
            Heading("Shimmer colours" + (own?.Ramp == null && below?.Ramp != null ? "  <size=10>(from a lower layer)</size>" : ""));
            Info("colours");
            var ramps = new List<(string, bool, bool, Action)>
            {
                Option("Thin film", rampKind == RampKind.ThinFilm, true, () => SetFilm((float)(ramp?.ThinFilm?.Thickness ?? LookEditing.DefaultThickness), ramp?.ThinFilm?.Index, rebuild: true)),
                Option("Colours", rampKind == RampKind.Stops, true, () => PickColours(own?.Ramp, below?.Ramp)),
                Option("Game's", rampKind == RampKind.Game, true, () =>
                {
                    draft.SetRamp(new RampSource { Game = true }); // "game" beats a lower layer's ramp (null would show it again)
                    Changed(rebuild: true);
                }),
                Option("PNG", rampKind == RampKind.File, true, () => PickFirst(ramp?.File, file => draft.SetRamp(new RampSource { File = file }))),
                Option("Species", rampKind == RampKind.Species, true, () => PickSpecies(ramp?.Species, species => draft.SetRamp(new RampSource { Species = species }))),
            };
            Choices(null, ramps);
            var template = SpeciesModule.TemplateOf(_context!.Species);
            // What Game's shows is the ramp of the material the animal renders with (a custom species' is rebuilt from its
            // template's): read from the animal, so opening the tab never loads a species' prefab.
            if (LookEditing.GameRampMissing(rampKind, OwnRamp()))
                Note($"{SpeciesLooks.NameOf(template)} has no shimmer colours of its own, so Game's shows nothing: pick Thin film, Colours or Species.");
            if (rampKind == RampKind.ThinFilm)
            {
                var index = ramp!.ThinFilm!.Index;
                SliderRow("Film thickness (nm)", (float)ramp.ThinFilm.Thickness, 0f, LookEditing.MaxThickness, 0f, LookEditing.MaxThickness, true,
                    v => SetFilm(v, index, rebuild: false));
            }
            else if (rampKind == RampKind.Stops)
                ColourRows(ramp!.Stops!);
            else if (rampKind == RampKind.File)
                Cycler("Ramp", Pngs(ramp!.File), ramp.File!, file => draft.SetRamp(new RampSource { File = file }));
            else if (rampKind == RampKind.Species)
                SpeciesStepper("Colours of", SpeciesLooks.List(PresetKind.Iridescence), ramp!.Species, none: false,
                    species => PickSpecies(species, s => draft.SetRamp(new RampSource { Species = s })), null); // loads it and redraws

            // Mask.
            var mask = own?.Mask ?? below?.Mask;
            var maskKind = LookEditing.MaskKindOf(mask);
            Heading("Mask" + (own?.Mask == null && below?.Mask != null ? "  <size=10>(from a lower layer)</size>" : ""));
            Info("mask");
            Choices(null, LookEditing.MaskKinds(_scope).Select(kind => kind switch
            {
                MaskKind.Everywhere => Option("Everywhere", maskKind == kind, true, () => SetMask(new MaskSource { Everywhere = true })),
                MaskKind.Game => Option("Game's", maskKind == kind, true, () => SetMask(new MaskSource { Game = true })),
                MaskKind.Channel => Option("Channel", maskKind == kind, true, () =>
                    SetMask(new MaskSource { From = mask?.From ?? LookEditing.Slots[0], Channel = mask?.From != null ? mask.Channel : 'r', Invert = mask?.From != null && mask.Invert })),
                MaskKind.Species => Option("Species", maskKind == kind, true, () => PickSpecies(mask?.Species, species => draft.SetMask(new MaskSource { Species = species }))),
                _ => Option("PNG", maskKind == kind, true, () => PickFirst(mask?.File, file => draft.SetMask(new MaskSource { File = file }))),
            }));
            if (maskKind == MaskKind.Channel && _scope != LookScope.Species) // a species file cannot hold a mask from a skin slot
            {
                var from = mask!;
                Choices("Skin map", LookEditing.Slots.Select(slot =>
                    Option(slot, from.From == slot, true, () => SetMask(new MaskSource { From = slot, Channel = from.Channel, Invert = from.Invert }))));
                Choices("Channel", LookEditing.Channels.Select(channel =>
                    Option(char.ToUpperInvariant(channel).ToString(), from.Channel == channel, true, () => SetMask(new MaskSource { From = from.From, Channel = channel, Invert = from.Invert })))
                    .Concat(new[] { Option("Invert", from.Invert, true, () => SetMask(new MaskSource { From = from.From, Channel = from.Channel, Invert = !from.Invert })) }));
            }
            else if (maskKind == MaskKind.File)
                Cycler("Mask", Pngs(mask!.File), mask.File!, file => draft.SetMask(new MaskSource { File = file }));
            else if (maskKind == MaskKind.Species)
                SpeciesStepper("Mask of", SpeciesLooks.List(PresetKind.Iridescence), mask!.Species, none: false,
                    species => PickSpecies(species, s => draft.SetMask(new MaskSource { Species = s })), null);

            // Shaping, folded away.
            Choices(null, new[] { Option(_shapingOpen ? "Shaping  -" : "Shaping  +", false, true, () =>
            {
                _shapingOpen = !_shapingOpen;
                Rebuild();
            }) });
            Info("shaping");
            if (!_shapingOpen) return;
            var scale = own?.Scale ?? below?.Scale ?? MaterialFloat("_IridescenceScale");
            var power = own?.Power ?? below?.Power ?? MaterialFloat("_IridescencePower");
            var bias = own?.Bias ?? below?.Bias ?? MaterialFloat("_IridescenceBias");
            float Now(Func<IridescenceSettings, float?> part, float fallback) => draft.Current.Iridescence is IridescenceSettings i && part(i) is float v ? v : fallback;
            SliderRow("Scale", scale, -3f, 3f, LookEditing.MinShaping, LookEditing.MaxShaping, true, v => draft.SetShaping(v, Now(i => i.Power, power), Now(i => i.Bias, bias)));
            SliderRow("Power", power, -3f, 3f, LookEditing.MinShaping, LookEditing.MaxShaping, true, v => draft.SetShaping(Now(i => i.Scale, scale), v, Now(i => i.Bias, bias)));
            SliderRow("Bias", bias, -3f, 3f, LookEditing.MinShaping, LookEditing.MaxShaping, true, v => draft.SetShaping(Now(i => i.Scale, scale), Now(i => i.Power, power), v));
        }

        /// <summary>A thin film of this thickness, keeping a refractive index set in the app.</summary>
        private static void SetFilm(float thickness, double? index, bool rebuild)
        {
            _draft!.SetThinFilm(thickness);
            if (index is double n && _draft.Current.Iridescence?.Ramp?.ThinFilm is ThinFilmSettings film)
            {
                film.Index = n;
                _draft.Touch(); // changed directly, not through the draft's methods
            }
            if (rebuild) Changed(rebuild: true);
        }

        private static void SetMask(MaskSource? mask)
        {
            _draft!.SetMask(mask);
            Changed(rebuild: true);
        }

        /// <summary>Picks the PNG in use, else the first offered; says where to put one when there is none.</summary>
        private static void PickFirst(string? current, Action<string> use)
        {
            var files = Pngs(current);
            if (files.Count == 0)
            {
                _message = "No PNG yet: put one in Masks (Open folder), then Reload images.";
                Footer();
                return;
            }
            use(current ?? files[0]);
            Changed(rebuild: true);
        }

        /// <summary>A ramp or mask from a species: the one in use, else the iridescence preset picked, else the first that shimmers.</summary>
        private static void PickSpecies(string? current, Action<string> use)
        {
            var offered = SpeciesLooks.List(PresetKind.Iridescence);
            var species = current ?? (PresetPick.TryGetValue(PresetKind.Iridescence, out var picked) ? picked : null) ?? offered.FirstOrDefault().Id;
            if (species == null)
            {
                _message = "No species shimmers in the game's animal data (it may not be read yet).";
                Footer();
                return;
            }
            use(species);
            SpeciesLooks.Values(species); // its copy starts loading now; the animals show it once it is made
            Changed(rebuild: true);
        }

        // ---- presets ----

        /// <summary>
        /// The Preset row: ‹ None · Microraptor · … › and Apply. Picking a species starts loading its look; Apply starts the draft
        /// from it (the draft is then dirty like after any edit, and Reset still goes back to the saved look), at once or, while
        /// the species' material still loads, as soon as it is ready.
        /// </summary>
        private static void PresetRow(PresetKind kind)
        {
            var offered = SpeciesLooks.List(kind);
            var pick = PresetPick.TryGetValue(kind, out var p) && p != null && offered.Any(o => o.Id == p) ? p : null;
            SpeciesStepper("Preset", offered, pick, none: true, species =>
            {
                PresetPick[kind] = species;
                if (species != null) SpeciesLooks.Values(species); // loads in the background while the player decides
                Rebuild();
            }, ("Apply", pick != null && _draft != null, () => ApplyPreset(kind, pick!)));
        }

        private static void ApplyPreset(PresetKind kind, string species)
        {
            if (_draft == null) return;
            if (SpeciesLooks.Unavailable(species))
            {
                _presetWaiting = null;
                _message = SpeciesLooks.NameOf(species) + "'s look could not be read (see the MelonLoader log).";
                Footer();
                return;
            }
            if (!LookEditing.ApplyPreset(_draft, kind, SpeciesLooks.Source, species, _context?.Species))
            {
                if (SpeciesLooks.Unavailable(species)) // its load could not even start
                {
                    _presetWaiting = null;
                    _message = SpeciesLooks.NameOf(species) + "'s look could not be read (see the MelonLoader log).";
                    Footer();
                    return;
                }
                _presetWaiting = (kind, species); // SpeciesLoaded applies it once the material is read
                _message = "Loading " + SpeciesLooks.NameOf(species) + "'s look…";
                Footer();
                return;
            }
            _presetWaiting = null;
            _draft.Touch(); // the preset sets some parts directly
            Switch.Forget(); // the preset's values are the draft's own now: a later off/on or grow off starts from them
            FurSwitch.Forget();
            _message = "Started from " + SpeciesLooks.NameOf(species) + (kind == PresetKind.Fur ? "'s fur" : "'s iridescence") + "; edit anything, Reset goes back to the saved look.";
            Changed(rebuild: true);
        }

        /// <summary>A species' look finished loading: a preset waiting for it is applied, and the draft shows its copies.</summary>
        private static void SpeciesLoaded(string species)
        {
            if (!IsOpen || _draft == null) return;
            if (_presetWaiting is { } waiting && waiting.Species == species)
            {
                ApplyPreset(waiting.Kind, species);
                return;
            }
            PushLive(keywords: false); // the park's animals were written again; the Nursery's preview is not among them
        }

        /// <summary>
        /// A label, ‹ name ›, and an optional button: steps through the species (and "None" first when <paramref name="none"/>);
        /// <paramref name="pick"/> gets the id (null for None).
        /// </summary>
        private static void SpeciesStepper(string label, IReadOnlyList<(string Id, string Name)> items, string? current, bool none, Action<string?> pick,
            (string Text, bool Enabled, Action Click)? button)
        {
            var row = Row();
            Place((RectTransform)Label(row, _font!, label, 11f).transform, 0f, 0.3f, 0f, 0f);
            var ids = (none ? new string?[] { null } : Array.Empty<string?>()).Concat(items.Select(i => (string?)i.Id)).ToList();
            var index = ids.IndexOf(current);
            void Step(int by)
            {
                if (ids.Count == 0) return;
                pick(ids[((index < 0 ? 0 : index) + by + ids.Count) % ids.Count]);
            }
            var end = button == null ? 1f : 0.78f;
            var back = FlatButton(row, _font!, "<", Idle, Safe(() => Step(-1)));
            Place((RectTransform)back.Button.transform, 0.3f, 0.3f, 0f, 22f);
            var shown = current == null ? (ids.Count == (none ? 1 : 0) ? "none in the game's data" : "None") : SpeciesLooks.NameOf(current);
            Place((RectTransform)Label(row, _font!, shown, 11f, "Center").transform, 0.3f, end, 26f, -26f);
            var next = FlatButton(row, _font!, ">", Idle, Safe(() => Step(1)));
            Place((RectTransform)next.Button.transform, end, end, -22f, 0f);
            if (button is { } b)
            {
                var (apply, text) = FlatButton(row, _font!, b.Text, b.Enabled ? Green : Idle, Safe(b.Click));
                Place((RectTransform)apply.transform, 0.8f, 1f, 0f, 0f);
                SetInteractable(apply.GetComponent(Button!), b.Enabled);
                if (!b.Enabled) Traverse.Create(text).Property("color").SetValue(Faint);
            }
        }

        private static bool ShownOn() =>
            LookEditing.Shimmers(_draft?.Current.Iridescence, _below?.Iridescence, _context?.Variant ?? "normal", GameStrength());

        /// <summary>After a slider or typed strength: the On/Off pair shows the state without redrawing (a drag must keep its slider).</summary>
        private static void RefreshOnOff()
        {
            if (_onOff == null) return;
            var (onButton, offButton) = _onOff.Value;
            if (onButton == null || offButton == null) return;
            var on = ShownOn();
            SetColor(onButton.GetComponent(Image!), on ? Green : Idle);
            SetColor(offButton.GetComponent(Image!), on ? Idle : Green);
        }

        // ---- fur ----

        private static void FurRows()
        {
            PresetRow(PresetKind.Fur);
            var draft = _draft!;
            // Grow fur: the game's fur on a skin it gives none (fur.grow), at any scope; on also gives a length when none shows.
            var growing = LookEditing.GrowsFur(draft, _below);
            Choices("Grow fur", new[]
            {
                Option("On", growing, true, () => SetGrow(true)),
                Option("Off", !growing, true, () => SetGrow(false)),
            });
            Info("grow");
            // Fur on: which ages wear fur (fur.age); Tyrant grows it, like Grow fur, on an age the game gives none. Game's: its fur maps decide.
            var ownAge = draft.Current.Fur?.Age;
            var age = LookEditing.FurAgeOf(draft, _below);
            Choices("Fur on" + (ownAge == null && age != null ? " <size=10>(below)</size>" : ""), new[]
            {
                Option("Babies", age == FurAge.Babies, true, () => SetAge(FurAge.Babies)),
                Option("Adults", age == FurAge.Adults, true, () => SetAge(FurAge.Adults)),
                Option("Both", age == FurAge.Both, true, () => SetAge(FurAge.Both)),
                Option("Game's", age == null, ownAge != null || age == null, () => SetAge(null)), // over a lower layer's age there is no "game" to write
            });
            Info("furAge");
            if (NumberRow("_Droopage", "Droop")) Info("droop");
            var droop = Vector("_DroopDirection");
            var range = MaterialProperties.Find("_DroopDirection");
            var names = new[] { "Droop X", "Droop Y", "Droop Z" };
            for (var c = 0; c < 3; c++)
            {
                var component = c;
                SliderRow(names[c], droop[c], range?.Min ?? -20f, range?.Max ?? 20f, range?.Min ?? -20f, range?.Max ?? 20f, true, v =>
                {
                    var now = Vector("_DroopDirection"); // the other two are the ones shown (often the game's own)
                    LookEditing.SetComponent(draft, "_DroopDirection", new[] { now.x, now.y, now.z }, component, v);
                });
            }
            if (NumberRow("_Length", "Length")) Info("length");
            if (NumberRow("_FurWindStrength", "Wind strength")) Info("wind");
            NumberRow("_FurWindSpeed", "Wind speed");
            NumberRow("_FurWindScale", "Wind scale");

            var own = draft.Current.Fur?.Mask;
            var mask = own ?? _below?.Fur?.Mask;
            Heading("Fur mask" + (own == null && mask != null ? "  <size=10>(from a lower layer)</size>" : ""));
            Choices(null, new[]
            {
                Option("Game's", mask == null || mask.Game, true, () =>
                {
                    draft.SetFurMask(new MaskSource { Game = true }, infant: false); // beats a lower layer's mask
                    Changed(rebuild: true);
                }),
                Option("PNG", mask?.File != null, true, () => PickFirst(mask?.File, file => draft.SetFurMask(new MaskSource { File = file }, infant: false))),
            });
            if (mask?.File != null) Cycler("Mask", Pngs(mask.File), mask.File, file => draft.SetFurMask(new MaskSource { File = file }, infant: false));
            Note("Export mask PNG writes the fur mask the animal shows now into UserData/Tyrant/Masks.");
        }

        private static void SetGrow(bool on)
        {
            if (_draft == null || LookEditing.GrowsFur(_draft, _below) == on) return;
            FurSwitch.Set(_draft, _below, MaterialFloat("_Length"), on);
            _message = ""; // the status line can say when the game draws no fur
            Changed(rebuild: true); // the shader variant changes: Changed re-picks the keywords
        }

        private static void SetAge(FurAge? age)
        {
            if (_draft == null || _draft.Current.Fur?.Age == age) return;
            FurSwitch.SetAge(_draft, _below, MaterialFloat("_Length"), age);
            _message = "";
            Changed(rebuild: true); // the shader variant can change: Changed re-picks the keywords
        }

        /// <summary>A note for the status line while this look grows fur and the game draws none (its Fur graphics setting); "" otherwise.</summary>
        private static string FurNote() =>
            _draft != null && (LookEditing.GrowsFur(_draft, _below) || LookEditing.FurAgeOf(_draft, _below) != null) && MaterialsModule.TessellationOn == false
                ? " Grown fur needs tessellated fur in the game's graphics settings."
                : "";

        /// <summary>A slider row for a number setting; false when the setting is unknown (no row).</summary>
        private static bool NumberRow(string key, string label)
        {
            var property = MaterialProperties.Find(key);
            if (property == null) return false;
            var draft = _draft!;
            var value = Setting(key) is MaterialValue v && v.Kind == MaterialValueKind.Number ? v.Number : MaterialFloat(key);
            SliderRow(label, value, property.Min, property.Max, property.Min, property.Max, true, n => draft.SetSetting(key, MaterialValue.OfNumber(n)));
            return true;
        }

        /// <summary>The draft's value for a setting, else the one the animal wears now.</summary>
        private static MaterialValue? Setting(string key)
        {
            if (_draft != null && _draft.Current.Settings.TryGetValue(key, out var own)) return own;
            return _below != null && _below.Settings.TryGetValue(key, out var below) ? below : (MaterialValue?)null;
        }

        private static Vector3 Vector(string key) =>
            Setting(key) is MaterialValue v && v.Kind == MaterialValueKind.Vector ? new Vector3(v.Vector[0], v.Vector[1], v.Vector[2]) : (Vector3)GameVector(key);

        // ---- the game's own values (what shows when no layer sets one) ----

        private static readonly Dictionary<(Type, string), System.Reflection.PropertyInfo?> GameProperties = new Dictionary<(Type, string), System.Reflection.PropertyInfo?>();
        private static readonly Dictionary<(Type, string), System.Reflection.FieldInfo?> GameFields = new Dictionary<(Type, string), System.Reflection.FieldInfo?>();

        /// <summary>A property of a game object, looked up once per type (slider moves read some); null when it has none.</summary>
        private static object? GameProperty(object? target, string name)
        {
            if (target == null) return null;
            var key = (target.GetType(), name);
            if (!GameProperties.TryGetValue(key, out var property)) GameProperties[key] = property = AccessTools.Property(key.Item1, name);
            return property?.GetValue(target, null);
        }

        /// <summary>A field of a game object, looked up once per type; null when it has none.</summary>
        private static object? GameField(object? target, string name)
        {
            if (target == null) return null;
            var key = (target.GetType(), name);
            if (!GameFields.TryGetValue(key, out var field)) GameFields[key] = field = AccessTools.Field(key.Item1, name);
            return field?.GetValue(target);
        }

        private static Material? BaseMaterial() => GameProperty(_animal, "BaseMaterial") as Material;

        /// <summary>The first LOD renderer's property block (the game's per-animal values) into <see cref="GameBlock"/>.</summary>
        private static bool ReadBlock()
        {
            if (_animal == null || !(GameProperty(_animal, "Lods") is IEnumerable lods)) return false;
            foreach (var item in lods)
                if (item is Renderer renderer && renderer != null)
                {
                    renderer.GetPropertyBlock(GameBlock);
                    return true;
                }
            return false;
        }

        private static float MaterialFloat(string key)
        {
            if (ReadBlock() && GameBlock.HasFloat(key)) return GameBlock.GetFloat(key);
            var material = BaseMaterial();
            return material != null && material.HasProperty(key) ? material.GetFloat(key) : 0f;
        }

        private static Vector4 GameVector(string key)
        {
            if (ReadBlock() && GameBlock.HasVector(key)) return GameBlock.GetVector(key);
            var material = BaseMaterial();
            return material != null && material.HasProperty(key) ? material.GetVector(key) : Vector4.zero;
        }

        private static bool GameHasRamp() => OwnRamp() == true;

        /// <summary>True when the animal's own material has an iridescence ramp (what Game's shows); null while its material cannot be read.</summary>
        private static bool? OwnRamp()
        {
            var material = BaseMaterial();
            if (material == null) return null;
            return material.HasProperty("_IridescenceTexture") && material.GetTexture("_IridescenceTexture") != null;
        }

        /// <summary>The strength the game gives the animal (SkinVariationRuntime.iridescenceOn / iridescenceStrength), before growth.</summary>
        private static float GameStrength()
        {
            var runtime = GameProperty(_animal, "SkinVariationRuntime");
            if (runtime == null) return 0f;
            if (!(GameField(runtime, "iridescenceOn") is true)) return 0f;
            return GameField(runtime, "iridescenceStrength") is float strength ? strength : 0f;
        }

        // ---- rows ----

        private static RectTransform Row(float height = 24f)
        {
            var row = Child(_content!, "Row", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, height));
            SetHeight(row.gameObject, height);
            return row;
        }

        private static void Note(string text)
        {
            var label = Label(Row(32f), _font!, text, 11f);
            Traverse.Create(label).Property("enableWordWrapping").SetValue(true);
            Traverse.Create(label).Property("color").SetValue(Faint);
        }

        private static void Heading(string text) => Label(Row(18f), _font!, "<b>" + text + "</b>", 12f);

        private static (string Text, bool Active, bool Enabled, Action Pick) Option(string text, bool active, bool enabled, Action pick) => (text, active, enabled, pick);

        /// <summary>A row of buttons (the active one green), after an optional label; returns the buttons.</summary>
        private static List<GameObject> Choices(string? label, IEnumerable<(string Text, bool Active, bool Enabled, Action Pick)> options)
        {
            var row = Row();
            var start = 0f;
            if (label != null)
            {
                Place((RectTransform)Label(row, _font!, label, 11f).transform, 0f, 0.3f, 0f, 0f);
                start = 0.3f;
            }
            var list = options.ToList();
            var buttons = new List<GameObject>();
            for (var i = 0; i < list.Count; i++)
            {
                var option = list[i];
                var (button, text) = FlatButton(row, _font!, option.Text, option.Active ? Green : Idle, Safe(option.Pick));
                var width = (1f - start) / list.Count;
                Place((RectTransform)button.transform, start + width * i, start + width * (i + 1), i == 0 ? 0f : 2f, i == list.Count - 1 ? 0f : -2f);
                SetInteractable(button.GetComponent(Button!), option.Enabled);
                if (!option.Enabled) Traverse.Create(text).Property("color").SetValue(Faint);
                buttons.Add(button);
            }
            return buttons;
        }

        /// <summary>
        /// A label, a slider (min..max) and a box to type an exact value (boxMin..boxMax, which may reach past the slider). Both
        /// call <paramref name="set"/> on the draft and then go live. A <paramref name="set"/> that changes what other rows show
        /// sets <see cref="_redrawAsked"/>: a typed value redraws at once, a slider once it is let go (a redraw would end the drag).
        /// </summary>
        private static void SliderRow(string label, float value, float min, float max, float boxMin, float boxMax, bool enabled, Action<float> set)
        {
            var row = Row();
            var text = Label(row, _font!, label, 11f);
            Place((RectTransform)text.transform, 0f, 0.36f, 0f, -4f);
            if (!enabled) Traverse.Create(text).Property("color").SetValue(Faint);
            Component? box = null;
            Component? slider = null;
            var shown = value;
            slider = NewSlider(row, min, max, LookEditing.Clamp(value, min, max), Safe<float>(v =>
            {
                shown = v;
                _redrawAsked = false;
                set(v);
                if (!IsFocused(box)) SetInputText(box, LookEditing.Format(v));
                Changed();
                RefreshOnOff();
                if (_redrawAsked) _redrawOnRelease = slider;
                _redrawAsked = false;
            }));
            Place((RectTransform)slider.transform, 0.36f, 0.8f, 0f, -6f);
            box = NewInput(row, _inputTemplate, _font!, Safe<string>(typed =>
            {
                if (LookEditing.TryParse(typed, boxMin, boxMax, out var v) && v != shown)
                {
                    shown = v;
                    _redrawAsked = false;
                    set(v);
                    var redraw = _redrawAsked;
                    _redrawAsked = false;
                    SetSliderQuietly(slider!, LookEditing.Clamp(v, min, max));
                    Changed(rebuild: redraw);
                    if (redraw) return; // this row is drawn again
                    RefreshOnOff();
                }
                SetInputText(box, LookEditing.Format(shown)); // a bad entry shows the value again
            }));
            if (box != null)
            {
                Place((RectTransform)box.transform, 0.8f, 1f, 0f, 0f, 1f, -1f);
                SetInputText(box, LookEditing.Format(value));
                SetInteractable(box, enabled);
            }
            SetInteractable(slider, enabled);
        }

        /// <summary>The PNG in use with < > to step through the offered ones.</summary>
        private static void Cycler(string label, List<string> files, string current, Action<string> use)
        {
            var row = Row();
            Place((RectTransform)Label(row, _font!, label, 11f).transform, 0f, 0.3f, 0f, 0f);
            var index = files.FindIndex(f => string.Equals(Path.GetFullPath(f), Path.GetFullPath(current), StringComparison.OrdinalIgnoreCase));
            void Step(int by)
            {
                if (files.Count == 0) return;
                use(files[((index < 0 ? 0 : index) + by + files.Count) % files.Count]);
                Changed(rebuild: true);
            }
            var back = FlatButton(row, _font!, "<", Idle, Safe(() => Step(-1)));
            Place((RectTransform)back.Button.transform, 0.3f, 0.3f, 0f, 22f);
            var name = Label(row, _font!, Shown(current), 11f, "Center");
            Place((RectTransform)name.transform, 0.3f, 1f, 26f, -26f);
            var next = FlatButton(row, _font!, ">", Idle, Safe(() => Step(1)));
            Place((RectTransform)next.Button.transform, 1f, 1f, -22f, 0f);
        }

        /// <summary>A file as the cycler shows it: "Masks/x.png" for the player's own images, else its name.</summary>
        private static string Shown(string file) =>
            ModPaths.IsInside(file, MasksDirectory) ? "Masks/" + Path.GetFileName(file) : Path.GetFileName(file);
    }
}
