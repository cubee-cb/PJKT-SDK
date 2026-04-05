using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using VRC.SDK3.Components;
using VRC.Udon;

namespace PJKT.SDK2
{
    internal class BoothValidator
    {
        public static BoothValidationReport Report = new BoothValidationReport();        
        public static BoothRequirements Requirements;
        public static BoothDescriptor[] BoothsInScene = new BoothDescriptor[0];
        public static BoothDescriptor SelectedBooth;

        public static void GetBoothsInScene()
        {
            BoothsInScene = GameObject.FindObjectsOfType<BoothDescriptor>();
            Debug.Log("<color=#FFBB00><b>PJKT SDK:</b></color> Found " + BoothsInScene.Length + " booths in scene");
        }

        public static void GenerateReport()
        {
            if (SelectedBooth == null)
            {
                //try getting the first one?
                if (BoothsInScene.Length > 0) GenerateReport(BoothsInScene[0]);
                else return;
            }
            else GenerateReport(SelectedBooth);
        }
        public static void GenerateReport(BoothDescriptor booth)
        {
            SelectedBooth = booth;
            Report = new BoothValidationReport();
            
            if (booth == null) return;
            Report.Booth = booth;

            if (Requirements == null)
            {
                Requirements = new BoothRequirements();
                PjktSdkWindow.Notify("No booth requirements found. Have you selected an event?", BoothErrorType.Warning);
            }

            //Debug.Log("<color=#FFBB00><b>PJKT SDK:</b></color> Generating booth report...");
            // fetch all the relevant components
            List<Animator> Animators = booth.gameObject.GetComponentsInChildren<Animator>(true).ToList();
            List<MeshFilter> meshFilters = booth.gameObject.GetComponentsInChildren<MeshFilter>(true).ToList();
            List<SkinnedMeshRenderer> SkinnedMeshes = booth.gameObject.GetComponentsInChildren<SkinnedMeshRenderer>(true).ToList();
            List<ParticleSystem> Particles = booth.gameObject.GetComponentsInChildren<ParticleSystem>(true).ToList();
            List<ParticleSystemRenderer> ParticleRenderers = booth.gameObject.GetComponentsInChildren<ParticleSystemRenderer>(true).ToList();
            List<VRCPickup> Pickups = booth.gameObject.GetComponentsInChildren<VRCPickup>(true).ToList();
            List<VRCPortalMarker> Portals = booth.gameObject.GetComponentsInChildren<VRCPortalMarker>(true).ToList();
            List<TMP_Text> TMProTexts = booth.gameObject.GetComponentsInChildren<TMP_Text>(true).ToList(); 
            List<UdonBehaviour> UdonBehaviours = booth.gameObject.GetComponentsInChildren<UdonBehaviour>(true).ToList();
            List<Renderer> Renderers = booth.gameObject.GetComponentsInChildren<Renderer>(true).ToList();
            List<VRCAvatarPedestal> AvatarPeds = booth.gameObject.GetComponentsInChildren<VRCAvatarPedestal>(true).ToList();
            
            /*//debugging 
            Debug.Log("Booth: " + booth.boothName);
            Debug.Log("Animators: " + Animators.Count);
            Debug.Log("MeshFilters: " + meshFilters.Count);
            Debug.Log("SkinnedMeshes: " + SkinnedMeshes.Count);
            Debug.Log("Particles: " + Particles.Count);
            Debug.Log("Pickups: " + Pickups.Count);
            Debug.Log("Portals: " + Portals.Count);
            Debug.Log("TMProTexts: " + TMProTexts.Count);
            Debug.Log("UdonBehaviours: " + UdonBehaviours.Count);
            Debug.Log("Renderers: " + Renderers.Count);
            Debug.Log("AvatarPeds: " + AvatarPeds.Count);*/

            //get the meshes
            List<Mesh> meshes = new List<Mesh>();
            foreach (MeshFilter filer in meshFilters) if (filer.sharedMesh != null && !meshes.Contains(filer.sharedMesh)) meshes.Add(filer.sharedMesh);
            foreach (SkinnedMeshRenderer renderer in SkinnedMeshes) if (renderer.sharedMesh != null && !meshes.Contains(renderer.sharedMesh)) meshes.Add(renderer.sharedMesh);
            foreach (ParticleSystemRenderer renderer in ParticleRenderers) if (renderer.mesh != null && !meshes.Contains(renderer.mesh)) meshes.Add(renderer.mesh);


            //Generate the stats for each requirement. THESE ARE IN A SPECIFIC ORDER!
            Report.Stats.Add(GetBounds(Renderers));
            Report.Stats.Add(GetTriangles(meshes));
            Report.Stats.Add(GetStaticMeshes(meshFilters));
            Report.Stats.Add(GetSkinnedMeshes(SkinnedMeshes));
            Report.Stats.Add(GetParticleMeshes(Particles));
            Report.Stats.Add(GetMaterials(Renderers));
            Report.Stats.Add(GetTextures(Report.GetStats(StatsType.Materials).ComponentList.Cast<Material>().ToList()));
            Report.Stats.Add(GetPickups(Pickups));
            Report.Stats.Add(GetAnimators(Animators));
            Report.Stats.Add(GetAnimationClips(Report.GetStats(StatsType.Animators).ComponentList.Cast<AnimatorInfo>().ToList()));
            Report.Stats.Add(GetTextMeshPro(TMProTexts));
            Report.Stats.Add(GetParticles(Particles));
            Report.Stats.Add(GetUdonBehaviours(UdonBehaviours));
            Report.Stats.Add(GetAvatarPedestals(AvatarPeds));
            Report.Stats.Add(GetPortals(Portals));
            
            List<TextureInfo> textureInfos = Report.GetStats(StatsType.Textures).ComponentList.Cast<TextureInfo>().ToList();
            List<MeshAsset> meshAssets = Report.GetStats(StatsType.Mesh).ComponentList.Cast<MeshAsset>().ToList();
            Report.Stats.Add(GetVram(textureInfos, meshAssets));
            Report.Stats.Add(GetFileSize());
            
            //new invalid object, just directional lights and reflection probes for now
            Report.Stats.Add(GetInvalidObjects());

            Report.Overallranking = OverallRank(); //done :3
            
            //sike we got issues
            FindSharedMeshFix.FixMeshInstances();
        }

