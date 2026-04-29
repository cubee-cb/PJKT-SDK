using System;
using System.IO;
using System.IO.Compression;
using UnityEngine;

#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using PJKT.SDK2.NET;
using UnityEditor;
using VRC.Udon.Common;
#endif

namespace PJKT.SDK2
{
    public class PjktFileExporter //: IDisposable
    {
#if UNITY_EDITOR

        //[MenuItem("PJKT/TestExporter")]
        public static void Test()
        {
            if (Selection.activeObject is GameObject)
            {
                GameObject booth = Selection.activeObject as GameObject;
                PjktFileExporter exporter = new PjktFileExporter("test");
                exporter.CreateBoothfile(booth);
            }
        }

        public readonly string PrefabDirectory;
        public readonly string TempDirectory;
        public readonly string CommunityName;
        public readonly string ExportPath;

        public PjktFileExporter(string communityName, string exportPath = "")
        {
            //sanitise community name. disallow < > : " / \ | ? *
            char[] invalidChars = Path.GetInvalidFileNameChars();
            string cleanName = string.Join("_", communityName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries)).TrimEnd('.');

            CommunityName = cleanName;
            TempDirectory = Path.Combine(Path.GetTempPath(), "PjktSdk", CommunityName);
            ExportPath = exportPath;
            PrefabDirectory = Path.Combine("Assets", "PjktTemp");
        }

        public string CreateBoothfile(GameObject booth)
        {
            if (booth == null)
            {
                Debug.LogError("Cannot create booth file from null object");
                return string.Empty;
            }

            //create prefab of the booth first
            string boothName = CommunityName + "_" + PjktEventManager.SelectedProjekt.name;
            string prefabPath = Path.Combine(PrefabDirectory, boothName + ".prefab");

            //check if the directory exists
            if (!Directory.Exists(PrefabDirectory))
            {
                Directory.CreateDirectory(PrefabDirectory);
            }

            //create temp directories for the booth files
            CreateTempfolders();

            //using a duplicate of the booth so we can zero out the xz pos and unlink any other prefabs
            GameObject tempBooth = GameObject.Instantiate(booth, new Vector3(0, booth.transform.position.y, 0), booth.transform.rotation);
            FindAndUnpackPrefabInstances(tempBooth);

            //create the prefab
            PrefabUtility.SaveAsPrefabAsset(tempBooth, prefabPath);

            //get all dependedncies of the prefab
            string[] dependencies = AssetDatabase.GetDependencies(prefabPath);

            //sorts duplicated of the files into temp appdata folder
            if (!SortFiles(dependencies))
            {
                PjktSdkWindow.Notify("Booth upload canceled", BoothErrorType.Warning);
                //cleanup the prefab
                GameObject.DestroyImmediate(tempBooth);
                if (File.Exists(prefabPath)) File.Delete(prefabPath);
                if (File.Exists(prefabPath + ".meta")) File.Delete(prefabPath + ".meta");
                return string.Empty;
            }

            //do community and booth info json here
            CommunityInfo communityInfo = new CommunityInfo();
            communityInfo.Id = Authentication.ActiveUser.GetCommunityId(CommunityName);
            communityInfo.CommunityName = CommunityName;
            communityInfo.CommunityDescription = ""; //cant get this yet. waiting on backend
            communityInfo.LogoUrl = ""; //cant get this yet. waiting on backend
            communityInfo.GroupID = booth.GetComponent<BoothDescriptor>().GroupID;

            SdkBoothInfo sdkBoothInfo = new SdkBoothInfo();
            sdkBoothInfo.BoothPrefabName = booth.name;
            List<string> boothStats = new List<string>();
            foreach (BoothStats stat in BoothValidator.Report.Stats)
            {
                boothStats.Add(stat.ToString());
            }
            sdkBoothInfo.BoothStats = boothStats.ToArray();

            BoothMetadata metadata = new BoothMetadata();
            metadata.boothType = BoothType.SdkBooth;
            metadata.communityInfo = communityInfo;
            metadata.sdkBoothInfo = sdkBoothInfo;
            metadata.EventName = PjktEventManager.SelectedProjekt.name;
            metadata.BoothUploadDate = DateTime.Now;
            metadata.BoothUploaderUsername = Authentication.ActiveUser.user.username;

            string json = JsonUtility.ToJson(metadata, true);
            File.WriteAllText(Path.Combine(TempDirectory, $"boothInfo {CommunityName} - {metadata.EventName}.json"), json);

            //now zip it up
            string zipPath = string.IsNullOrEmpty(ExportPath) ? Path.Combine(PrefabDirectory, boothName + ".zip") : ExportPath;
            if (File.Exists(zipPath)) File.Delete(zipPath);
            ZipFile.CreateFromDirectory(TempDirectory, zipPath);

            //cleanup the prefab
            GameObject.DestroyImmediate(tempBooth);

            if (File.Exists(prefabPath)) File.Delete(prefabPath);
            if (File.Exists(prefabPath + ".meta")) File.Delete(prefabPath + ".meta");

            if (File.Exists(zipPath)) return zipPath;
            return string.Empty;
        }

