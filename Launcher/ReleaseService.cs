using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace ShishaniRP;

internal sealed record ReleaseInfo(string Version, string AssetUrl, string Sha256, long Size);
internal sealed record InstallStatus(int Percent, string Message);

internal static class ReleaseService
{
    private const string Repo = "tcuevhostor-eng/shishani";
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromHours(2)
    };

    static ReleaseService()
    {
        Http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SHISHANI-Launcher", "1.0"));
        Http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public static async Task<ReleaseInfo> FindAsync()
    {
        using var response = await Http.GetAsync("https://api.github.com/repos/" + Repo + "/releases?per_page=30");
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        foreach (var rel in doc.RootElement.EnumerateArray())
        {
            if (rel.GetProperty("draft").GetBoolean()) continue;
            string version = rel.GetProperty("tag_name").GetString() ?? "";
            if (string.IsNullOrWhiteSpace(version)) continue;
            foreach (var asset in rel.GetProperty("assets").EnumerateArray())
            {
                if (asset.GetProperty("name").GetString() != "client.zip") continue;
                if (asset.GetProperty("state").GetString() != "uploaded") continue;
                string digest = asset.TryGetProperty("digest", out var d) ? d.GetString() ?? "" : "";
                if (!digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                    || digest.Length != 71 || !digest.Skip(7).All(Uri.IsHexDigit))
                    throw new Exception("В релизе отсутствует корректная SHA-256 контрольная сумма.");
                string link = asset.GetProperty("browser_download_url").GetString() ?? "";
                if (!Uri.TryCreate(link, UriKind.Absolute, out var uri)
                    || uri.Scheme != Uri.UriSchemeHttps || uri.Host != "github.com"
                    || !uri.AbsolutePath.StartsWith("/tcuevhostor-eng/shishani/releases/download/", StringComparison.OrdinalIgnoreCase))
                    throw new Exception("Ссылка архива должна вести на GitHub Releases SHISHANI.");
                long size = asset.GetProperty("size").GetInt64();
                if (size <= 0 || size > 2L * 1024 * 1024 * 1024)
                    throw new Exception("Недопустимый размер игрового архива.");
                return new ReleaseInfo(version, link, digest[7..], size);
            }
        }
        throw new Exception("Нет опубликованного релиза с файлом client.zip.");
    }

    public static async Task InstallAsync(ReleaseInfo release, string target, IProgress<InstallStatus> progress)
    {
        string scratch = Path.Combine(Path.GetTempPath(), "shishani-" + Guid.NewGuid().ToString("N"));
        string zipFile = Path.Combine(scratch, "client.zip");
        string stage = Path.Combine(scratch, "staged");
        string backups = Path.Combine(scratch, "backups");
        Directory.CreateDirectory(stage);
        Directory.CreateDirectory(backups);
        try
        {
            progress.Report(new(1, "Скачиваем файлы ШиШаНи..."));
            using (var response = await Http.GetAsync(release.AssetUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                if (response.RequestMessage?.RequestUri?.Scheme != "https")
                    throw new Exception("Загрузка перенаправлена на небезопасный адрес.");
                await using var src = await response.Content.ReadAsStreamAsync();
                await using var dst = new FileStream(zipFile, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, true);
                byte[] buf = new byte[131072];
                long count = 0;
                int previousPct = -1;
                int n;
                while ((n = await src.ReadAsync(buf)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n));
                    count += n;
                    int pct = (int)Math.Clamp(count * 73 / release.Size, 0, 73);
                    if (pct != previousPct)
                    {
                        progress.Report(new(pct, $"Загружено {count / 1048576.0:0.0} из {release.Size / 1048576.0:0.0} МБ"));
                        previousPct = pct;
                    }
                }
                if (count != release.Size) throw new Exception("Скачанный файл имеет неверный размер.");
            }

            progress.Report(new(75, "Проверка целостности архива (SHA-256)..."));
            string actual;
            await using (var file = File.OpenRead(zipFile))
            {
                byte[] hash = await SHA256.HashDataAsync(file);
                actual = Convert.ToHexString(hash);
            }
            if (!actual.Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new Exception("Контрольная сумма архива не совпадает. Файлы не установлены.");

            progress.Report(new(78, "Распаковка сборки..."));
            await Task.Run(() => ExtractSafely(zipFile, stage, progress));

            string sourceRoot = ResolveRoot(stage);
            string[] files = Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories);
            if (files.Length == 0) throw new Exception("Архив client.zip не содержит файлов.");

            progress.Report(new(86, "Установка игровых файлов..."));
            Directory.CreateDirectory(target);
            string backupRoot = Path.Combine(scratch, "backups");
            var changes = new List<(string destination, string? backup)>();
            try
            {
                await Task.Run(() =>
                {
                    string targetFull = Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    string sourcePrefix = Path.GetFullPath(sourceRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    for (int i = 0; i < files.Length; i++)
                    {
                        string relative = files[i][sourcePrefix.Length..];
                        string dest = Path.GetFullPath(Path.Combine(target, relative));
                        if (!dest.StartsWith(targetFull, StringComparison.OrdinalIgnoreCase))
                            throw new Exception("Небезопасный путь игрового файла.");
                        CheckReparseSegments(target, dest);
                        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                        string? backup = null;
                        if (File.Exists(dest))
                        {
                            backup = Path.Combine(backupRoot, i.ToString("D8") + ".bak");
                            File.Copy(dest, backup, true);
                        }
                        changes.Add((dest, backup));
                        File.Copy(files[i], dest, true);
                        if (i % 10 == 0 || i + 1 == files.Length)
                        {
                            int pct = 86 + (int)(14L * (i + 1) / files.Length);
                            progress.Report(new(pct, $"Установка файлов: {i + 1}/{files.Length}"));
                        }
                    }
                });
            }
            catch
            {
                for (int i = changes.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        var (dest, backup) = changes[i];
                        if (backup != null) File.Copy(backup, dest, true);
                        else if (File.Exists(dest)) File.Delete(dest);
                    }
                    catch { }
                }
                throw;
            }
            progress.Report(new(100, "Файлы установлены."));
        }
        finally
        {
            try { Directory.Delete(scratch, true); } catch { }
        }
    }

    private static string ResolveRoot(string stage)
    {
        string path = stage;
        for (int i = 0; i < 4; i++)
        {
            if (File.Exists(Path.Combine(path, "gta_sa.exe")) || File.Exists(Path.Combine(path, "samp.exe")))
                return path;
            string[] dirs = Directory.GetDirectories(path);
            string[] files = Directory.GetFiles(path);
            if (dirs.Length != 1 || files.Length != 0) break;
            path = dirs[0];
        }
        return path;
    }

    private static void ExtractSafely(string archive, string destination, IProgress<InstallStatus> progress)
    {
        using var zip = ZipFile.OpenRead(archive);
        if (zip.Entries.Count > 30000) throw new Exception("Слишком много файлов в архиве.");
        long uncompressed = 0;
        string prefix = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        int idx = 0;
        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            string name = entry.FullName.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(name) || name.StartsWith('/') || name.Contains(':')
                || name.Split('/').Any(s => s is "." or ".."))
                throw new Exception("Архив содержит небезопасный путь.");
            int unixType = (entry.ExternalAttributes >> 16) & 0xF000;
            if (unixType == 0xA000) throw new Exception("Символические ссылки в архиве запрещены.");
            string output = Path.GetFullPath(Path.Combine(destination, name.Replace('/', Path.DirectorySeparatorChar)));
            if (!output.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new Exception("Архив пытается записать файлы за пределами каталога.");
            if (name.EndsWith('/'))
            {
                Directory.CreateDirectory(output);
                continue;
            }
            uncompressed += entry.Length;
            if (uncompressed > 10L * 1024 * 1024 * 1024)
                throw new Exception("Размер распакованных файлов превышает 10 ГБ.");
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            entry.ExtractToFile(output, true);
            if ((++idx % 50) == 0)
                progress.Report(new(80 + 6 * idx / Math.Max(zip.Entries.Count, 1), $"Распаковано файлов: {idx}"));
        }
    }

    private static void CheckReparseSegments(string root, string file)
    {
        var dir = new DirectoryInfo(Path.GetFullPath(root));
        if ((dir.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new Exception("Нельзя устанавливать через символическую ссылку.");
        string relative = Path.GetRelativePath(root, file);
        string walk = Path.GetFullPath(root);
        foreach (string part in relative.Split(Path.DirectorySeparatorChar))
        {
            walk = Path.Combine(walk, part);
            if (File.Exists(walk) && (File.GetAttributes(walk) & FileAttributes.ReparsePoint) != 0)
                throw new Exception("Файл является символической ссылкой: " + walk);
            if (Directory.Exists(walk) && (File.GetAttributes(walk) & FileAttributes.ReparsePoint) != 0)
                throw new Exception("Папка является символической ссылкой: " + walk);
        }
    }
}
