/*
 * Copyright(c) Live2D Inc. All rights reserved.
 * 
 * Use of this source code is governed by the Live2D Open Software license
 * that can be found at http://live2d.com/eula/live2d-open-software-license-agreement_en.html.
 */


using Live2D.Cubism.Rendering;
using Live2D.Cubism.Rendering.Masking;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Live2D.Cubism.Editor.Importers;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Live2D.Cubism.Editor.Deleters;

namespace Live2D.Cubism.Editor
{
    /// <summary>
    /// Hooks into Unity's asset pipeline allowing custom processing of assets.
    /// </summary>
    public class CubismAssetProcessor : AssetPostprocessor
    {
        #region Unity Event Handling

        /// <summary>
        /// Called by Unity. Makes sure <see langword="unsafe"/> code is allowed.
        /// </summary>
        // ReSharper disable once InconsistentNaming
        public static void OnGeneratedCSProjectFiles()
        {
            AllowUnsafeCode();
        }


        /// <summary>
        /// Called by Unity on asset import. Handles importing of Cubism related assets.
        /// </summary>
        /// <param name="importedAssetPaths">Paths of imported assets.</param>
        /// <param name="deletedAssetPaths">Paths of removed assets.</param>
        /// <param name="movedAssetPaths">Paths of moved assets</param>
        /// <param name="movedFromAssetPaths">Paths of moved assets before moving</param>
        private static void OnPostprocessAllAssets(
            string[] importedAssetPaths,
            string[] deletedAssetPaths,
            string[] movedAssetPaths,
            string[] movedFromAssetPaths)
        {
            // Make sure builtin resources are available.
            GenerateBuiltinResources();


            // Handle any imported Cubism assets.
            foreach (var assetPath in importedAssetPaths)
            {
                var importer = CubismImporter.GetImporterAtPath(assetPath);


                if (importer == null)
                {
                    continue;
                }


                importer.Import();
            }


            // Handle any deleted Cubism assets.
            foreach (var assetPath in deletedAssetPaths)
            {
                var deleter = CubismDeleter.GetDeleterAsPath(assetPath);

                if (deleter == null)
                {
                    continue;
                }

                deleter.Delete();
            }

        }

        #endregion

        #region C# Project Patching

        /// <summary>
        /// Makes sure <see langword="unsafe"/> code is allowed in the runtime project.
        /// </summary>
        private static void AllowUnsafeCode()
        {
            foreach (var csproj in Directory.GetFiles(Directory.GetCurrentDirectory(), "*.csproj"))
            {
                // Skip Editor assembly.
                if (csproj.EndsWith(".Editor.csproj"))
                {
                    continue;
                }


                var document = XDocument.Load(csproj);
                var project = document.Root;


                // Allow unsafe code.
                for (var propertyGroup = project.FirstNode as XElement; propertyGroup != null; propertyGroup = propertyGroup.NextNode as XElement)
                {
                    // Skip non-relevant groups.
                    if (!propertyGroup.ToString().Contains("PropertyGroup") || !propertyGroup.ToString().Contains("$(Configuration)|$(Platform)"))
                    {
                        continue;
                    }


                    // Add unsafe-block element if necessary.
                    if (!propertyGroup.ToString().Contains("AllowUnsafeBlocks"))
                    {
                        var nameSpace = propertyGroup.GetDefaultNamespace();


                        propertyGroup.Add(new XElement(nameSpace + "AllowUnsafeBlocks", "true"));
                    }


                    // Make sure unsafe-block element is always set to true.
                    for (var allowUnsafeBlocks = propertyGroup.FirstNode as XElement; allowUnsafeBlocks != null; allowUnsafeBlocks = allowUnsafeBlocks.NextNode as XElement)
                    {
                        if (!allowUnsafeBlocks.ToString().Contains("AllowUnsafeBlocks"))
                        {
                            continue;
                        }


                        allowUnsafeBlocks.SetValue("true");
                    }
                }


                // Store changes.
                document.Save(csproj);
            }
        }

        #endregion

        #region Resources Generation

        /// <summary>
        /// Sets Cubism-style normal blending for a material.
        /// </summary>
        /// <param name="material">Material to set up.</param>
        private static void EnableNormalBlending(Material material)
        {
            material.SetInt("_SrcColor", (int)BlendMode.One);
            material.SetInt("_DstColor", (int)BlendMode.OneMinusSrcAlpha);
            material.SetInt("_SrcAlpha", (int)BlendMode.One);
            material.SetInt("_DstAlpha", (int)BlendMode.OneMinusSrcAlpha);
        }

        /// <summary>
        /// Sets Cubism-style additive blending for a material.
        /// </summary>
        /// <param name="material">Material to set up.</param>
        private static void EnableAdditiveBlending(Material material)
        {
            material.SetInt("_SrcColor", (int)BlendMode.One);
            material.SetInt("_DstColor", (int)BlendMode.One);
            material.SetInt("_SrcAlpha", (int)BlendMode.Zero);
            material.SetInt("_DstAlpha", (int)BlendMode.One);
        }

        /// <summary>
        /// Sets Cubism-style multiplicative blending for a material.
        /// </summary>
        /// <param name="material">Material to set up.</param>
        private static void EnableMultiplicativeBlending(Material material)
        {
            material.SetInt("_SrcColor", (int)BlendMode.DstColor);
            material.SetInt("_DstColor", (int)BlendMode.OneMinusSrcAlpha);
            material.SetInt("_SrcAlpha", (int)BlendMode.Zero);
            material.SetInt("_DstAlpha", (int)BlendMode.One);
        }