        private static BoothPerformanceRanking OverallRank()
        {
            //not sure of a better way to do this but eeh
            BoothPerformanceRanking overallrank = BoothPerformanceRanking.NotApplicable;
            foreach (BoothStats stats in Report.Stats)
            {
                if (stats.PerformanceRank < overallrank) overallrank = stats.PerformanceRank;
            }

            return overallrank;
        }
        
        private static BoothStats GetBounds(List<Renderer> renderers)
        { 
            //Override the max dims with the margin
            Vector3 maxDims = new Vector3(Requirements.MaxDims[0], Requirements.MaxDims[1], Requirements.MaxDims[2]);
            Vector3 dims = maxDims + (Vector3.one * Requirements.MaxDimsMargin);
            
            string reqs = $"Max Bounds: {maxDims.ToString()}";
            
            //sanity check
            if (renderers.Count == 0)
            {
                return new BoothStats(StatsType.Bounds, BoothPerformanceRanking.NotApplicable, "Bounds: No renderers found", reqs, new List<object>());
            }

            BoothPerformanceRanking ranking = BoothPerformanceRanking.Error;
            
            //I have lost all faith in Bounds.Encapsulate
            Vector3 min = renderers[0].bounds.min;
            Vector3 max = renderers[0].bounds.max;

            //Get bounds of all renderers
            foreach (Renderer renderer in renderers) 
            {
                Bounds bounds = renderer.bounds;
                min = Vector3.Min(min, bounds.min);
                max = Vector3.Max(max, bounds.max);
            }

            Vector3 size = max - min;

            //Round up to 1 decimal place
            size.x = Mathf.Ceil(size.x * 10) / 10;
            size.y = Mathf.Ceil(size.y * 10) / 10;
            size.z = Mathf.Ceil(size.z * 10) / 10;
            
            //bool pass;
            if (size.x > dims.x || size.y > dims.y || size.z > dims.z) ranking = BoothPerformanceRanking.Bad;
            else ranking = BoothPerformanceRanking.Good;

            //Return size to 1 decimal place
            string boundsString = "Bounds: " + size.x.ToString("0.0") + " x " + size.y.ToString("0.0") + " x " + size.z.ToString("0.0") + "\n max " + maxDims.ToString("0.0");
            
            return  new BoothStats(StatsType.Bounds, ranking, boundsString, reqs,  new List<object>(renderers));
        }
        private static BoothStats GetTriangles(List<Mesh> meshes)
        {
            long triangles = 0;
            bool meshReadWriteDisabled = false;
            foreach (Mesh mesh in meshes)
            {
                if (!mesh.isReadable)
                {
                    meshReadWriteDisabled = true;
                    continue;
                }
                triangles += mesh.triangles.Length / 3;
            }

            BoothPerformanceRanking ranking = triangles == Requirements.MaxTriangles ? BoothPerformanceRanking.Ok : triangles > Requirements.MaxTriangles ? BoothPerformanceRanking.Bad : BoothPerformanceRanking.Good;
            string detailsString = "Total Triangles: " + triangles + "/" + Requirements.MaxTriangles;
            if (meshReadWriteDisabled)
            {
                ranking = BoothPerformanceRanking.Error;
                detailsString += " (Some meshes have Read/Write disabled)";
            }
            
            
            return new BoothStats(StatsType.TriCount, ranking, detailsString, $"Max Triangles: {Requirements.MaxTriangles}", new List<object>(meshes));
        }
        private static BoothStats GetStaticMeshes(List<MeshFilter> staticMeshes)
        {
            Texture icon = AssetPreview.GetMiniTypeThumbnail(typeof(MeshFilter));
            List<PerformanceTip> tips = new List<PerformanceTip>();
            
            //create a MeshAsset for each one
            List<MeshAsset> meshAssets = new List<MeshAsset>();
            foreach (MeshFilter filter in staticMeshes)
            {
                if (filter.sharedMesh == null) continue;
                
                MeshAsset asset = GetAssetForMesh(filter.sharedMesh, MeshType.StaticMesh);
                asset.Icon = icon;
                asset.ObjectReference = filter.gameObject;
                meshAssets.Add(asset);
            }
            
            //check and warn for probuilder
            if (PjktPackageChecker.PackageSources.ContainsKey("com.unity.probuilder"))
            {
                tips.Add(new PerformanceTip("Probuilder detected in project. If you are using it, make sure to export instanced meshes to files before uploading.", BoothErrorType.Warning));
            }
            
            BoothPerformanceRanking ranking = staticMeshes.Count == Requirements.MaxStaticMeshes ? BoothPerformanceRanking.Ok : staticMeshes.Count > Requirements.MaxStaticMeshes ? BoothPerformanceRanking.Bad : BoothPerformanceRanking.Good;
            return new BoothStats(StatsType.Mesh, ranking, "Static Meshes: " + staticMeshes.Count + "/" + Requirements.MaxStaticMeshes, $"Max Static Meshes: {Requirements.MaxStaticMeshes}", new List<object>(meshAssets), tips);
        }
        private static BoothStats GetSkinnedMeshes(List<SkinnedMeshRenderer> skinnedMeshes)
        {
            Texture icon = AssetPreview.GetMiniTypeThumbnail(typeof(SkinnedMeshRenderer));
            
            //create a MeshAsset for each one
            List<MeshAsset> meshAssets = new List<MeshAsset>();
            foreach (SkinnedMeshRenderer renderer in skinnedMeshes)
            {
                MeshAsset asset = GetAssetForMesh(renderer.sharedMesh, MeshType.SkinnedMesh);
                asset.Icon = icon;
                asset.ObjectReference = renderer.gameObject;
                meshAssets.Add(asset);
            }
            
            BoothPerformanceRanking ranking = skinnedMeshes.Count() == Requirements.MaxSkinnedMeshRenderers ? BoothPerformanceRanking.Ok : skinnedMeshes.Count() > Requirements.MaxSkinnedMeshRenderers ? BoothPerformanceRanking.Bad : BoothPerformanceRanking.Good;
            return new BoothStats(StatsType.SkinnedMesh, ranking, "Skinned Meshes: " + skinnedMeshes.Count() + "/" + Requirements.MaxSkinnedMeshRenderers, $"Max Skinned Meshes: {Requirements.MaxSkinnedMeshRenderers}", new List<object>(meshAssets));
        }
        private static BoothStats GetParticleMeshes(List<ParticleSystem> particles)
        {
            Texture icon = AssetPreview.GetMiniTypeThumbnail(typeof(ParticleSystem));
            
            //create a MeshAsset for each one
            List<MeshAsset> meshAssets = new List<MeshAsset>();
            foreach (ParticleSystem particle in particles)
            {
                ParticleSystemRenderer renderer = particle.GetComponent<ParticleSystemRenderer>();
                if (renderer.renderMode != ParticleSystemRenderMode.Mesh) continue;
                if (renderer.mesh == null) continue;

                MeshAsset asset = GetAssetForMesh(renderer.mesh, MeshType.ParticleMesh);
                asset.Icon = icon;
                asset.ObjectReference = particle.gameObject;
                meshAssets.Add(asset);
            }
            
            return new BoothStats(StatsType.ParticleMesh, BoothPerformanceRanking.NotApplicable, "Particle Meshes: " + meshAssets.Count + "/" + Requirements.MaxParticles, "Particle meshes count as Static Meshes", new List<object>(meshAssets));
        }
        private static BoothStats GetVram(List<TextureInfo> textureInfos, List<MeshAsset> meshAssets)
        {
            long totalVram = 0;
            
            long maxVram = Requirements.MaxVram * 1024 * 1024;
            
            foreach (TextureInfo info in textureInfos) totalVram += info.vRamSize;
            foreach (MeshAsset asset in meshAssets) totalVram += asset.VramSize;
            BoothPerformanceRanking ranking = totalVram == maxVram ? BoothPerformanceRanking.Ok : totalVram > maxVram ? BoothPerformanceRanking.Bad : BoothPerformanceRanking.Good;
            
            return new BoothStats(StatsType.Vram, ranking, "VRam: " + FormatSize(totalVram) + "/" + FormatSize(maxVram), $"Max Vram: {maxVram}", new List<object>());
        }
        private static BoothStats GetMaterials(List<Renderer> renderers)
        {
            List<PerformanceTip> tips = new List<PerformanceTip>();
            HashSet<Material> materials = new HashSet<Material>();
            foreach (Renderer renderer in renderers)
            {
                materials.UnionWith(renderer.sharedMaterials);
            }
            
            //check for non vrc shaders
            foreach (var mat in materials)
            {
                if (mat == null) continue;
                if (!mat.shader.name.StartsWith("VRChat/"))
                {
                    tips.Add(new PerformanceTip("Custom shaders found. If you arent doing something special, please try to use the built in VRChat shaders for the best performance.", BoothErrorType.Info));
                    break;
                }
            }
            
            BoothPerformanceRanking ranking = materials.Count == Requirements.MaxMaterial ? BoothPerformanceRanking.Ok : materials.Count > Requirements.MaxMaterial ? BoothPerformanceRanking.Bad : BoothPerformanceRanking.Good;
            return new BoothStats(StatsType.Materials, ranking, "Materials: " + materials.Count + "/" + Requirements.MaxMaterial, $"Max Materials: {Requirements.MaxMaterial}", new List<object>(materials), tips);
        }
        private static BoothStats GetTextures(List<Material> materials)
        {
            List<TextureInfo> textureInfos = new List<TextureInfo>();
            List<Texture> textures = new List<Texture>();
            string buildTarget = EditorUserBuildSettings.activeBuildTarget.ToString();

            //get all textures from materials
            foreach (var material in materials)
            {
                int[] textureIDs = material.GetTexturePropertyNameIDs();
                foreach (var textureID in textureIDs)
                {
                    Texture texture = material.GetTexture(textureID);
                    if (texture is RenderTexture) continue;
                    
                    string path = AssetDatabase.GetAssetPath(texture);
                    if (path == null || path.Contains(".asset")) continue;
                    
                    if (texture == null) continue;
                    if (!textures.Contains(texture)) textures.Add(texture);
                    else
                    {
                        //we have seen this texture before, add this material to its textureinfo
                        foreach (var info in textureInfos)
                        {
                            if (info.texture == texture)
                            {
                                if (info.materials.Contains(material)) break;
                                info.materials.Add(material);
                                break;
                            }
                        }
                        continue;
                    }
                    
                    //get resolution from the actual texture file before import
                    if (path.Contains("unity_builtin_extra") || path.Contains("unity default resources"))
                    {
                        var defaultResourceInfo = new TextureInfo
                        {
                            texture = texture,
                            name = texture.name.Length <= 20 ? texture.name : texture.name.Substring(0, 20) + "...",
                            filetype = "",
                            pixelSize = new int[2],
                            importedSize = 0,
                            vRamSize = Profiler.GetRuntimeMemorySizeLong(texture),
                            importer = null,
                            materials = new List<Material>()
                            {
                                material
                            }
                        };
                        textureInfos.Add(defaultResourceInfo);
                        continue;
                    }
                    
                    TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                    TextureImporterPlatformSettings platformSettings = importer.GetPlatformTextureSettings(buildTarget);
                    TextureImporterFormat format = platformSettings.format;
                    
                    if (format == TextureImporterFormat.Automatic)
                    {
                        format = importer.GetAutomaticFormat(buildTarget);
                    }

                    //if there are windows overrides report that overriden value
                    int importedSize = importer.maxTextureSize; //default
                    if (platformSettings.overridden)
                    {
                        importedSize = platformSettings.maxTextureSize;
                    }

                    var textureInfo = new TextureInfo
                    {
                        texture = texture,
                        name = texture.name.Length <= 20 ? texture.name : texture.name.Substring(0, 20) + "...",
                        filetype = path.Substring(path.LastIndexOf('.')),
                        pixelSize = GetOriginalTextureSize(importer),
                        importedSize = importedSize,
                        vRamSize = CalculateTextureVram(texture, format),
                        sizeOnDisk = new FileInfo(path).Length,
                        importer = importer,
                        materials = new List<Material>()
                        {
                            material
                        }
                    };
                    textureInfos.Add(textureInfo);
                }
            }
            
            return new BoothStats(StatsType.Textures, BoothPerformanceRanking.NotApplicable, "Textures: " + textures.Count , "No limit on Textures", new List<object>(textureInfos));
        }

