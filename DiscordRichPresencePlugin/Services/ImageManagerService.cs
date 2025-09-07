using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DiscordRichPresencePlugin.Models;
using IOPath = System.IO.Path;

namespace DiscordRichPresencePlugin.Services
{
    /// <summary>
    /// Підготовка локальних зображень для Discord assets:
    /// - масштаб до діапазону [512..1024] по обох осях,
    /// - якщо після масштабування одна зі сторін < 512 → додаємо прозору підкладку,
    /// - PNG як перша спроба, потім JPEG зі зниженням якості до <256KB,
    /// - кеш за хешем джерела.
    /// </summary>
    public class ImageManagerService
    {
        private const int MinSizePx = 512;
        private const int MaxSizePx = 1024;
        private const long MaxBytes = 256 * 1024; // 256KB

        private readonly IPlayniteAPI api;
        private readonly ILogger logger;
        private readonly GameMappingService mappingService;
        private readonly string assetsDir;
        private readonly string manifestPath;

        private readonly object sync = new object();
        private Dictionary<string, string> manifest; // assetKey -> sourceHash

        public ImageManagerService(
            IPlayniteAPI api,
            ILogger logger,
            GameMappingService mappingService,
            string pluginUserDataPath)
        {
            this.api = api;
            this.logger = logger;
            this.mappingService = mappingService;

            assetsDir = IOPath.Combine(pluginUserDataPath, "assets");
            if (!Directory.Exists(assetsDir))
            {
                Directory.CreateDirectory(assetsDir);
            }

            manifestPath = IOPath.Combine(assetsDir, "assets_manifest.json");
            LoadManifest();
        }

        public (string localPath, string assetKey) PrepareGameImage(Game game)
        {
            if (game == null || string.IsNullOrWhiteSpace(game.Name))
            {
                return (null, null);
            }

            var assetKey = mappingService?.GetImageKeyForGame(game.Name);
            if (string.IsNullOrWhiteSpace(assetKey))
            {
                logger?.Debug($"ImageManager: no mapping image key for '{game.Name}', skipping.");
                return (null, null);
            }

            var sourcePath = ResolveBestSourceImage(game);
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                logger?.Debug($"ImageManager: no source image for '{game.Name}', key '{assetKey}'");
                return (null, assetKey);
            }

            var destJpg = IOPath.Combine(assetsDir, assetKey + ".jpg");
            var destPng = IOPath.Combine(assetsDir, assetKey + ".png");

            var srcHash = ComputeFileHash(sourcePath);

            string cachedPath;
            if (IsCachedUpToDate(assetKey, srcHash, out cachedPath))
            {
                string reason;
                if (ValidateLocalAsset(cachedPath, out reason))
                {
                    logger?.Debug($"ImageManager: cache hit for '{game.Name}' ({assetKey}) -> {IOPath.GetFileName(cachedPath)}");
                    return (cachedPath, assetKey);
                }
            }

            try
            {
                var tmpPng = destPng + ".tmp";
                var tmpJpg = destJpg + ".tmp";

                var src = LoadBitmap(sourcePath);
                var prepared = ResizeAndPadToRange(src, MinSizePx, MaxSizePx); // ⬅️ нова логіка

                // 1) PNG спроба
                SavePng(prepared, tmpPng);
                var fileToUse = tmpPng;

                var fi = new FileInfo(tmpPng);
                if (fi.Length > MaxBytes)
                {
                    // 2) JPEG з пониженням якості
                    int[] qualities = new[] { 90, 85, 80, 75, 70, 65, 60, 55, 50 };
                    bool ok = false;
                    foreach (var q in qualities)
                    {
                        SaveJpeg(prepared, tmpJpg, q);
                        var fj = new FileInfo(tmpJpg);
                        if (fj.Length <= MaxBytes)
                        {
                            fileToUse = tmpJpg;
                            ok = true;
                            break;
                        }
                    }
                    if (!ok)
                    {
                        fileToUse = tmpJpg; // остання спроба буде найменшою
                    }

                    TryDelete(tmpPng);
                }

                if (fileToUse == tmpPng)
                {
                    TryDelete(destPng);
                    if (File.Exists(destPng)) File.Delete(destPng);
                    File.Move(tmpPng, destPng);
                    TryDelete(tmpJpg);
                    UpdateManifest(assetKey, srcHash, destPng);
                    return (destPng, assetKey);
                }
                else
                {
                    TryDelete(destJpg);
                    if (File.Exists(destJpg)) File.Delete(destJpg);
                    File.Move(tmpJpg, destJpg);
                    TryDelete(tmpPng);
                    UpdateManifest(assetKey, srcHash, destJpg);
                    return (destJpg, assetKey);
                }
            }
            catch (Exception ex)
            {
                logger?.Error($"ImageManager: failed to prepare image for '{game.Name}' ({assetKey}): {ex.Message}");
                return (null, assetKey);
            }
        }

