using System;
using HarmonyLib;
using Tyrant.Framework.Core;
using UnityEngine;

namespace Tyrant.Framework
{
    /// <summary>
    /// A custom species' Paleopedia page. Its big picture is not heroRender but <c>infoSign.AnimalBackgroundImage</c>: the
    /// sprite on the first child of an AnimalInfoSignCanvas prefab that the species shares with its template. A species with
    /// its own picture gets its own copy of that prefab (kept inactive, out of every scene), so the template's page keeps its
    /// picture. The same sprite is the species' row in the Paleopedia's animal list, the background of its notifications and
    /// the education sign's preview in the sign's info panel (the painted in-park sign comes from a texture array instead).
    /// </summary>
    internal static class SpeciesPaleopedia
    {
        private static GameObject? _holder;
        private static bool _openAnnounced;

        /// <summary>Gives <paramref name="data"/> its own info sign showing <paramref name="picture"/>.</summary>
        public static void UsePicture(object data, Sprite picture, string id)
        {
            if (!(Members.Get(data, "infoSign") is Component sign) || sign == null)
                throw new InvalidOperationException("the template has no Paleopedia info sign to copy");
            if (sign.transform.childCount == 0) throw new InvalidOperationException("the template's info sign has no picture");
            var image = GameUi.Image == null ? null : sign.transform.GetChild(0).GetComponent(GameUi.Image);
            if (image == null) throw new InvalidOperationException("the template's info sign has no picture");
            var copy = UnityEngine.Object.Instantiate(sign.gameObject, Holder().transform, false); // inactive under the holder: no Awake, never drawn
            copy.name = id + ".infoSign";
            var own = copy.GetComponent(sign.GetType());
            var ownImage = copy.transform.GetChild(0).GetComponent(GameUi.Image!);
            if (!Members.Set(ownImage, "sprite", picture)) throw new MissingMemberException("Image.sprite");
            if (!Members.Set(data, "infoSign", own)) throw new MissingFieldException("infoSign");
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

        /// <summary>The Nursery's Open Paleopedia: checks that the page shows the selected custom species (see <see cref="AfterOpen"/>).</summary>
        public static void Patch(HarmonyLib.Harmony harmony)
        {
            try
            {
                var menu = AccessTools.TypeByName("PrehistoricKingdom.NurseryMenuV2");
                var open = menu == null ? null : AccessTools.Method(menu, "OpenPaleopedia", Type.EmptyTypes);
                if (open == null)
                {
                    FrameworkMod.Log.Warning("NurseryMenuV2.OpenPaleopedia was not found (game updated?); Open Paleopedia is not checked for new species.");
                    return;
                }
                harmony.Patch(open, postfix: new HarmonyMethod(typeof(SpeciesPaleopedia), nameof(AfterOpen)));
            }
            catch (Exception ex)
            {
                FrameworkMod.Log.Warning("Open Paleopedia could not be checked for new species: " + ex.GetBaseException().Message);
            }
        }

        /// <summary>
        /// The game hands the page the Nursery's selected species; if the page ends up on another animal, it is set again to
        /// the custom species and the log says what it showed (so an in-game test shows what happened).
        /// </summary>
        private static void AfterOpen(object __instance)
        {
            try
            {
                var wanted = Members.Get(__instance, "CurrentSelectedAnimalData");
                if (wanted == null || !SpeciesModule.TryCustom(wanted, out var id, out _)) return;
                var game = AccessTools.TypeByName("PrehistoricKingdom.Game");
                var gui = game == null ? null : Members.GetStatic(game, "GUI");
                var manager = gui == null ? null : Members.Get(gui, "PaleopediaGUI");
                var screen = manager == null ? null : Members.Get(manager, "AnimalScreen");
                if (manager == null || screen == null) return;
                var shown = Members.Get(screen, "currentSelectedAnimal");
                if (ReferenceEquals(shown, wanted))
                {
                    if (!_openAnnounced) FrameworkMod.Log.Msg($"Open Paleopedia: {id} shows its own page.");
                    _openAnnounced = true;
                    return;
                }
                var name = shown == null ? "no animal" : Members.Get(shown, "speciesID") as string ?? shown.ToString();
                FrameworkMod.Log.Warning($"Open Paleopedia showed {name} instead of {id}; showing {id}'s own page.");
                AccessTools.Method(manager.GetType(), "SetSelectedAnimalScreen")?.Invoke(manager, new[] { wanted });
            }
            catch (Exception ex)
            {
                FrameworkMod.Log.Warning("Open Paleopedia could not be checked: " + ex.GetBaseException().Message);
            }
        }
    }
}