        private static BoothStats GetPickups(List<VRCPickup> pickups)
        {
            BoothPerformanceRanking ranking = pickups.Count == Requirements.MaxPickups ? BoothPerformanceRanking.Ok : pickups.Count > Requirements.MaxPickups ? BoothPerformanceRanking.Bad : BoothPerformanceRanking.Good;
            return new BoothStats(StatsType.Pickups, ranking, "Pickups: " + pickups.Count + "/" + Requirements.MaxPickups, $"Max Pickups: {Requirements.MaxPickups}", new List<object>(pickups));
        }
        private static BoothStats GetAnimators(List<Animator> animators)
        {
            List<AnimatorInfo> animatorInfos = new List<AnimatorInfo>();
            foreach (Animator animator in animators)
            {
                AnimatorInfo info = new AnimatorInfo();
 
                
                info.Animator = animator;
                AnimatorController controller = animator.runtimeAnimatorController as AnimatorController;
                
                if (controller == null)
                {
                    info.Controller = null;
                    info.Clips = new List<AnimationClip>();
                    info.Layers = 0;
                    info.States = 0;
                    info.AnyStateTransitions = 0;
                    info.Parameters = new List<Tuple<string, AnimatorControllerParameterType>>();
                    animatorInfos.Add(info);
                    continue;
                }
                info.Controller = controller;

                foreach (AnimatorControllerLayer controllerLayer in controller.layers)
                {
                    info.Layers++;
                    info.States += controllerLayer.stateMachine.states.Length;
                    info.AnyStateTransitions += controllerLayer.stateMachine.anyStateTransitions.Length;
                }

                info.Clips = new List<AnimationClip>();
                foreach (AnimationClip clip in controller.animationClips) if (!info.Clips.Contains(clip)) info.Clips.Add(clip);
                info.Parameters = new List<Tuple<string, AnimatorControllerParameterType>>();
                foreach (AnimatorControllerParameter parameter in controller.parameters) info.Parameters.Add(new Tuple<string, AnimatorControllerParameterType>(parameter.name, parameter.type));

                animatorInfos.Add(info);
            }

            BoothPerformanceRanking ranking = animators.Count == Requirements.MaxAnimators ? BoothPerformanceRanking.Ok : animators.Count > Requirements.MaxAnimators ? BoothPerformanceRanking.Bad : BoothPerformanceRanking.Good;
            return new BoothStats(StatsType.Animators, ranking, "Animators: " + animators.Count + "/" + Requirements.MaxAnimators, $"Max Animators: {Requirements.MaxAnimators}", new List<object>(animatorInfos));
        }
        private static BoothStats GetAnimationClips(List<AnimatorInfo> animators)
        {
            if (animators.Count == 0) return new BoothStats(StatsType.AnimationClips, BoothPerformanceRanking.NotApplicable, "Animation Clips: No animators found", $"Max Animations: {Requirements.MaxAnimations}", new List<object>());

            //get all animation clips
            HashSet<AnimationClip> animations = new HashSet<AnimationClip>();
            foreach (AnimatorInfo info in animators)
            {
                if (info.Controller == null) continue;
                animations.UnionWith(info.Clips);
            }

            BoothPerformanceRanking ranking = animations.Count == Requirements.MaxAnimations ? BoothPerformanceRanking.Ok : animations.Count > Requirements.MaxAnimations ? BoothPerformanceRanking.Bad : BoothPerformanceRanking.Good;
            return new BoothStats(StatsType.AnimationClips, ranking, "Animation Clips: " + animations.Count + "/" + Requirements.MaxAnimations,$"Max Animations: {Requirements.MaxAnimations}", new List<object>(animations));
        }
        private static BoothStats GetTextMeshPro(List<TMP_Text> textMeshes)
        {
            //warnign about font
            List<PerformanceTip> tips = new List<PerformanceTip>();
            if (textMeshes.Count > 0)
            {
                tips.Add(new PerformanceTip("Custom fonts on TMP might be overriden by our font.", BoothErrorType.Info));
            }
            
            BoothPerformanceRanking ranking = textMeshes.Count == Requirements.MaxTextMeshPro ? BoothPerformanceRanking.Ok : textMeshes.Count > Requirements.MaxTextMeshPro ? BoothPerformanceRanking.Bad : BoothPerformanceRanking.Good;
            return new BoothStats(StatsType.TMProTexts, ranking, "Text Mesh Pro: " + textMeshes.Count + "/" + Requirements.MaxTextMeshPro, $"Max TMP: {Requirements.MaxTextMeshPro}", new List<object>(textMeshes), tips);
        }
        private static BoothStats GetParticles(List<ParticleSystem> particles)
        {
            int totalParticles = 0;
            foreach (ParticleSystem particle in particles)
            {
                totalParticles += particle.main.maxParticles;
            }
            
            BoothPerformanceRanking ranking = totalParticles == Requirements.MaxParticles ? BoothPerformanceRanking.Ok : totalParticles > Requirements.MaxParticles ? BoothPerformanceRanking.Bad : BoothPerformanceRanking.Good;
            return new BoothStats(StatsType.ParticleSystem, ranking, "Particles: " + totalParticles + "/" + Requirements.MaxParticles, $"Max Particles: {Requirements.MaxParticles}", new List<object>(particles));
        }
        private static BoothStats GetUdonBehaviours(List<UdonBehaviour> behaviours)
        {
            List<UdonInfo> behaviourinfos = new List<UdonInfo>();
            bool disallowedScripts = false;
            foreach (UdonBehaviour behaviour in behaviours)
            {
                if (behaviour.programSource == null)
                {
                    behaviourinfos.Add(new UdonInfo()
                    {
                        Behaviour = behaviour,
                        Allowed = false,
                        ProgramSource = "No program source found",
                        SyncType = behaviour.SyncMethod,
                    });
                    //disallowedScripts = true;
                    continue;
                }

                string assetpath = AssetDatabase.GetAssetPath(behaviour.programSource);
                bool allowed = false;
                
                //check if its an allowed script from VRChat
                string[] vrcAssets = new string[]
                {
                    "Packages/com.vrchat.worlds/Samples/UdonExampleScene/UdonProgramSources/AvatarPedestal Program.asset",
                    "Packages/com.vrchat.worlds/Samples/UdonExampleScene/UdonProgramSources/DownloadString.asset",
                    "Packages/com.vrchat.worlds/Samples/UdonExampleScene/Prefabs/VRCChair/StationGraph.asset"
                };

                List<string> allowedAssetPaths = new List<string>(Requirements.UdonWhitelist);

                allowedAssetPaths.AddRange(vrcAssets);

                foreach (string path in allowedAssetPaths)
                {
                    if (assetpath == path)
                    {
                        allowed = true;
                        break;
                    }
                }
                
                //if (!allowed) disallowedScripts = true;
                behaviourinfos.Add(new UdonInfo()
                {
                    Behaviour = behaviour,
                    Allowed = allowed,
                    ProgramSource = assetpath,
                    SyncType = behaviour.SyncMethod,
                });
            }

            BoothPerformanceRanking ranking;
            string detailsString = "Udon Behaviours: " + behaviours.Count + "/" + Requirements.MaxUdonScripts;
            
            if (disallowedScripts)
            {
                detailsString += "(Some scripts are not allowed)";
                ranking = BoothPerformanceRanking.Error;
            }
            else ranking = behaviours.Count == Requirements.MaxUdonScripts ? BoothPerformanceRanking.Ok : behaviours.Count > Requirements.MaxUdonScripts ? BoothPerformanceRanking.Bad : BoothPerformanceRanking.Good;
            
            return new BoothStats(StatsType.UdonBehaviours, ranking, detailsString, $"Max Udon Behaviours: {Requirements.MaxUdonScripts}", new List<object>(behaviourinfos));
        }
        private static BoothStats GetAvatarPedestals(List<VRCAvatarPedestal> avatarPeds)
        {
            BoothPerformanceRanking ranking = avatarPeds.Count == Requirements.MaxAvatarPedestals ? BoothPerformanceRanking.Ok : avatarPeds.Count > Requirements.MaxAvatarPedestals ? BoothPerformanceRanking.Bad : BoothPerformanceRanking.Good;
            return new BoothStats(StatsType.AvatarPeds, ranking, "Avatar Pedestals: " + avatarPeds.Count + "/" + Requirements.MaxAvatarPedestals, $"Max Avatar Pedistals: {Requirements.MaxAvatarPedestals}", new List<object>(avatarPeds));
        }
        private static BoothStats GetPortals(List<VRCPortalMarker> portals)
        {
            BoothPerformanceRanking ranking = portals.Count == Requirements.MaxPortals ? BoothPerformanceRanking.Ok : portals.Count > Requirements.MaxPortals ? BoothPerformanceRanking.Bad : BoothPerformanceRanking.Good;
            return new BoothStats(StatsType.Portals, ranking, "Portals: " + portals.Count + "/" + Requirements.MaxPortals, $"Max Portals: {Requirements.MaxPortals}", new List<object>(portals));
        }

