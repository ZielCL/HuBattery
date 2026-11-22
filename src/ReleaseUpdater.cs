using System;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using Microsoft.Win32;

[assembly: AssemblyInformationalVersion(ReleaseUpdater.CurrentVersion)]
[assembly: AssemblyFileVersion("0.4.1.0")]

internal static class BootUpdateState
{
    private const string Key = @"Software\HuBattery";
    private const string Value = "LastUpdateBoot";
    private const int SystemBootEnvironmentInformation = 90;

    internal static bool ShouldCheck()
    {
        var data = new byte[32];
        int returned;
        if (NtQuerySystemInformation(SystemBootEnvironmentInformation, data, data.Length, out returned) != 0 ||
            returned < 16)
            return false;

        var id = new byte[16];
        Buffer.BlockCopy(data, 0, id, 0, id.Length);
        var boot = new Guid(id);
        if (boot == Guid.Empty)
            return false;

        try
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(Key))
            {
                if (key == null)
                    return false;
                string previous = key.GetValue(Value) as string;
                string current = boot.ToString("D");
                if (string.Equals(previous, current, StringComparison.OrdinalIgnoreCase))
                    return false;
                key.SetValue(Value, current);
                return ShouldCheck(previous, boot);
            }
        }
        catch (UnauthorizedAccessException) { return false; }
        catch (SecurityException) { return false; }
        catch (IOException) { return false; }
    }

    internal static bool ShouldCheck(string previous, Guid current)
    {
        Guid lastBoot;
        return current != Guid.Empty && Guid.TryParse(previous, out lastBoot) &&
            lastBoot != Guid.Empty && lastBoot != current;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQuerySystemInformation(int informationClass, [Out] byte[] information,
        int length, out int returnLength);
}

internal sealed class ReleaseAsset
{
    public string name { get; set; }
    public string state { get; set; }
    public long size { get; set; }
    public string digest { get; set; }
    public string browser_download_url { get; set; }
}

internal sealed class GitHubRelease
{
    public string tag_name { get; set; }
    public bool draft { get; set; }
    public bool prerelease { get; set; }
    public ReleaseAsset[] assets { get; set; }
}

internal sealed class UpdateCandidate
{
    internal readonly string Tag;
    internal readonly Uri Download;
    internal readonly long Size;
    internal readonly string Hash;

    internal UpdateCandidate(string tag, Uri download, long size, string hash)
    {
        Tag = tag;
        Download = download;
        Size = size;
        Hash = hash;
    }
}

internal static class ReleaseUpdater
{
    internal const string CurrentVersion = "0.4.1-beta.2";
    private const string Releases = "https://api.github.com/repos/ZielCL/HuBattery/releases?per_page=30";
    private const string Downloads = "https://github.com/ZielCL/HuBattery/releases/download/";
    private const int MaxReleaseBytes = 4 * 1024 * 1024;
    private const int MaxExecutableBytes = 32 * 1024 * 1024;
    private static readonly Regex Tags = new Regex(@"^v?(\d+\.\d+\.\d+)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?\z");
    private static readonly Regex Sha256 = new Regex(@"\A[0-9a-fA-F]{64}\z");

    internal static UpdateCandidate Check()
    {
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        using (var response = (HttpWebResponse)CreateRequest(new Uri(Releases), "application/vnd.github+json").GetResponse())
        using (var data = new MemoryStream())
        {
            ReadResponse(response, data, MaxReleaseBytes);
            return SelectLatest(System.Text.Encoding.UTF8.GetString(data.ToArray()), CurrentVersion);
        }
    }

    internal static UpdateCandidate SelectLatest(string json, string current)
    {
        var serializer = new JavaScriptSerializer { MaxJsonLength = MaxReleaseBytes };
        return SelectLatest(serializer.Deserialize<GitHubRelease[]>(json), current);
    }

    internal static UpdateCandidate SelectLatest(GitHubRelease[] releases, string current)
    {
        var installed = new ReleaseVersion(current);
        ReleaseVersion best = installed;
        UpdateCandidate result = null;

        foreach (GitHubRelease release in releases ?? new GitHubRelease[0])
        {
            if (release == null || release.draft || string.IsNullOrEmpty(release.tag_name) ||
                !release.tag_name.StartsWith("v", StringComparison.Ordinal))
                continue;

            var version = new ReleaseVersion(release.tag_name);
            if (version.Core == null || (installed.IsStable && (release.prerelease || !version.IsStable)) ||
                version.CompareTo(best) <= 0)
                continue;

            string url = Downloads + release.tag_name + "/HuBattery.exe";
            foreach (ReleaseAsset asset in release.assets ?? new ReleaseAsset[0])
            {
                if (asset == null || asset.name != "HuBattery.exe" || asset.state != "uploaded" ||
                    asset.size <= 0 || asset.size > MaxExecutableBytes || asset.browser_download_url != url ||
                    asset.digest == null || !asset.digest.StartsWith("sha256:", StringComparison.Ordinal))
                    continue;

                string hash = asset.digest.Substring("sha256:".Length);
                if (!IsHash(hash))
                    continue;

                best = version;
                result = new UpdateCandidate(release.tag_name, new Uri(url), asset.size, hash);
                break;
            }
        }
        return result;
    }

