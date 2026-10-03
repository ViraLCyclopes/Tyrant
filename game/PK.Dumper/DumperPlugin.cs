using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using PK.Dumper.Serialization;
using UnityEngine;

namespace PK.Dumper
{
    /// <summary>Idle unless pk wrote a request file; then waits for the game's databases, dumps them and quits.</summary>
    [BepInPlugin("dev.pkmodstudio.dumper", "PK Mod Studio Dumper", "1.0.0")]
    public sealed class DumperPlugin : BaseUnityPlugin
    {
        public const string RequestFileName = "pk.dumper.request.json";

        private void Awake()
        {
            var requestPath = Path.Combine(Paths.ConfigPath, RequestFileName);
            if (!File.Exists(requestPath))
            {
                Logger.LogInfo("No dump requested; the dumper stays idle.");
                return;
            }

            DumpRequest? request;
            try
            {
                request = JsonUtility.FromJson<DumpRequest>(File.ReadAllText(requestPath));
            }
            catch (Exception ex)
            {
                Logger.LogError("Unreadable dump request: " + ex.Message);
                return;
            }
            if (request == null || string.IsNullOrEmpty(request.requestId) || string.IsNullOrEmpty(request.outputDir))
            {
                Logger.LogError("Incomplete dump request; ignoring it.");
                return;
            }
            Logger.LogInfo("Dump requested (" + request.requestId + ") → " + request.outputDir);
            StartCoroutine(Run(request, requestPath));
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
                    yield break;
                }
                yield return new WaitForSecondsRealtime(1f);
            }

            Logger.LogInfo("Game databases found; waiting " + request.settleSeconds + " s for localization.");
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
        }

        private void Finish(DumpRequest request, string requestPath, DumpResult result, List<LanguageTable> languages)
        {
            try
            {
                var manifest = new DumpManifest
                {
                    RequestId = request.requestId,
                    BuildGuid = request.buildGuid ?? "",
                    DumperVersion = Info.Metadata.Version.ToString(),
                    CreatedUtc = DateTime.UtcNow.ToString("o"),
                };
                DumpWriter.Write(request.outputDir, result, languages, manifest);
                Logger.LogInfo("Dump written: " + result.Objects.Count + " objects, " + result.Errors.Count + " errors.");
            }
            catch (Exception ex)
            {
                Logger.LogError("Could not write the dump: " + ex);
            }
            finally
            {
                try { File.Delete(requestPath); } catch (Exception ex) { Logger.LogWarning("Could not delete the request: " + ex.Message); }
            }
            if (request.quitWhenDone) Application.Quit();
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