        private static BoothStats GetFileSize()
        {
            long sizeOnDisk = 0; 
            
            if (!Directory.Exists("Assets/PjktTemp")) Directory.CreateDirectory("Assets/PjktTemp");
            
            //temp prefab
            string tempPrefabPath = "Assets/PjktTemp/Booth_temp.prefab";
            PrefabUtility.SaveAsPrefabAsset(SelectedBooth.gameObject, tempPrefabPath);
            
            string[] dependencies = AssetDatabase.GetDependencies(tempPrefabPath);
            
            //get all dependencies
            foreach (string dependency in dependencies)
            {
                //skip built in resources
                if (dependency.Contains("unity_builtin_extra") || dependency.Contains("unity default resources")) continue;
                
                //read file size and add to total
                FileInfo fileInfo = new FileInfo(dependency);
                if (fileInfo.Exists) sizeOnDisk += fileInfo.Length;
            }
            
            //cleanup
            if (File.Exists(tempPrefabPath)) File.Delete(tempPrefabPath);
            if (File.Exists(tempPrefabPath + ".meta")) File.Delete(tempPrefabPath + ".meta");
            AssetDatabase.Refresh();

            long maxFileSize = Requirements.MaxFileSize * 1024 * 1024;
            
            //set performance and string
            BoothPerformanceRanking ranking = sizeOnDisk == maxFileSize ? BoothPerformanceRanking.Ok : sizeOnDisk > maxFileSize ? BoothPerformanceRanking.Bad : BoothPerformanceRanking.Good;
            return new BoothStats(StatsType.FileSize, ranking, "Approx file size: " + FormatSize(sizeOnDisk) + " / " + FormatSize(maxFileSize), $"Max Filesize: {maxFileSize}",new List<object>());
        }

