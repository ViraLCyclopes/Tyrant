using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using HarmonyLib;
using Tyrant.Framework.Core;
using UnityEngine;

namespace Tyrant.Framework
{
    /// <summary>
    /// A custom species' Paleopedia page.
    /// <para>
    /// Picture: the page's big picture is not heroRender but <c>infoSign.AnimalBackgroundImage</c>, the sprite on the first
    /// child of an AnimalInfoSignCanvas prefab that the species shares with its template. A species with its own picture gets
    /// its own copy of that prefab (kept inactive, out of every scene), so the template's page keeps its picture. The game shows
    /// the same sprite for the species' row in the Paleopedia's animal list and a formation's species list, its notifications,
    /// objective images, the most popular animal and the education sign's preview in the sign's info panel (the painted
    /// in-park sign comes from a texture array instead).
    /// </para>
    /// <para>
    /// Open Paleopedia check: the Nursery panel's Open Paleopedia button has one persistent call, NurseryMenuV2.OpenPaleopedia
    /// (it hands PaleopediaManager.SetSelectedAnimalScreen the selected species). Every PaleopediaAnimalScreen.SetAnimalScreen
    /// call is recorded (its animal, a short call site, an exception). One frame after a new species' Open Paleopedia, the page's
    /// header text is compared with the species' genus and species; if it shows another animal, the page is set to the species
    /// again and the log says, once per species, what was shown and what last set it.
    /// </para>
    /// </summary>
    internal static class SpeciesPaleopedia
    {
        private static GameObject? _holder;
        private static readonly Dictionary<object, (GameObject Copy, Sprite Picture)> Copies = new Dictionary<object, (GameObject, Sprite)>(ReferenceComparer.Instance);
        private static readonly PaleopediaCheck.OncePerCase Logged = new PaleopediaCheck.OncePerCase();

        // The last SetAnimalScreen call.
        private static object? _lastAnimal;
        private static string _lastCallSite = "";
        private static float _lastTime;
        private static string? _lastError;

        // A new species' Open Paleopedia, checked on a later frame.
        private sealed class Pending
        {
            public Pending(object data, string id, int frame, string? error) { Data = data; Id = id; Frame = frame; Error = error; }
            public object Data { get; }
            public string Id { get; }
            public int Frame { get; }
            public string? Error { get; }
        }
        private static Pending? _pending;

        /// <summary>Gives <paramref name="data"/> its own info sign showing <paramref name="picture"/>.</summary>
        public static void UsePicture(object data, Sprite picture, string id)
        {
            if (!(Members.Get(data, "infoSign") is Component sign) || sign == null)
                throw new InvalidOperationException("the template has no Paleopedia info sign to copy");
            if (sign.transform.childCount == 0) throw new InvalidOperationException("the template's info sign has no picture");
            var image = GameUi.Image == null ? null : sign.transform.GetChild(0).GetComponent(GameUi.Image);
            if (image == null) throw new InvalidOperationException("the template's info sign has no picture");
            var copy = UnityEngine.Object.Instantiate(sign.gameObject, Holder().transform, false); // inactive under the holder: no Awake, never drawn
            try
            {
                copy.name = id + ".infoSign";
                var own = copy.GetComponent(sign.GetType());
                var ownImage = copy.transform.GetChild(0).GetComponent(GameUi.Image!);
                if (!Members.Set(ownImage, "sprite", picture)) throw new MissingMemberException("Image.sprite");
                if (!Members.Set(data, "infoSign", own)) throw new MissingFieldException("infoSign");
                Copies[data] = (copy, picture);
            }
            catch
            {
                UnityEngine.Object.Destroy(copy);
                throw;
            }
        }

        /// <summary>A species that was skipped after getting its info sign copy: the copy and its picture go too.</summary>
        public static void Discard(object data)
        {
            if (!Copies.TryGetValue(data, out var own)) return;
            Copies.Remove(data);
            if (own.Copy != null) UnityEngine.Object.Destroy(own.Copy);
            if (own.Picture != null)
            {
                if (own.Picture.texture != null) UnityEngine.Object.Destroy(own.Picture.texture);
                UnityEngine.Object.Destroy(own.Picture);
            }
        }

        private static GameObject Holder()
        {
            if (_holder != null) return _holder;
            _holder = new GameObject("TyrantInfoSigns");
            _holder.SetActive(false);
            _holder.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(_holder);
            return _holder;
        }

        // ---- the Open Paleopedia check ----

        public static void Patch(HarmonyLib.Harmony harmony)
        {
            try
            {
                var screen = AccessTools.TypeByName("PrehistoricKingdom.PaleopediaAnimalScreen");
                var set = screen == null ? null : AccessTools.Method(screen, "SetAnimalScreen");
                var menu = AccessTools.TypeByName("PrehistoricKingdom.NurseryMenuV2");
                var open = menu == null ? null : AccessTools.Method(menu, "OpenPaleopedia", Type.EmptyTypes);
                if (set == null || open == null)
                {
                    FrameworkMod.Log.Warning("PaleopediaAnimalScreen.SetAnimalScreen or NurseryMenuV2.OpenPaleopedia was not found (game updated?); Open Paleopedia is not checked for new species.");
                    return;
                }
                harmony.Patch(set, prefix: new HarmonyMethod(typeof(SpeciesPaleopedia), nameof(BeforeSetAnimalScreen)),
                    finalizer: new HarmonyMethod(typeof(SpeciesPaleopedia), nameof(AfterSetAnimalScreen)));
                // A finalizer runs even when the game's method throws.
                harmony.Patch(open, finalizer: new HarmonyMethod(typeof(SpeciesPaleopedia), nameof(AfterOpen)));
            }
            catch (Exception ex)
            {
                FrameworkMod.Log.Warning("Open Paleopedia could not be checked for new species: " + ex.GetBaseException().Message);
            }
        }