        private void FindAndUnpackPrefabInstances(GameObject booth)
        {
            foreach (Transform child in booth.transform)
            {
                //check the booth itself
                if (PrefabUtility.IsPartOfPrefabInstance(booth))
                {
                    PrefabUtility.UnpackPrefabInstance(booth, PrefabUnpackMode.Completely, InteractionMode.UserAction);
                }

                //check child objects
                if (PrefabUtility.IsPartOfPrefabInstance(child.gameObject))
                {
                    PrefabUtility.UnpackPrefabInstance(child.gameObject, PrefabUnpackMode.Completely, InteractionMode.UserAction);
                }
            }
        }

        // creates directory structure in the temporary folder
        // Windows: %LocalAppdata%\Temp\PjktSdk\CommunityName
        // Linux: /tmp/PjktSdk/CommunityName
        private void CreateTempfolders()
        {
            if (!Directory.Exists(TempDirectory))
            {
                Directory.CreateDirectory(TempDirectory);
            }
            else
            {
                //delete existing files
                DirectoryInfo di = new DirectoryInfo(TempDirectory);
                foreach (FileInfo file in di.GetFiles()) file.Delete();
            }

            //folders for textures, materials, models, etc
            CreateOrClearFolder("Textures");
            CreateOrClearFolder("Materials");
            CreateOrClearFolder("Models");
            CreateOrClearFolder("Animations");
            CreateOrClearFolder("Audio");
            CreateOrClearFolder("Shaders");
            CreateOrClearFolder("OtherFiles");
        }

        // ensures the target folders exist and are empty
        private void CreateOrClearFolder(string fileType)
        {
            // if directory already exists then delete files in it
            if (!Directory.Exists(Path.Combine(TempDirectory, fileType)))
            {
                Directory.CreateDirectory(Path.Combine(TempDirectory, fileType));
            }
            else
            {
                DirectoryInfo di = new DirectoryInfo(Path.Combine(TempDirectory, fileType));
                foreach (FileInfo file in di.GetFiles()) file.Delete();
            }
        }

        // sorts the files into correct folders in the temp appdata folder
        private bool SortFiles(string[] files)
        {
            //chat is dir real?
            if (!Directory.Exists(TempDirectory)) throw new Exception("Temp Directory does not exist");

            //for each file in the dependencies, get the file type and make a duplicate in the temp folder
            foreach (string file in files)
            {
                //if the file path starts with packages ignore it
                if (file.StartsWith("Packages")) continue;

                //if file is a script then skip it
                if (file.EndsWith(".cs") || file.EndsWith(".cs.meta")) continue;

                //exclude udon program assets
                if (file.EndsWith(".asset"))
                {
                    //if the yaml contains the phrase serializedUdonProgramAsset then skip it
                    string yaml = File.ReadAllText(file);
                    if (yaml.Contains("serializedUdonProgramAsset")) continue;
                }

                string fileType = GetFileType(file);
                string fileName = Path.GetFileName(file);
                string newFilePath = Path.Combine(TempDirectory, fileType, fileName);

                //grab its .meta file as well
                string metaFile = file + ".meta";

                //copy the file to the new location

                //rename if duplicate name
                if (File.Exists(newFilePath))
                {
                    //auto rename shaders because poiyomi is being difficult
                    if (Path.GetExtension(newFilePath) != ".shader")
                    {
                        //for eveything else warn them
                        string message = $"File exist with the same name, this will cause conflicts and may break your booth. You should rename one of the files.\n" +
                                         $"File: {newFilePath} \n" +
                                         $"File: {file} \n" +
                                         $"Do you want to continue anyways?";
                        if (!EditorUtility.DisplayDialog("Duplicate File", message, "Yolo", "Cancel"))
                        {
                            Debug.LogWarning($"<color=#FFBB00><b>PJKT SDK:</b></color> Duplicate Files: \n {newFilePath} \n {file}");
                            return false;
                        }
                    }

                    string newFilename = Path.GetFileNameWithoutExtension(newFilePath) + $"_{Guid.NewGuid()}" + Path.GetExtension(newFilePath);
                    newFilePath = $"{TempDirectory}\\{fileType}\\{newFilename}";
                }

                File.Copy(file, newFilePath);
                File.Copy(metaFile, newFilePath + ".meta");
            }

            return true;
        }

        // figures out what folder the file is supposed to go in
        private string GetFileType(string file)
        {
            //sort by file extensions, type causes too many issues
            string extension = Path.GetExtension(file);

            switch (extension)
            {
                //textures
                case ".png":
                    return "Textures";
                case ".jpg":
                    return "Textures";
                case ".exr":
                    return "Textures";
                case ".tif":
                    return "Textures";

                //animations
                case ".anim":
                    return "Animations";
                case ".controller":
                    return "Animations";

                //audio
                case ".wav":
                    return "Audio";
                case ".mp3":
                    return "Audio";
                case ".flac":
                    return "Audio";

                //materials
                case ".mat":
                    return "Materials";

                //shaders
                case ".shader":
                    return "Shaders";

                //models
                case ".fbx":
                    return "Models";
                case ".obj":
                    return "Models";

                //prefabs
                case ".prefab":
                    return "";

                //everything else
                default:
                    return "OtherFiles";
            }
        }

        public void Dispose()
        {
            //cleanup temp files
            if (Directory.Exists(TempDirectory))
            {
                Directory.Delete(TempDirectory, true);
            }

            if (Directory.Exists(PrefabDirectory))
            {
                Directory.Delete(PrefabDirectory, true);
            }
        }
#endif
    }
}