        private static BoothStats GetInvalidObjects()
        {
            string detailsString = "";
            BoothPerformanceRanking ranking = BoothPerformanceRanking.Good;
            
            //get all reflection probes and directional lights
            ReflectionProbe[] probes = SelectedBooth.gameObject.GetComponentsInChildren<ReflectionProbe>(true);
            Light[] lights = SelectedBooth.gameObject.GetComponentsInChildren<Light>(true);
            
            if (probes.Length > 0)
            {
                detailsString += "Booth contains reflection probes. Please remove these";
                ranking = BoothPerformanceRanking.Bad;
            }

            int directionalLights = 0;
            foreach (Light light in lights) if (light.type == LightType.Directional) directionalLights++;
            if (directionalLights > 0)
            {
                if (detailsString != "") detailsString += "\n";
                detailsString += "Booth contains directional lights. Please remove these";
                ranking = BoothPerformanceRanking.Bad;
            }
            
            return new BoothStats(StatsType.InvalidObjects, ranking, detailsString, "Booths cannot contain Directional Lights or Reflection Probes", null);
        }
        
        public static string FormatSize(long size)
        {
            if (size < 1024)
                return size + " B";
            if (size < 1024 * 1024)
                return (size / 1024.00).ToString("##0.0") + " KB";
            if (size < 1024 * 1024 * 1024)
                return (size / (1024.0 * 1024.0)).ToString("##0.0") + " MB";
            return (size / (1024.0 * 1024.0 * 1024.0)).ToString("##0.0") + " GB";
        }

