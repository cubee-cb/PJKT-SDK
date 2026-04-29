using System;
using System.IO;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif


namespace PJKT.SDK2
{
    /// <summary>
    /// Builds an asset bundle of the current booth to check the build size.
    /// idk if thats how we wanna do it but it appears to be what vitdeck does
    /// </summary>
    public static class PjktBuildSize
    {
#if UNITY_EDITOR
        //temp for testing
        [MenuItem("PJKT SDK/Tools/Test Build Size")]
        public static void Test()
        {
            //get descriptor from selected object
            if (Selection.activeObject == null)
            {
                Debug.LogError("<color=#FFBB00><b>PJKT SDK:</b></color> Select an object with a booth descriptor");
                return;
            }

            GameObject obj = Selection.activeObject as GameObject;
            BoothDescriptor booth = obj.GetComponent<BoothDescriptor>();

            if (booth == null)
            {
                Debug.LogError("<color=#FFBB00><b>PJKT SDK:</b></color> Selected object does not have a booth descriptor");
                return;
            }

            long size = AssessBuildSize(booth);

            if (size == -1)
            {
                Debug.Log("<color=#FFBB00><b>PJKT SDK:</b></color> Failed to build booth");
                return;
            }

            Debug.Log("<color=#FFBB00><b>PJKT SDK:</b></color> Build size: " + BoothValidator.FormatSize(size));
        }

        //plan is to create a new temporary scene, copy the booth to it and build that scene into an asset bundle
        public static long AssessBuildSize(BoothDescriptor booth)
        {
            //prolly need to do some sort of editor lock or progress bar here
            char[] invalidChars = Path.GetInvalidFileNameChars();
            string cleanCommunityName = string.Join("_", booth.currentCommunity.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries)).TrimEnd('.');

            string tempPath = Path.Combine(Path.GetTempPath(), "PjktSdk");
            string assetBundleName = $"{cleanCommunityName.ToLower()}_buildsizetemp"; // lowercase due to BuildAssetBundles producing only lowercase filenames
            string assetBundlePath = Path.Combine(tempPath, assetBundleName);

            string prefabBuildPath = Path.Combine("Assets", "PjktTemp");
            string prefabPath = Path.Combine(prefabBuildPath, assetBundleName + ".prefab");

            AssetBundleManifest manifest = null;

            try
            {
                if (!Directory.Exists(prefabBuildPath)) Directory.CreateDirectory(prefabBuildPath);
                if (!Directory.Exists(tempPath)) Directory.CreateDirectory(tempPath);

                //make a copy before anything
                GameObject boothClone = GameObject.Instantiate(booth.gameObject);

                //if the booth clone has any material swappers remove them
                PjktMaterialSwapper[] swappers = boothClone.GetComponentsInChildren<PjktMaterialSwapper>(true);
                foreach (PjktMaterialSwapper swap in swappers)
                {
                    GameObject.DestroyImmediate(swap);
                }

                PrefabUtility.SaveAsPrefabAsset(boothClone, prefabPath);
                GameObject.DestroyImmediate(boothClone);

                AssetBundleBuild build = new AssetBundleBuild
                {
                    assetBundleName = assetBundleName,
                    assetNames = new[] { prefabPath }
                };

                manifest = BuildPipeline.BuildAssetBundles(tempPath, new[] { build }, BuildAssetBundleOptions.None, BuildTarget.StandaloneWindows64); //figure out android later
            }
            catch (Exception e)
            {
                throw e;
            }

            if (manifest == null) return -1;

            if (!File.Exists(assetBundlePath)) return -1;

            FileInfo fileInfo = new FileInfo(assetBundlePath);
            long builtSize = fileInfo.Length;

            //get rid of temp prefab
            if (File.Exists(prefabPath)) File.Delete(prefabPath);
            if (File.Exists(prefabPath + ".meta")) File.Delete(prefabPath + ".meta");

            AssetDatabase.Refresh();
            return builtSize;
        }
#endif
    }
}