using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace NeonSumo.Editor
{
    internal static class NeonSumoWebCompressionUtility
    {
        private const string WebPlatform = "WebGL";
        private const int DownscaleThreshold = 2048;
        private const int DownscaleLongestSide = 1024;
        private static readonly string[] IncludeRoots =
        {
            "Assets/ART",
            "Assets/UI"
        };

        private static readonly string[] ExcludedPathTokens =
        {
            "/Thirdparty/",
            "/TutorialInfo/",
            "/Creator Kit - Puzzle/"
        };

        [MenuItem("NeonSumo/Assets/Apply Web Compression (Textures + Models)")]
        public static void ApplyWebCompressionAll()
        {
            int texturesChanged = ApplyTextureCompression();
            int modelsChanged = ApplyModelCompression();
            Debug.Log($"[WebCompression] Completed. texturesChanged={texturesChanged}, modelsChanged={modelsChanged}");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        [MenuItem("NeonSumo/Assets/Apply Web Compression (Textures Only)")]
        public static int ApplyTextureCompression()
        {
            int changedCount = 0;
            int skippedCount = 0;
            int errorCount = 0;

            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", IncludeRoots))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!ShouldProcess(path))
                {
                    skippedCount++;
                    continue;
                }

                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                {
                    skippedCount++;
                    continue;
                }

                try
                {
                    if (ConfigureTextureImporter(importer, path))
                    {
                        changedCount++;
                        importer.SaveAndReimport();
                    }
                    else
                    {
                        skippedCount++;
                    }
                }
                catch (Exception ex)
                {
                    errorCount++;
                    Debug.LogWarning($"[WebCompression] Texture failed: {path} -> {ex.Message}");
                }
            }

            Debug.Log($"[WebCompression] Texture pass complete. changed={changedCount}, skipped={skippedCount}, errors={errorCount}");
            return changedCount;
        }

        [MenuItem("NeonSumo/Assets/Apply Web Compression (Models Only)")]
        public static int ApplyModelCompression()
        {
            int changedCount = 0;
            int skippedCount = 0;
            int errorCount = 0;

            foreach (string guid in AssetDatabase.FindAssets("t:Model", IncludeRoots))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!ShouldProcess(path))
                {
                    skippedCount++;
                    continue;
                }

                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null)
                {
                    skippedCount++;
                    continue;
                }

                try
                {
                    if (ConfigureModelImporter(importer))
                    {
                        changedCount++;
                        importer.SaveAndReimport();
                    }
                    else
                    {
                        skippedCount++;
                    }
                }
                catch (Exception ex)
                {
                    errorCount++;
                    Debug.LogWarning($"[WebCompression] Model failed: {path} -> {ex.Message}");
                }
            }

            Debug.Log($"[WebCompression] Model pass complete. changed={changedCount}, skipped={skippedCount}, errors={errorCount}");
            return changedCount;
        }

        [MenuItem("NeonSumo/Assets/Downscale Large Source Textures To 1K")]
        public static void DownscaleLargeSourceTexturesTo1K()
        {
            int changedCount = 0;
            int skippedCount = 0;
            int errorCount = 0;
            int verifiedCount = 0;

            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", IncludeRoots))
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (!ShouldProcess(assetPath))
                {
                    skippedCount++;
                    continue;
                }

                try
                {
                    if (!TryDownscaleTextureSource(assetPath, out bool changed))
                    {
                        errorCount++;
                        continue;
                    }

                    if (!changed)
                    {
                        skippedCount++;
                        continue;
                    }

                    changedCount++;
                    AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
                    if (VerifyImportedTextureMaxDimension(assetPath, DownscaleLongestSide))
                    {
                        verifiedCount++;
                    }
                    else
                    {
                        Debug.LogWarning($"[WebCompression] Verification failed (still > {DownscaleLongestSide}px): {assetPath}");
                    }
                }
                catch (Exception ex)
                {
                    errorCount++;
                    Debug.LogWarning($"[WebCompression] Downscale failed: {assetPath} -> {ex.Message}");
                }
            }

            Debug.Log(
                $"[WebCompression] Source downscale complete. changed={changedCount}, skipped={skippedCount}, errors={errorCount}, verified={verifiedCount}");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static bool ConfigureTextureImporter(TextureImporter importer, string assetPath)
        {
            bool changed = false;
            bool isLikelyUi = IsLikelyUiTexture(importer, assetPath);
            bool hasAlpha = importer.DoesSourceTextureHaveAlpha();

            if (!isLikelyUi && !importer.mipmapEnabled)
            {
                importer.mipmapEnabled = true;
                changed = true;
            }
            else if (isLikelyUi && importer.mipmapEnabled)
            {
                importer.mipmapEnabled = false;
                changed = true;
            }

            // Encourage compressed texture workflow for Web delivery.
            if (importer.textureCompression != TextureImporterCompression.Compressed)
            {
                importer.textureCompression = TextureImporterCompression.Compressed;
                changed = true;
            }

            TextureImporterPlatformSettings settings = importer.GetPlatformTextureSettings(WebPlatform);
            var desired = BuildDesiredWebSettings(settings.maxTextureSize, hasAlpha);

            if (!PlatformSettingsEqual(settings, desired))
            {
                importer.SetPlatformTextureSettings(desired);
                changed = true;
            }

            return changed;
        }

        private static TextureImporterPlatformSettings BuildDesiredWebSettings(int currentMaxSize, bool hasAlpha)
        {
            int maxSize = currentMaxSize > 0 ? currentMaxSize : 2048;
            var settings = new TextureImporterPlatformSettings
            {
                name = WebPlatform,
                overridden = true,
                maxTextureSize = maxSize,
                resizeAlgorithm = TextureResizeAlgorithm.Mitchell,
                format = TextureImporterFormat.Automatic,
                textureCompression = TextureImporterCompression.Compressed,
                compressionQuality = 70,
                allowsAlphaSplitting = false
            };

            // Keep format selection automatic while preserving alpha where needed.
            if (!hasAlpha)
            {
                settings.format = TextureImporterFormat.Automatic;
            }

            return settings;
        }

        private static bool ConfigureModelImporter(ModelImporter importer)
        {
            bool changed = false;

            if (importer.meshCompression != ModelImporterMeshCompression.Medium)
            {
                importer.meshCompression = ModelImporterMeshCompression.Medium;
                changed = true;
            }

            if (!importer.optimizeMeshPolygons)
            {
                importer.optimizeMeshPolygons = true;
                changed = true;
            }

            if (!importer.optimizeMeshVertices)
            {
                importer.optimizeMeshVertices = true;
                changed = true;
            }

            if (!importer.weldVertices)
            {
                importer.weldVertices = true;
                changed = true;
            }

            return changed;
        }

        private static bool TryDownscaleTextureSource(string assetPath, out bool changed)
        {
            changed = false;
            string extension = Path.GetExtension(assetPath)?.ToLowerInvariant();
            if (string.IsNullOrEmpty(extension))
            {
                Debug.LogWarning($"[WebCompression] Unsupported texture extension: {assetPath}");
                return true;
            }

            if (!IsSupportedImageExtension(extension))
            {
                Debug.LogWarning($"[WebCompression] Skipping unsupported source format ({extension}): {assetPath}");
                return true;
            }

            string absolutePath = Path.GetFullPath(assetPath);
            if (!File.Exists(absolutePath))
            {
                Debug.LogWarning($"[WebCompression] Source file not found: {assetPath}");
                return false;
            }

            byte[] sourceBytes = File.ReadAllBytes(absolutePath);
            var sourceTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
            if (!ImageConversion.LoadImage(sourceTexture, sourceBytes, false))
            {
                UnityEngine.Object.DestroyImmediate(sourceTexture);
                Debug.LogWarning($"[WebCompression] Could not decode source image: {assetPath}");
                return false;
            }

            int width = sourceTexture.width;
            int height = sourceTexture.height;
            if (width < DownscaleThreshold && height < DownscaleThreshold)
            {
                UnityEngine.Object.DestroyImmediate(sourceTexture);
                return true;
            }

            GetScaledSize(width, height, DownscaleLongestSide, out int targetWidth, out int targetHeight);
            Texture2D resizedTexture = ResizeTextureBilinear(sourceTexture, targetWidth, targetHeight);
            byte[] outputBytes = EncodeTextureBytes(resizedTexture, extension);

            UnityEngine.Object.DestroyImmediate(sourceTexture);
            UnityEngine.Object.DestroyImmediate(resizedTexture);

            if (outputBytes == null || outputBytes.Length == 0)
            {
                Debug.LogWarning($"[WebCompression] Failed to encode resized image: {assetPath}");
                return false;
            }

            File.WriteAllBytes(absolutePath, outputBytes);
            changed = true;
            return true;
        }

        private static bool VerifyImportedTextureMaxDimension(string assetPath, int maxDimension)
        {
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (texture == null)
            {
                return false;
            }

            return texture.width <= maxDimension && texture.height <= maxDimension;
        }

        private static bool IsSupportedImageExtension(string extension)
        {
            return extension == ".png"
                   || extension == ".jpg"
                   || extension == ".jpeg"
                   || extension == ".tga";
        }

        private static void GetScaledSize(int width, int height, int maxSide, out int outWidth, out int outHeight)
        {
            int longest = Mathf.Max(width, height);
            float scale = longest > 0 ? (float)maxSide / longest : 1f;
            outWidth = Mathf.Max(1, Mathf.RoundToInt(width * scale));
            outHeight = Mathf.Max(1, Mathf.RoundToInt(height * scale));
        }

        private static Texture2D ResizeTextureBilinear(Texture2D source, int targetWidth, int targetHeight)
        {
            var rt = RenderTexture.GetTemporary(targetWidth, targetHeight, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(source, rt);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;

            var result = new Texture2D(targetWidth, targetHeight, TextureFormat.RGBA32, false, false);
            result.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
            result.Apply(false, false);

            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            return result;
        }

        private static byte[] EncodeTextureBytes(Texture2D texture, string extension)
        {
            if (extension == ".png")
            {
                return texture.EncodeToPNG();
            }

            if (extension == ".jpg" || extension == ".jpeg")
            {
                return texture.EncodeToJPG(92);
            }

            if (extension == ".tga")
            {
                return ImageConversion.EncodeToTGA(texture);
            }

            return null;
        }

        private static bool PlatformSettingsEqual(TextureImporterPlatformSettings a, TextureImporterPlatformSettings b)
        {
            return a.name == b.name
                   && a.overridden == b.overridden
                   && a.maxTextureSize == b.maxTextureSize
                   && a.resizeAlgorithm == b.resizeAlgorithm
                   && a.format == b.format
                   && a.textureCompression == b.textureCompression
                   && a.compressionQuality == b.compressionQuality
                   && a.allowsAlphaSplitting == b.allowsAlphaSplitting;
        }

        private static bool IsLikelyUiTexture(TextureImporter importer, string assetPath)
        {
            if (importer.textureType == TextureImporterType.Sprite)
            {
                return true;
            }

            return assetPath.IndexOf("/UI/", StringComparison.OrdinalIgnoreCase) >= 0
                   || assetPath.IndexOf("/ART/UI/", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool ShouldProcess(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return false;
            }

            foreach (string token in ExcludedPathTokens)
            {
                if (assetPath.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