        private static MeshAsset GetAssetForMesh(Mesh mesh, MeshType type)
        {
            //check for built in resources
            string path = AssetDatabase.GetAssetPath(mesh);
            if (path.Contains("unity_builtin_extra") || path.Contains("unity default resources"))
            {
                return new MeshAsset()
                {
                    BlendShapes = mesh.blendShapeCount,
                    Name = mesh.name,
                    MaterialSlots = mesh.subMeshCount,
                    Type = type,
                    TriCount = mesh.triangles.Length / 3,
                    VramSize = Profiler.GetRuntimeMemorySizeLong(mesh),
                    SizeOnDisk = 0, //cant get size on disk for built in resources
                };
            }
            
            //fix error with no read/write access
            if (!mesh.isReadable)
            {
                return new MeshAsset()
                {
                    BlendShapes = mesh.blendShapeCount,
                    Name = mesh.name,
                    MaterialSlots = mesh.subMeshCount,
                    Type = type,
                    TriCount = 0,
                    VramSize = GetMeshVramSize(mesh),
                    SizeOnDisk = new FileInfo(AssetDatabase.GetAssetPath(mesh)).Length,
                };
            }
            
            return new MeshAsset()
            {
                BlendShapes = mesh.blendShapeCount,
                Name = mesh.name,
                MaterialSlots = mesh.subMeshCount,
                Type = type,
                TriCount = mesh.triangles.Length / 3,
                VramSize = GetMeshVramSize(mesh),
                SizeOnDisk = new FileInfo(AssetDatabase.GetAssetPath(mesh)).Length,
            };
        }
        
        private delegate void GetWidthAndHeight(TextureImporter importer, ref int width, ref int height);
        private static GetWidthAndHeight getWidthAndHeightDelegate;
        
        public static int[] GetOriginalTextureSize(TextureImporter importer)
        {
            var size = new int[2];
            if (importer == null) return size;
            
            if (getWidthAndHeightDelegate == null) {
                var method = typeof(TextureImporter).GetMethod("GetWidthAndHeight", BindingFlags.NonPublic | BindingFlags.Instance);
                getWidthAndHeightDelegate = Delegate.CreateDelegate(typeof(GetWidthAndHeight), null, method) as GetWidthAndHeight;
            }

            
            getWidthAndHeightDelegate(importer, ref size[0], ref size[1]);
 
            return size;
        }
        
        //You can thank Thry for this one https://github.com/Thryrallo/VRC-Avatar-Performance-Tools/tree/master
        private static Dictionary<Mesh, long> meshSizeCache = new Dictionary<Mesh, long>();
        
