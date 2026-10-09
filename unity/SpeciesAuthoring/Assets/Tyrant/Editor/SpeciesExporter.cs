using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Tyrant.SpeciesAuthoring
{
    // Editor-only tooling. No Tyrant authoring scripts are serialized into animal prefabs.
    public sealed class SpeciesExporter : EditorWindow
    {
        public const string EditorVersion = "2022.3.62f3";
        private static readonly string[] RequiredComponents = { "PrehistoricKingdom.Animal", "AnimalSetup", "PrehistoricKingdom.AnimalsV2.AnimalAnimationController", "PrehistoricKingdom.AnimalGrowthManager", "PrehistoricKingdom.Audio.AnimalAudioComponent" };
        private GameObject prefab;
        private string output;
        private string status = "Select a complete PK animal prefab, then validate it.";

        [MenuItem("Tyrant/Species prefab exporter")]
        public static void Open() => GetWindow<SpeciesExporter>("Tyrant species");

        [MenuItem("Tyrant/Configure local PK references")]
        public static void ConfigureReferences()
        {
            AssetDatabase.StartAssetEditing();
            try
            {
            foreach (var file in Directory.GetFiles("Assets/PKReferences", "*.dll"))
            {
                var importer = AssetImporter.GetAtPath(file.Replace('\\', '/')) as PluginImporter;
                if (importer == null) continue;
                importer.SetCompatibleWithEditor(true);
                importer.SetCompatibleWithPlatform(BuildTarget.StandaloneWindows64, true);
                importer.SaveAndReimport();
            }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            Debug.Log("TYRANT_REFERENCES_CONFIGURED");
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("PK species authoring", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Use Unity " + EditorVersion + ". This exports a complete animal prefab, including its local asset dependencies. Species settings stay in Tyrant's SQLite database.", MessageType.Info);
            prefab = (GameObject)EditorGUILayout.ObjectField("Animal prefab", prefab, typeof(GameObject), false);
            if (GUILayout.Button("Validate prefab"))
            {
                var problems = Validate(prefab);
                status = problems.Count == 0 ? "Structural checks passed. An in-game spawn test is still required." : string.Join("\n", problems);
            }
            if (GUILayout.Button("Choose export folder")) output = EditorUtility.OpenFolderPanel("Export animal bundle", output ?? "", "");
            EditorGUILayout.LabelField("Export folder", output ?? "Not selected");
            if (GUILayout.Button("Build Windows prefab bundle"))
            {
                try { status = Build(prefab, output); }
                catch (Exception ex) { status = ex.Message; Debug.LogException(ex); }
            }
            EditorGUILayout.HelpBox(status, MessageType.None);
            EditorGUILayout.HelpBox("Do not enter Play Mode: PK's components require the running game. This project does not recreate the game's scenes or databases.", MessageType.Info);
        }

        public static List<string> Validate(GameObject animal)
        {
            var errors = new List<string>();
            if (Application.unityVersion != EditorVersion) errors.Add("Open this project in Unity " + EditorVersion + ".");
            if (animal == null) { errors.Add("Select an animal prefab from the Project window."); return errors; }
            var path = AssetDatabase.GetAssetPath(animal);
            if (!path.StartsWith("Assets/", StringComparison.Ordinal) || !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) || !PrefabUtility.IsPartOfPrefabAsset(animal))
                errors.Add("Select a saved .prefab asset, not a scene object or imported FBX.");
            if (animal.activeSelf) errors.Add("Disable the prefab root. Tyrant assigns the species identity before enabling it in the game.");
            foreach (var name in RequiredComponents)
                if (!animal.GetComponents<Component>().Any(c => c != null && c.GetType().FullName == name))
                    errors.Add("Root is missing " + name + ". Import the game's DLLs and configure the animal components.");
            if (animal.GetComponentInChildren<Animator>(true) == null) errors.Add("The prefab needs an Animator and configured animation assets.");
            var renderers = animal.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (renderers.Length == 0) errors.Add("The prefab needs a SkinnedMeshRenderer.");
            foreach (var renderer in renderers)
            {
                if (renderer.sharedMesh == null) errors.Add(renderer.name + ": missing mesh.");
                if (renderer.sharedMaterials.Length == 0 || renderer.sharedMaterials.Any(m => m == null)) errors.Add(renderer.name + ": missing material.");
                if (renderer.bones.Any(b => b == null)) errors.Add(renderer.name + ": missing bone reference.");
            }
            foreach (var component in animal.GetComponentsInChildren<Component>(true))
            {
                if (component == null) { errors.Add("The prefab contains a missing script."); continue; }
                if (component is MonoBehaviour)
                {
                    var assembly = component.GetType().Assembly.GetName().Name;
                    if (!File.Exists("Assets/PKReferences/" + assembly + ".dll"))
                        errors.Add(component.GetType().FullName + ": script assembly is not an imported PK reference. Custom C# scripts require a separate runtime mod.");
                }
                using (var serialized = new SerializedObject(component))
                {
                    var property = serialized.GetIterator();
                    while (property.Next(true))
                        if (property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue == null && property.objectReferenceInstanceIDValue != 0)
                            errors.Add(component.name + ": broken reference at " + property.propertyPath);
                }
            }
            return errors.Distinct().ToList();
        }

        // Batch diagnostic: checks real PK type loading and a Windows bundle roundtrip.
        // The resulting probe is not an animal and must not be attached to a species.
        public static void VerifyEnvironment()
        {
            if (Application.unityVersion != EditorVersion) throw new InvalidOperationException("Wrong Unity version.");
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            var types = RequiredComponents.Select(name => assemblies.Select(a => a.GetType(name, false)).FirstOrDefault(t => t != null)
                ?? throw new TypeLoadException("PK reference is not loaded: " + name)).ToArray();
            foreach (var type in types)
                if (!typeof(MonoBehaviour).IsAssignableFrom(type)) throw new InvalidOperationException(type.FullName + " is not a MonoBehaviour.");
            var scripts = AssetDatabase.LoadAllAssetsAtPath("Assets/PKReferences/PK.Authoring.Gameplay.dll").OfType<MonoScript>().ToArray();
            Debug.Log("TYRANT_PK_SCRIPT_COUNT " + scripts.Length);
            foreach (var script in scripts.Where(s => s.name == "Animal" || s.name == "AnimalSetup"))
                Debug.Log("TYRANT_PK_SCRIPT " + script.name + " class=" + script.GetClass() + " assembly=" + script.GetClass()?.Assembly.GetName().Name);
            const string assetPath = "Assets/TyrantDiagnostics/EnvironmentProbe.prefab";
            Directory.CreateDirectory("Assets/TyrantDiagnostics");
            var instance = new GameObject("Tyrant environment probe - NOT A PLAYABLE ANIMAL");
            instance.SetActive(false);
            try
            {
                if (instance.AddComponent(types[0]) == null) throw new InvalidOperationException("Unity cannot attach the PK Animal script. Configure the DLLs for Editor compatibility first.");
                PrefabUtility.SaveAsPrefabAsset(instance, assetPath);
            }
            finally { DestroyImmediate(instance); }
            AssetDatabase.SaveAssets();
            var destination = Path.GetFullPath("Logs/TyrantProbeBundle");
            Directory.CreateDirectory(destination);
            var bundleName = "environment-probe-" + Guid.NewGuid().ToString("N") + ".bundle";
            var manifest = BuildPipeline.BuildAssetBundles(destination,
                new[] { new AssetBundleBuild { assetBundleName = bundleName, assetNames = new[] { assetPath } } },
                BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode, BuildTarget.StandaloneWindows64);
            if (manifest == null) throw new InvalidOperationException("Windows probe bundle build failed.");
            if (manifest.GetAllDependencies(bundleName).Length != 0) throw new InvalidOperationException("Probe has external dependencies.");
            var bundle = AssetBundle.LoadFromFile(Path.Combine(destination, bundleName));
            if (bundle == null) throw new InvalidOperationException("Cannot load built Windows probe bundle.");
            try
            {
                var loaded = bundle.LoadAsset<GameObject>(assetPath.ToLowerInvariant());
                if (loaded == null || loaded.activeSelf || loaded.GetComponent(types[0]) == null)
                    throw new InvalidOperationException("PK Animal script reference did not survive the bundle roundtrip.");
            }
            finally { bundle.Unload(true); }
            File.WriteAllText("Logs/TyrantEnvironment.txt", "Unity " + EditorVersion + "\nPK component types loaded:\n" +
                string.Join("\n", types.Select(t => t.FullName)) + "\nWindows AssetBundle build/load: PASS\nPK Animal script reference roundtrip: PASS\nThis probe is not a playable animal. Custom animal spawning/save testing is still required.\n");
            Debug.Log("TYRANT_ENVIRONMENT_CHECK_PASS");
        }

        public static void VerifyImportedPrefab()
        {
            var args = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(args, "-tyrantPrefab");
            if (index < 0 || index + 1 >= args.Length) throw new ArgumentException("Pass -tyrantPrefab Assets/...prefab.");
            var path = args[index + 1];
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var problems = Validate(prefab);
            if (problems.Count != 0) throw new InvalidOperationException(string.Join("\n", problems));
            var destination = Path.GetFullPath("Logs/TyrantImportedBundle");
            var result = Build(prefab, destination);
            var report = Directory.GetFiles(destination, "*.bundle.txt").OrderByDescending(File.GetLastWriteTimeUtc).First();
            var bundlePath = report.Substring(0, report.Length - 4);
            var bundle = AssetBundle.LoadFromFile(bundlePath);
            if (bundle == null) throw new InvalidOperationException("Cannot load imported prefab bundle.");
            try
            {
                var loaded = bundle.LoadAsset<GameObject>(path.ToLowerInvariant());
                if (loaded == null || loaded.activeSelf) throw new InvalidOperationException("Imported prefab root did not survive export.");
                foreach (var type in RequiredComponents)
                    if (!loaded.GetComponents<Component>().Any(c => c != null && c.GetType().FullName == type))
                        throw new InvalidOperationException("Imported bundle lost " + type);
                if (loaded.GetComponentsInChildren<Component>(true).Any(c => c == null)) throw new InvalidOperationException("Imported bundle has missing scripts.");
                foreach (var renderer in loaded.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if (renderer.sharedMesh == null || renderer.sharedMaterials.Any(m => m == null) || renderer.bones.Any(b => b == null))
                        throw new InvalidOperationException("Imported bundle lost renderer dependencies.");
                File.WriteAllText("Logs/TyrantImportedPrefab.txt", "Prefab: " + path + "\nStructural checks: PASS\nWindows build/load: PASS\nComponents: "
                    + loaded.GetComponentsInChildren<Component>(true).Length + "\nSkinned renderers: " + loaded.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length
                    + "\n" + result + "\nIn-game spawn and save/reload still require testing.\n");
            }
            finally { bundle.Unload(true); }
            Debug.Log("TYRANT_IMPORTED_PREFAB_CHECK_PASS");
        }

        public static string Build(GameObject animal, string destination)
        {
            var errors = Validate(animal);
            if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors));
            if (string.IsNullOrWhiteSpace(destination)) throw new InvalidOperationException("Choose an export folder first.");
            var fullDestination = Path.GetFullPath(destination);
            var project = Path.GetFullPath(".");
            if (fullDestination.StartsWith(Path.Combine(project, "Assets") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || fullDestination == Path.Combine(project, "Assets"))
                throw new InvalidOperationException("Export outside Assets so Unity does not import its own output.");
            var asset = AssetDatabase.GetAssetPath(animal);
            var bundle = "animal-" + Guid.NewGuid().ToString("N") + ".bundle";
            var staging = Path.Combine(project, "Temp", "TyrantBundles", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            // One explicit bundle includes dependencies instead of relying on user-assigned bundle labels.
            var manifest = BuildPipeline.BuildAssetBundles(staging,
                new[] { new AssetBundleBuild { assetBundleName = bundle, assetNames = new[] { asset } } },
                BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode, BuildTarget.StandaloneWindows64);
            if (manifest == null) throw new InvalidOperationException("Unity bundle build failed. See the Console.");
            if (manifest.GetAllDependencies(bundle).Length != 0) throw new InvalidOperationException("The prefab bundle has external bundle dependencies; export a self-contained prefab.");
            Directory.CreateDirectory(fullDestination);
            File.Copy(Path.Combine(staging, bundle), Path.Combine(fullDestination, bundle), false);
            var report = Path.Combine(fullDestination, bundle + ".txt");
            File.WriteAllText(report, "Unity: " + EditorVersion + "\nTarget: StandaloneWindows64\nBundle: " + bundle + "\nAsset: " + asset.ToLowerInvariant() + "\n\nIn Tyrant, select the species and attach this .bundle with the Asset path above.\nStructural checks passed; spawn and save/reload must be tested in PK.\n");
            return "Built " + Path.Combine(fullDestination, bundle) + "\nAsset: " + asset.ToLowerInvariant();
        }

        public static void VerifyConvertedBundle()
        {
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("PrehistoricKingdom.Animal", false)).FirstOrDefault(t => t != null)
                ?? throw new TypeLoadException("PK Animal is not loaded.");
            var bundle = AssetBundle.LoadFromFile(Path.GetFullPath("Logs/TyrantProbeBundle/reversed-probe.bundle"));
            if (bundle == null) throw new InvalidOperationException("Cannot load the converted diagnostic bundle.");
            try
            {
                var animal = bundle.LoadAsset<GameObject>("assets/tyrantdiagnostics/environmentprobe.prefab");
                if (animal == null || animal.activeSelf || animal.GetComponent(type) == null)
                    throw new InvalidOperationException("Game/authoring assembly conversion damaged the prefab.");
                File.WriteAllText("Logs/TyrantConvertedBundle.txt", "Tyrant bundle repack + assembly conversion + Unity load: PASS\nPK Animal component and inactive root preserved.\nThis is a diagnostic prefab, not a playable animal.\n");
                Debug.Log("TYRANT_CONVERTED_BUNDLE_CHECK_PASS");
            }
            finally { bundle.Unload(true); }
        }
    }
}
