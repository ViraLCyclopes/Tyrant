using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using MelonLoader;
using MelonLoader.Utils;
using PK.Dumper;
using PK.Dumper.Serialization;
using UnityEngine;

[assembly: MelonInfo(typeof(DumperMod), "PK Mod Studio Dumper", "1.0.0", "PK Mod Studio")]
[assembly: MelonGame("Blue Meridian", "Prehistoric Kingdom")]

namespace PK.Dumper
{
    /// <summary>Idle unless pk wrote a request file; then waits for the game's databases, dumps them and quits.</summary>
    public sealed class DumperMod : MelonMod
    {
        public const string RequestFileName = "pk.dumper.request.json";

        public override void OnInitializeMelon()
        {
            var requestPath = Path.Combine(MelonEnvironment.UserDataDirectory, RequestFileName);
            if (!File.Exists(requestPath))
            {
                LoggerInstance.Msg("No dump requested; the dumper stays idle.");
                return;
            }

            DumpRequest? request;
            try
            {
                request = JsonUtility.FromJson<DumpRequest>(File.ReadAllText(requestPath));
            }
            catch (Exception ex)
            {
                LoggerInstance.Error("Unreadable dump request: " + ex.Message);
                return;
            }
            if (request == null || string.IsNullOrEmpty(request.requestId) || string.IsNullOrEmpty(request.outputDir))
            {
                LoggerInstance.Error("Incomplete dump request; ignoring it.");
                return;
            }
            LoggerInstance.Msg("Dump requested (" + request.requestId + ") -> " + request.outputDir);
            MelonCoroutines.Start(Run(request, requestPath));
        }

        private IEnumerator Run(DumpRequest request, string requestPath)
        {
            var deadline = Time.realtimeSinceStartup + request.timeoutSeconds;
            UnityEngine.Object? persistent = null;
            while (persistent == null)
            {
                persistent = GameReflection.FindPersistentData();
                if (persistent != null) break;
                if (Time.realtimeSinceStartup > deadline)
                {
                    var failed = new DumpResult();
                    failed.Errors.Add("PKPersistentData never appeared within " + request.timeoutSeconds + " s.");
                    Finish(request, requestPath, failed, new List<LanguageTable>());
                    if (request.quitWhenDone) yield return QuitGame();
                    yield break;
                }
                yield return new WaitForSecondsRealtime(1f);
            }

            LoggerInstance.Msg("Game databases found; waiting " + request.settleSeconds + " s for localization.");
            yield return new WaitForSecondsRealtime(request.settleSeconds);

            DumpResult result;
            List<LanguageTable> languages;
            try
            {
                result = new DumpCollector(new UnityDumpAdapter()).Collect(GameReflection.DatabaseRoots(persistent));
                languages = GameReflection.LocalizationTables(out var localizationError);
                if (localizationError != null) result.Errors.Add(localizationError);
            }
            catch (Exception ex)
            {
                result = new DumpResult();
                result.Errors.Add("Dump failed: " + ex);
                languages = new List<LanguageTable>();
            }
            Finish(request, requestPath, result, languages);
            if (request.quitWhenDone) yield return QuitGame();
        }

        /// <summary>Quits through the game's own confirmation flag; forces the exit if the game is still running after 20 s.</summary>
        private IEnumerator QuitGame()
        {
            if (!GameReflection.ConfirmQuit()) LoggerInstance.Warning("Could not pre-confirm the game's quit dialog.");
            Application.Quit();
            yield return new WaitForSecondsRealtime(20f);
            LoggerInstance.Warning("The game did not quit; forcing exit (the dump was taken at the main menu, nothing to save).");
            Environment.Exit(0);
        }

        private void Finish(DumpRequest request, string requestPath, DumpResult result, List<LanguageTable> languages)
        {
            try
            {
                var manifest = new DumpManifest
                {
                    RequestId = request.requestId,
                    BuildGuid = request.buildGuid ?? "",
                    DumperVersion = Info.Version,
                    CreatedUtc = DateTime.UtcNow.ToString("o"),
                };
                DumpWriter.Write(request.outputDir, result, languages, manifest);
                LoggerInstance.Msg("Dump written: " + result.Objects.Count + " objects, " + result.Errors.Count + " errors.");
            }
            catch (Exception ex)
            {
                LoggerInstance.Error("Could not write the dump: " + ex);
            }
            finally
            {
                try { File.Delete(requestPath); } catch (Exception ex) { LoggerInstance.Warning("Could not delete the request: " + ex.Message); }
            }
        }
    }

    [Serializable]
    public sealed class DumpRequest
    {
        public string requestId = "";
        public string outputDir = "";
        public string? buildGuid;
        public float timeoutSeconds = 240f;
        public float settleSeconds = 5f;
        public bool quitWhenDone = true;
    }
}