        private static Dictionary<VertexAttributeFormat, int> VertexAttributeByteSize = new Dictionary<VertexAttributeFormat, int>()
        {
            { VertexAttributeFormat.UNorm8, 1},
            { VertexAttributeFormat.SNorm8, 1},
            { VertexAttributeFormat.UInt8, 1},
            { VertexAttributeFormat.SInt8, 1},

            { VertexAttributeFormat.UNorm16, 2},
            { VertexAttributeFormat.SNorm16, 2},
            { VertexAttributeFormat.UInt16, 2},
            { VertexAttributeFormat.SInt16, 2},
            { VertexAttributeFormat.Float16, 2},

            { VertexAttributeFormat.Float32, 4},
            { VertexAttributeFormat.UInt32, 4},
            { VertexAttributeFormat.SInt32, 4},
        };
        private static long GetMeshVramSize(Mesh mesh)
        {
            if (meshSizeCache.ContainsKey(mesh))
                return meshSizeCache[mesh];
            
            long bytes = 0;

            var vertexAttributes = mesh.GetVertexAttributes();
            long vertexAttributeVRAMSize = 0;
            foreach (var vertexAttribute in vertexAttributes)
            {
                int skinnedMeshPositionNormalTangentMultiplier = 1;
                // skinned meshes have 2x the amount of position, normal and tangent data since they store both the un-skinned and skinned data in VRAM
                if (mesh.HasVertexAttribute(VertexAttribute.BlendIndices) && mesh.HasVertexAttribute(VertexAttribute.BlendWeight) &&
                    (vertexAttribute.attribute == VertexAttribute.Position || vertexAttribute.attribute == VertexAttribute.Normal || vertexAttribute.attribute == VertexAttribute.Tangent))
                    skinnedMeshPositionNormalTangentMultiplier = 2;
                vertexAttributeVRAMSize += VertexAttributeByteSize[vertexAttribute.format] * vertexAttribute.dimension * skinnedMeshPositionNormalTangentMultiplier;
            }
            var deltaPositions = new Vector3[mesh.vertexCount];
            var deltaNormals = new Vector3[mesh.vertexCount];
            var deltaTangents = new Vector3[mesh.vertexCount];
            long blendShapeVRAMSize = 0;
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                var blendShapeName = mesh.GetBlendShapeName(i);
                var blendShapeFrameCount = mesh.GetBlendShapeFrameCount(i);
                for (int j = 0; j < blendShapeFrameCount; j++)
                {
                    mesh.GetBlendShapeFrameVertices(i, j, deltaPositions, deltaNormals, deltaTangents);
                    for (int k = 0; k < deltaPositions.Length; k++)
                    {
                        if (deltaPositions[k] != Vector3.zero || deltaNormals[k] != Vector3.zero || deltaTangents[k] != Vector3.zero)
                        {
                            // every affected vertex has 1 uint for the index, 3 floats for the position, 3 floats for the normal and 3 floats for the tangent
                            // this is true even if all normals or tangents in all blend shapes are zero
                            blendShapeVRAMSize += 40;
                        }
                    }
                }
            }
            bytes = vertexAttributeVRAMSize * mesh.vertexCount + blendShapeVRAMSize;
            meshSizeCache[mesh] = bytes;
            return bytes;
        }
        
        //thry's Texture vram stuff, from my understanding it takes the number of pixels * the bits per pixel
        static Dictionary<TextureImporterFormat, int> BPP = new Dictionary<TextureImporterFormat, int>()
        {
            { TextureImporterFormat.BC7 , 8 },
            { TextureImporterFormat.DXT5 , 8 },
            { TextureImporterFormat.DXT5Crunched , 8 },
            { TextureImporterFormat.RGBA64 , 64 },
            { TextureImporterFormat.RGBA32 , 32 },
            { TextureImporterFormat.RGBA16 , 16 },
            { TextureImporterFormat.DXT1 , 4 },
            { TextureImporterFormat.DXT1Crunched , 4 },
            { TextureImporterFormat.RGB48 , 64 },
            { TextureImporterFormat.RGB24 , 32 },
            { TextureImporterFormat.RGB16 , 16 },
            { TextureImporterFormat.BC5 , 8 },
            { TextureImporterFormat.RG32 , 32 },
            { TextureImporterFormat.BC4 , 4 },
            { TextureImporterFormat.R8 , 8 },
            { TextureImporterFormat.R16 , 16 },
            { TextureImporterFormat.Alpha8 , 8 },
            { TextureImporterFormat.RGBAHalf , 64 },
            { TextureImporterFormat.BC6H , 8 },
            { TextureImporterFormat.RGB9E5 , 32 },
            { TextureImporterFormat.ETC2_RGBA8Crunched , 8 },
            { TextureImporterFormat.ETC2_RGB4 , 4 },
            { TextureImporterFormat.ETC2_RGBA8 , 8 },
            { TextureImporterFormat.ETC2_RGB4_PUNCHTHROUGH_ALPHA , 4 },
            { TextureImporterFormat.PVRTC_RGB2 , 2 },
            { TextureImporterFormat.PVRTC_RGB4 , 4 },
            { TextureImporterFormat.ARGB32 , 32 },
            { TextureImporterFormat.ARGB16 , 16 }
        };

        private static long CalculateTextureVram(Texture texture, TextureImporterFormat format)
        {
            if (!BPP.ContainsKey(format)) return Profiler.GetRuntimeMemorySizeLong(texture);
            
            int bpp = BPP[format];
            long bytes = 0;
            double mipmaps = 1;
            for (int i = 0; i < texture.mipmapCount; i++) mipmaps += Math.Pow(0.25, i + 1);
            bytes = (long)(bpp * texture.width * texture.height * (texture.mipmapCount > 1 ? mipmaps : 1) / 8);

            return bytes;
        }

        //for debugging
        [MenuItem("PJKT SDK/Tools/Run booth preprocessing")]
        public static void PreProcessBooth()
        {
            if (Selection.activeGameObject == null)
            {
                EditorUtility.DisplayDialog("PJKT SDK", "Please select a gameobject with a booth descriptor component first.", "OK");
                return;
            }

            BoothDescriptor booth = Selection.activeGameObject.GetComponent<BoothDescriptor>();
            if (booth == null)
            {
                EditorUtility.DisplayDialog("PJKT SDK", "Please select a gameobject with a booth descriptor component first.", "OK");
                return;
            }
            
            if (!BoothInfoButton.ConfirmBoothChanges()) return;
            
            PrepareBooth(booth);
        }
        