        public bool ValidateLocalAsset(string path, out string reason)
        {
            reason = null;
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    reason = "File not found.";
                    return false;
                }

                var fi = new FileInfo(path);
                if (fi.Length > MaxBytes)
                {
                    reason = $">256KB ({fi.Length} bytes)";
                    return false;
                }

                int w, h;
                if (!TryReadDimensions(path, out w, out h))
                {
                    reason = "Unable to read image dimensions.";
                    return false;
                }

                if (w < MinSizePx || h < MinSizePx)
                {
                    reason = $"< {MinSizePx}px ({w}x{h})";
                    return false;
                }
                if (w > MaxSizePx || h > MaxSizePx)
                {
                    reason = $"> {MaxSizePx}px ({w}x{h})";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                reason = ex.Message;
                return false;
            }
        }

        public void OpenAssetsFolder()
        {
            try
            {
                if (!Directory.Exists(assetsDir))
                {
                    Directory.CreateDirectory(assetsDir);
                }
                System.Diagnostics.Process.Start("explorer.exe", assetsDir);
            }
            catch (Exception ex)
            {
                logger?.Error($"ImageManager: failed to open assets folder: {ex.Message}");
            }
        }

        // ===== Internals =====

        private string ResolveBestSourceImage(Game game)
        {
            var cover = GetFullDbPath(game.CoverImage);
            if (File.Exists(cover)) return cover;

            var back = GetFullDbPath(game.BackgroundImage);
            if (File.Exists(back)) return back;

            var icon = GetFullDbPath(game.Icon);
            if (File.Exists(icon)) return icon;

            return null;
        }

        private string GetFullDbPath(string dbMediaPath)
        {
            if (string.IsNullOrWhiteSpace(dbMediaPath))
                return null;

            try
            {
                return api.Database.GetFullFilePath(dbMediaPath);
            }
            catch
            {
                if (IOPath.IsPathRooted(dbMediaPath)) return dbMediaPath;
                var imagesRoot = IOPath.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Playnite", "library", "files");
                return IOPath.Combine(imagesRoot, dbMediaPath);
            }
        }

        private static BitmapSource LoadBitmap(string path)
        {
            using (var fs = File.OpenRead(path))
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad; // не тримаємо файл відкритим
                bi.StreamSource = fs;
                bi.EndInit();
                bi.Freeze();
                return bi;
            }
        }

        /// <summary>
        /// Масштабує зображення так, щоб виконувались обмеження:
        /// - max(dim) ≤ MaxPx,
        /// - min(dim) ≥ MinPx (якщо не виконується після масштабування — додає прозору підкладку).
        /// </summary>
        private static BitmapSource ResizeAndPadToRange(BitmapSource src, int minPx, int maxPx)
        {
            int sw = src.PixelWidth;
            int sh = src.PixelHeight;

            if (sw <= 0 || sh <= 0)
                return src;

            // Обчислюємо допустимий масштаб s:
            // 512 ≤ min(sw*s, sh*s),  max(sw*s, sh*s) ≤ 1024
            // sLower = 512 / min(sw,sh), sUpper = 1024 / max(sw,sh)
            double sLower = (double)minPx / Math.Min(sw, sh);
            double sUpper = (double)maxPx / Math.Max(sw, sh);

            double s = (sLower <= sUpper) ? sLower : sUpper; // якщо неможливо виконати обидва — не перевищуємо max
            if (double.IsNaN(s) || double.IsInfinity(s) || s <= 0) s = 1.0;

            // Масштабуємо
            var scaled = new TransformedBitmap(src, new ScaleTransform(s, s));
            scaled.Freeze();

            int tw = (int)Math.Round(sw * s);
            int th = (int)Math.Round(sh * s);

            // Якщо якась зі сторін все ще < minPx — додаємо прозору підкладку до minPx
            int canvasW = Math.Max(tw, minPx);
            int canvasH = Math.Max(th, minPx);

            // Якщо обидві сторони вже в діапазоні — підкладка не потрібна
            if (canvasW == tw && canvasH == th)
            {
                return scaled;
            }

            // Малюємо scaled по центру прозорого полотна
            var dv = new System.Windows.Media.DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                // Прозорий фон (нічого не малюємо — за замовчуванням прозорий)
                // Центруємо
                double ox = (canvasW - tw) / 2.0;
                double oy = (canvasH - th) / 2.0;
                dc.DrawImage(scaled, new System.Windows.Rect(ox, oy, tw, th));
            }

            var rtb = new RenderTargetBitmap(canvasW, canvasH, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }

        private static void SavePng(BitmapSource bitmap, string path)
        {
            var dir = IOPath.GetDirectoryName(path);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));

            using (var fs = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                encoder.Save(fs);
            }
        }

        private static void SaveJpeg(BitmapSource bitmap, string path, int quality)
        {
            var dir = IOPath.GetDirectoryName(path);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            var encoder = new JpegBitmapEncoder
            {
                QualityLevel = Math.Max(1, Math.Min(100, quality))
            };
            encoder.Frames.Add(BitmapFrame.Create(bitmap));

            using (var fs = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                encoder.Save(fs);
            }
        }

        private void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        private string ComputeFileHash(string path)
        {
            try
            {
                using (var sha1 = SHA1.Create())
                using (var fs = File.OpenRead(path))
                {
                    var hash = sha1.ComputeHash(fs);
                    return BitConverter.ToString(hash).Replace("-", "");
                }
            }
            catch
            {
                return null;
            }
        }

        private bool IsCachedUpToDate(string assetKey, string newHash, out string cachedPath)
        {
            cachedPath = null;
            lock (sync)
            {
                string oldHash;
                if (!manifest.TryGetValue(assetKey, out oldHash) || string.IsNullOrEmpty(oldHash) || string.IsNullOrEmpty(newHash))
                    return false;

                var jpg = IOPath.Combine(assetsDir, assetKey + ".jpg");
                var png = IOPath.Combine(assetsDir, assetKey + ".png");
                var existing = File.Exists(jpg) ? jpg : (File.Exists(png) ? png : null);
                if (existing == null) return false;

                string reason;
                if (!ValidateLocalAsset(existing, out reason)) return false;
                if (!string.Equals(oldHash, newHash, StringComparison.OrdinalIgnoreCase)) return false;

                cachedPath = existing;
                return true;
            }
        }

        private void UpdateManifest(string assetKey, string srcHash, string savedFilePath)
        {
            lock (sync)
            {
                manifest[assetKey] = srcHash;
                SaveManifest();
            }
            logger?.Debug($"ImageManager: prepared {IOPath.GetFileName(savedFilePath)} for asset '{assetKey}'");
        }

        private void LoadManifest()
        {
            try
            {
                if (File.Exists(manifestPath))
                {
                    var json = File.ReadAllText(manifestPath);
                    manifest = Playnite.SDK.Data.Serialization.FromJson<Dictionary<string, string>>(json)
                               ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }
                else
                {
                    manifest = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }
            }
            catch
            {
                manifest = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private void SaveManifest()
        {
            try
            {
                var json = Playnite.SDK.Data.Serialization.ToJson(manifest, true);
                File.WriteAllText(manifestPath, json);
            }
            catch (Exception ex)
            {
                logger?.Error($"ImageManager: failed to save manifest: {ex.Message}");
            }
        }

        private static bool TryReadDimensions(string path, out int width, out int height)
        {
            width = height = 0;
            try
            {
                using (var fs = File.OpenRead(path))
                {
                    var decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    var frame = decoder.Frames.FirstOrDefault();
                    if (frame == null) return false;
                    width = frame.PixelWidth;
                    height = frame.PixelHeight;
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