        private static void BeforeSetAnimalScreen(object __0)
        {
            try
            {
                _lastAnimal = __0;
                _lastTime = Time.realtimeSinceStartup;
                _lastError = null;
                var frames = new StackTrace(1, false).GetFrames() ?? new StackFrame[0];
                _lastCallSite = PaleopediaCheck.CallSite(frames.Select(f => f.GetMethod()).Where(m => m != null)
                    .Select(m => (m!.DeclaringType?.FullName, m.Name)), "SetAnimalScreen");
            }
            catch (Exception)
            {
                _lastCallSite = "(unknown)";
            }
        }

        private static void AfterSetAnimalScreen(Exception? __exception)
        {
            if (__exception != null) _lastError = Describe(__exception); // the game's exception still goes on
        }

        private static void AfterOpen(object __instance, Exception? __exception)
        {
            try
            {
                var data = Members.Get(__instance, "CurrentSelectedAnimalData");
                if (data == null || !SpeciesModule.TryCustom(data, out var id, out _)) return;
                _pending = new Pending(data, id, Time.frameCount, __exception == null ? null : Describe(__exception));
            }
            catch (Exception ex)
            {
                FrameworkMod.Log.Warning("Open Paleopedia could not be checked: " + ex.GetBaseException().Message);
            }
        }

        /// <summary>Per frame: the check waits one frame after Open Paleopedia, so whatever else the game does on the press is done.</summary>
        public static void Tick()
        {
            var pending = _pending;
            if (pending == null || Time.frameCount <= pending.Frame) return;
            _pending = null;
            try { Check(pending); }
            catch (Exception ex) { FrameworkMod.Log.Warning("Open Paleopedia could not be checked: " + ex.GetBaseException().Message); }
        }

        private static void Check(Pending pending)
        {
            var game = AccessTools.TypeByName("PrehistoricKingdom.Game");
            var gui = game == null ? null : Members.GetStatic(game, "GUI");
            var manager = gui == null ? null : Members.Get(gui, "PaleopediaGUI");
            var screen = manager == null ? null : Members.Get(manager, "AnimalScreen");
            if (manager == null || screen == null) return;
            var open = Members.Get(gui!, "isPaleopediaOpen") is true; // closed again before this frame: nothing to judge
            var genus = Members.Get(pending.Data, "genusName") as string ?? "";
            var species = Members.Get(pending.Data, "speciesName") as string ?? "";
            var name = $"{genus} {species} ({pending.Id})";
            var opened = pending.Error == null ? "" : $" Open Paleopedia threw {pending.Error}.";
            var (shownGenus, shownSpecies) = Header(screen);
            var verdict = PaleopediaCheck.Judge(open, Members.Get(screen, "currentSelectedAnimal"), pending.Data, shownGenus, shownSpecies, genus, species);
            if (verdict == PaleopediaCheck.Verdict.NotOpen) return;
            if (verdict == PaleopediaCheck.Verdict.OwnPage)
            {
                if (Logged.First("own page", pending.Id))
                    FrameworkMod.Log.Msg($"Open Paleopedia shows {name}'s own page (set with {LastCall()}).{opened}");
                return;
            }
            var shown = PaleopediaCheck.Shown(shownGenus, shownSpecies);
            var setBy = LastCall();
            string outcome;
            try
            {
                AccessTools.Method(manager.GetType(), "SetSelectedAnimalScreen")?.Invoke(manager, new[] { pending.Data });
                var (g, s) = Header(screen);
                outcome = PaleopediaCheck.Judge(true, Members.Get(screen, "currentSelectedAnimal"), pending.Data, g, s, genus, species) == PaleopediaCheck.Verdict.OwnPage
                    ? "switched it." : $"switching it left {PaleopediaCheck.Shown(g, s)} on the page.";
            }
            catch (Exception ex)
            {
                outcome = "switching it threw " + Describe(ex) + ".";
            }
            if (Logged.First("other page", pending.Id))
                FrameworkMod.Log.Warning($"Open Paleopedia showed {shown} (set with {setBy}) instead of {name}; {outcome}{opened}");
        }

        private static (string? Genus, string? Species) Header(object screen) =>
            (Text(Members.Get(screen, "animalHeaderGenusText")), Text(Members.Get(screen, "animalHeaderSpeciesText")));

        private static string? Text(object? component) => component == null ? null : Members.Get(component, "text") as string;

        /// <summary>The last SetAnimalScreen call: its animal, call site, how long ago, and its exception if it threw.</summary>
        private static string LastCall()
        {
            if (_lastAnimal == null) return "no SetAnimalScreen call recorded";
            var animal = Members.Get(_lastAnimal, "speciesID") as string ?? _lastAnimal.ToString();
            var ago = Math.Max(0f, Time.realtimeSinceStartup - _lastTime);
            return $"{animal} by {_lastCallSite}, {ago:0.0}s earlier" + (_lastError == null ? "" : $", which threw {_lastError}");
        }

        private static string Describe(Exception ex)
        {
            var inner = ex.GetBaseException();
            return inner.GetType().Name + ": " + inner.Message;
        }
    }
}