        public static void PrepareBooth(BoothDescriptor booth)
        {
            if (booth == null)
            {
                Debug.LogError("<color=#FFBB00><b>PJKT SDK:</b></color> Object does not have a booth descriptor");
                return;
            }

            if (Report == null || Report.Booth != booth)
            {
                Debug.LogError("<color=#FFBB00><b>PJKT SDK:</b></color> Something went wrong, select this booth in the SDK and try again.");
            }
            
            //set all meshes to low mesh compression
            BoothStats meshes = Report.GetStats(StatsType.TriCount);
            foreach (var mesh in meshes.ComponentList)
            {
                string assetpath = (Mesh)mesh is Mesh ? AssetDatabase.GetAssetPath((Mesh)mesh) : null;
                if (assetpath == null || assetpath.Contains(".asset")) continue;
                ModelImporter importer = AssetImporter.GetAtPath(assetpath) as ModelImporter;
                if (importer == null) continue;
                if (importer.meshCompression < ModelImporterMeshCompression.Low)
                {
                    importer.meshCompression = ModelImporterMeshCompression.Low;
                    importer.SaveAndReimport();
                }
            }
            
            //set all textures limits to 1k on android
            BoothStats textures = Report.GetStats(StatsType.Textures);
            foreach (TextureInfo info in textures.ComponentList)
            {
                if (info.importer == null) continue;
                TextureImporterPlatformSettings settings = info.importer.GetPlatformTextureSettings("Android");
                settings.overridden = true;
                settings.maxTextureSize = Mathf.Min(settings.maxTextureSize, 1024);
                info.importer.SetPlatformTextureSettings(settings);
                info.importer.SaveAndReimport();
            }
            
            //get all gameobjects in the booth
            List<Transform> objects = new List<Transform>();
            GetChildTransforms(booth.transform, objects);
            
            if (booth.gameObject.layer == 0) booth.gameObject.layer = 22;
            List<string> objectPaths = new List<string>();
            objectPaths.Add(GetGameobjectPath(booth.gameObject));
            foreach (Transform child in objects)
            {
                //set all stuff thats on default layer to layer 22
                if (child.gameObject.layer == 0) child.gameObject.layer = 22;
                
                //pickups go to layer 23
                if (child.TryGetComponent(typeof(VRCPickup), out _))
                {
                    SetLayerRecursively(child, 23);
                    continue;
                }
                
                //canvases and thier children do too.
                if (child.TryGetComponent(typeof(Canvas), out _))
                {
                    SetLayerRecursively(child, 23);
                }
                
                //ignore skinned meshes and pickups
                if (child.TryGetComponent(typeof(SkinnedMeshRenderer), out _)) continue;
                objectPaths.Add(GetGameobjectPath(child.gameObject));
            }
            
            //remove reflection probes
            ReflectionProbe[] probes = booth.GetComponentsInChildren<ReflectionProbe>();
            for (int i = 0; i < probes.Length; i++)
            {
                GameObject.DestroyImmediate(probes[i]);
            }
            
            //find all lights and limit
            Light[] lights = booth.GetComponentsInChildren<Light>();
            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i].type == LightType.Directional)
                {
                    GameObject.DestroyImmediate(lights[i]);
                    continue;
                }
                
                //light limits
                lights[i].lightmapBakeType = LightmapBakeType.Baked;
                lights[i].intensity = Mathf.Clamp(lights[i].intensity, 0, 10);
                lights[i].range = Mathf.Clamp(lights[i].range, 0, 7);
            }
            
            //limit audio to 3d
            AudioSource[] audioSources = booth.GetComponentsInChildren<AudioSource>();
            for (int i = 0; i < audioSources.Length; i++)
            {
                audioSources[i].spatialBlend = 1;
            }

            //get all animations from the report
            BoothStats animStats = Report.GetStats(StatsType.AnimationClips);
            List<AnimationClip> animationClips = new List<AnimationClip>();
            foreach (object clip in animStats.ComponentList) animationClips.Add(clip as AnimationClip);

            //get all gameobjects affected by the animations
            HashSet<string> affectedPaths = new HashSet<string>();

            // Collect all paths affected by animations
            foreach (var clip in animationClips)
            {
                var bindings = AnimationUtility.GetCurveBindings(clip);
                foreach (var binding in bindings)
                {
                    affectedPaths.Add(binding.path); // Full path of the animated object
                }
            }
            
            // Remove affected objects and their children
            objectPaths = objectPaths.Where(path => !IsAffectedOrChild(path, affectedPaths)).ToList();
            
            //set flags on remaining objects
            StaticEditorFlags flags = StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic;
            for (int i = 0; i < objectPaths.Count; i++)
            {
                GameObject obj = GameObject.Find(objectPaths[i]);
                if (obj == null) continue;
                
                StaticEditorFlags existingFlags = GameObjectUtility.GetStaticEditorFlags(obj);
                StaticEditorFlags newFlags = existingFlags | flags;
                GameObjectUtility.SetStaticEditorFlags(obj, newFlags);
            }
        }
        
        private static void GetChildTransforms(Transform current, List<Transform> result)
        {
            result.Add(current);
        
            for (int i = 0; i < current.childCount; i++)
            {
                GetChildTransforms(current.GetChild(i), result);
            }
        }

        private static void SetLayerRecursively(Transform obj, int layer)
        {
            obj.gameObject.layer = layer;
            foreach (Transform child in obj)
            {
                SetLayerRecursively(child, layer);
            }
        }
        
        private  static bool IsAffectedOrChild(string path, HashSet<string> affectedPaths)
        {
            // If the exact object is animated, remove it
            if (affectedPaths.Contains(path)) return true;

            // Check if it is a child of an affected object
            foreach (var affectedPath in affectedPaths)
            {
                if (path.StartsWith(affectedPath + "/")) // Ensures children are matched
                {
                    return true;
                }
            }

            return false;
        }
        
        private static string GetGameobjectPath(GameObject go) {
            string path = go.name;
            while (go.transform.parent != null) {
                go = go.transform.parent.gameObject;
                //If the name contains a /, it will break the path
                if (go.name.Contains("/")) {
                    //Debug.LogError("GameObject name contains a /");
                    return "";
                }
                path = go.name + "/" + path;
            }
            return path;
        }
    }
}