        /// <summary>
        /// Enables Cubism-style masking for a material.
        /// </summary>
        /// <param name="material">Material to set up.</param>
        private static void EnableMasking(Material material)
        {
            // Set toggle.
            material.SetInt("cubism_MaskOn", 1);


            // Enable keyword.
            var shaderKeywords = material.shaderKeywords.ToList();


            shaderKeywords.RemoveAll(k => k == "CUBISM_MASK_OFF");


            if (!shaderKeywords.Contains("CUBISM_MASK_ON"))
            {
                shaderKeywords.Add("CUBISM_MASK_ON");
            }


            material.shaderKeywords = shaderKeywords.ToArray();
        }


        /// <summary>
        /// Generates the builtin resources as necessary.
        /// </summary>
		private static void GenerateBuiltinResources()
        {
            var detectedLive2DFolderPath = GetTargetFolderPath("Live2D", "Assets/");

            if (string.IsNullOrEmpty(detectedLive2DFolderPath))
            {
                Debug.LogError("failed to find Live2D installed folder.");
                return;
            }

            var resourcePath = Path.Combine(detectedLive2DFolderPath, "Cubism/Rendering/Resources/Live2D/Cubism");

            if (!Directory.Exists(resourcePath))
            {
                Debug.Log("failed to detect Resources folder path.");
                return;
            }

            // create materials if need.
            var materialsPath = Path.Combine(resourcePath, "Materials");
            if (!Directory.Exists(materialsPath))
            {
                // create Materials folder.
                Directory.CreateDirectory(materialsPath);

                Debug.Log("フォルダ作った。 resourcePath:" + resourcePath);

                // Create mask material.
                var material = new Material(CubismBuiltinShaders.Mask)
                {
                    name = "Mask"
                };

                AssetDatabase.CreateAsset(material, Path.Combine(materialsPath, material.name) + ".mat");


                // Create non-masked materials.
                material = new Material(CubismBuiltinShaders.Unlit)
                {
                    name = "Unlit"
                };

                EnableNormalBlending(material);
                AssetDatabase.CreateAsset(material, Path.Combine(materialsPath, material.name) + ".mat");


                material = new Material(CubismBuiltinShaders.Unlit)
                {
                    name = "UnlitAdditive"
                };

                EnableAdditiveBlending(material);
                AssetDatabase.CreateAsset(material, Path.Combine(materialsPath, material.name) + ".mat");


                material = new Material(CubismBuiltinShaders.Unlit)
                {
                    name = "UnlitMultiply"
                };

                EnableMultiplicativeBlending(material);
                AssetDatabase.CreateAsset(material, Path.Combine(materialsPath, material.name) + ".mat");


                // Create masked materials.
                material = new Material(CubismBuiltinShaders.Unlit)
                {
                    name = "UnlitMasked"
                };

                EnableNormalBlending(material);
                EnableMasking(material);
                AssetDatabase.CreateAsset(material, Path.Combine(materialsPath, material.name) + ".mat");


                material = new Material(CubismBuiltinShaders.Unlit)
                {
                    name = "UnlitAdditiveMasked"
                };

                EnableAdditiveBlending(material);
                EnableMasking(material);
                AssetDatabase.CreateAsset(material, Path.Combine(materialsPath, material.name) + ".mat");


                material = new Material(CubismBuiltinShaders.Unlit)
                {
                    name = "UnlitMultiplyMasked"
                };

                EnableMultiplicativeBlending(material);
                EnableMasking(material);
                AssetDatabase.CreateAsset(material, Path.Combine(materialsPath, material.name) + ".mat");


                EditorUtility.SetDirty(CubismBuiltinShaders.Unlit);
                AssetDatabase.SaveAssets();
            }

            // Create global mask texture.
            var globalMaskTexture = ScriptableObject.CreateInstance<CubismMaskTexture>();
            if (globalMaskTexture == null)
            {
                Debug.LogError("failed to load CubismMaskTexture");
                return;
            }

            var targetAssetPath = Path.Combine(resourcePath, "GlobalMaskTexture") + ".asset";
            if (!File.Exists(targetAssetPath))
            {
                AssetDatabase.CreateAsset(globalMaskTexture, targetAssetPath);
            }
        }

        private static string GetTargetFolderPath(string targetFolderName, string findStartPath)
        {
            var childDirectoryPaths = Directory.GetDirectories(findStartPath);

            return FindRecursive(targetFolderName, childDirectoryPaths);
        }

        private static string FindRecursive(string targetFolderName, string[] paths)
        {
            foreach (var dirPath in paths)
            {
                // チェックを行う
                if (dirPath.Contains(targetFolderName))
                {
                    return dirPath;
                }

                // まだパスに欲しいフォルダ名が含まれていない。

                // 下の階層があるかチェック
                var child2 = Directory.GetDirectories(dirPath);
                if (child2.Length == 0)
                {
                    continue;
                }

                // 下の階層があるので、読み込み
                var result = FindRecursive(targetFolderName, child2);
                if (!string.IsNullOrEmpty(result))
                {
                    return result;
                }
            }

            return string.Empty;
        }

        #endregion
    }
}