    internal static void Download(UpdateCandidate candidate, string destination)
    {
        Uri url = candidate.Download;
        for (int redirects = 0; redirects <= 4; redirects++)
        {
            if (url.Scheme != Uri.UriSchemeHttps || !url.IsDefaultPort || url.UserInfo.Length != 0 ||
                (url.Host != "github.com" && url.Host != "release-assets.githubusercontent.com" &&
                    url.Host != "objects.githubusercontent.com"))
                throw new InvalidDataException("GitHub devolvió una dirección de descarga inesperada.");

            using (var response = (HttpWebResponse)CreateRequest(url, "application/octet-stream").GetResponse())
            {
                int status = (int)response.StatusCode;
                if (status == 301 || status == 302 || status == 303 || status == 307 || status == 308)
                {
                    Uri next;
                    if (!Uri.TryCreate(url, response.Headers[HttpResponseHeader.Location], out next))
                        throw new InvalidDataException("GitHub devolvió una redirección inválida.");
                    url = next;
                    continue;
                }
                if (response.StatusCode != HttpStatusCode.OK)
                    throw new InvalidDataException("GitHub no devolvió el ejecutable.");

                using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write))
                    if (ReadResponse(response, output, candidate.Size) != candidate.Size)
                        throw new InvalidDataException("La descarga está incompleta.");

                if (!MatchesHash(destination, candidate.Hash) ||
                    AssemblyName.GetAssemblyName(destination).Name != "HuBattery" ||
                    System.Diagnostics.FileVersionInfo.GetVersionInfo(destination).ProductVersion != candidate.Tag.Substring(1))
                    throw new InvalidDataException("No se pudo verificar el ejecutable descargado.");
                return;
            }
        }
        throw new InvalidDataException("GitHub devolvió demasiadas redirecciones.");
    }

    internal static bool IsHash(string hash)
    {
        return hash != null && Sha256.IsMatch(hash);
    }

    internal static bool MatchesHash(string path, string expected)
    {
        return string.Equals(HashFile(path), expected, StringComparison.OrdinalIgnoreCase);
    }

    internal static string HashFile(string path)
    {
        using (var hash = SHA256.Create())
        using (var stream = File.OpenRead(path))
            return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "");
    }

    private static HttpWebRequest CreateRequest(Uri url, string accept)
    {
        var request = (HttpWebRequest)WebRequest.Create(url);
        request.UserAgent = "HuBattery/" + CurrentVersion;
        request.Accept = accept;
        request.AllowAutoRedirect = false;
        request.Timeout = 10000;
        request.ReadWriteTimeout = 10000;
        return request;
    }

    private static long ReadResponse(HttpWebResponse response, Stream output, long limit)
    {
        var buffer = new byte[8192];
        long total = 0;
        if (response.ContentLength > limit)
            throw new InvalidDataException("La respuesta de GitHub excede el tamaño esperado.");
        using (Stream input = response.GetResponseStream())
        {
            int count;
            while ((count = input.Read(buffer, 0, buffer.Length)) != 0)
            {
                total += count;
                if (total > limit)
                    throw new InvalidDataException("La respuesta de GitHub excede el tamaño esperado.");
                output.Write(buffer, 0, count);
            }
        }
        return total;
    }

    private sealed class ReleaseVersion : IComparable<ReleaseVersion>
    {
        internal readonly Version Core;
        private readonly string[] prerelease;
        internal bool IsStable { get { return prerelease.Length == 0; } }

        internal ReleaseVersion(string value)
        {
            Match match = Tags.Match(value);
            if (match.Success)
                Version.TryParse(match.Groups[1].Value, out Core);
            prerelease = match.Groups[2].Success ? match.Groups[2].Value.Split('.') : new string[0];
        }

        public int CompareTo(ReleaseVersion other)
        {
            int order = Core.CompareTo(other.Core);
            if (order != 0 || (IsStable && other.IsStable))
                return order;
            if (IsStable || other.IsStable)
                return IsStable ? 1 : -1;
            for (int i = 0; i < Math.Min(prerelease.Length, other.prerelease.Length); i++)
            {
                int left, right;
                bool leftNumber = int.TryParse(prerelease[i], System.Globalization.NumberStyles.None, null, out left);
                bool rightNumber = int.TryParse(other.prerelease[i], System.Globalization.NumberStyles.None, null, out right);
                if (leftNumber && rightNumber)
                    order = left.CompareTo(right);
                else if (leftNumber || rightNumber)
                    order = leftNumber ? -1 : 1;
                else
                    order = string.CompareOrdinal(prerelease[i], other.prerelease[i]);
                if (order != 0)
                    return order;
            }
            return prerelease.Length.CompareTo(other.prerelease.Length);
        }
    }
}
