using System;
using System.Diagnostics;
using System.Windows.Forms;
using System.IO;
using System.Text.Json;
using System.Linq;

namespace FEBuilderGBA
{
    class UpdateCheck
    {
        public static void CheckUpdateUI()
        {
            string download_url;
            string net_version;

            int func_update_source = OptionForm.update_source();

            string error;

            if (func_update_source == 1)
                error = CheckUpdateURLByNightlyLink(out download_url, out net_version);
            else
                error = CheckUpdateURLByRelease(out download_url, out net_version);

            if (error != "")
            {
                if (net_version != "")
                {//バージョンが取れたということは、現在のが最新
                    OverradeLastUpdateTime();
                    R.ShowOK(error);
                    return;
                }
                else
                {//何かエラーが発生
                    R.ShowStopError(error);
                    return;
                }
            }
            CheckUpdateUI(download_url, net_version);
        }
        static void CheckUpdateUI(string download_url, string net_version)
        {
            //まずアップデートした日付を記録する
            OverradeLastUpdateTime();

            //確認ダイアログの表示
            ToolUpdateDialogForm f = (ToolUpdateDialogForm)InputFormRef.JumpFormLow<ToolUpdateDialogForm>();
            f.Init(net_version,download_url);
            f.ShowDialog();
        }

        public class UpdateEventArgs : EventArgs
        {
            public string error;
            public string download_url;
            public string net_version;
        }
        public static void CheckUpdateUI(UpdateEventArgs e)
        {
            if (e.error != "")
            {
                if (e.net_version != "")
                {//バージョンが取れたということは、現在のが最新
                    OverradeLastUpdateTime();
                    return;
                }
                else
                {//何かエラーが発生
                    return;
                }
            }
            
            //開いているフォームの中にUpdateDialogFormはすでにあるか？
            for (int i = 0; i < Application.OpenForms.Count; i++)
            {
                Form f = Application.OpenForms[i];
                if (f.Name == "UpdateDialogForm")
                {//すでにアップデート処理中
                    return;
                }
            }

            CheckUpdateUI(e.download_url, e.net_version);
        }

        static void OverradeLastUpdateTime()
        {
            string yyyymmdd = DateTime.Now.ToString("yyyyMMdd");
            Program.Config["LastUpdateCheck"] = yyyymmdd;
            //Configのセーブ.
            Program.Config.Save();
        }
        //自動アップデート確認をしてもいいの?
        public static bool IsAutoUpdateTime()
        {
            int func_auto_update = OptionForm.auto_update();
            if (func_auto_update == 0)
            {//自動アップデート確認をしない.
                return false;
            }

            DateTime dt = DateTime.Now.AddDays(-func_auto_update);
            uint now = U.atoi(dt.ToString("yyyyMMdd"));
            uint LastUpdateCheck = U.atoi(Program.Config.at("LastUpdateCheck", "0"));
            if (now <= LastUpdateCheck)
            {//まだアップデート確認する時間じゃない.
                return false;
            }
            return true;
        }

        public EventHandler EventHandler;
        public void CheckUpdateThread()
        {
            if (!IsAutoUpdateTime())
            {//まだアップデート確認する時間じゃない.
                return;
            }

            System.Threading.Thread s1 = new System.Threading.Thread(t =>
            {
                try
                {
                    string download_url;
                    string net_version;
                    string error = CheckUpdateURLByRelease(out download_url, out net_version);

                    UpdateEventArgs args = new UpdateEventArgs();
                    args.error = error;
                    args.download_url = download_url;
                    args.net_version = net_version;

                    if (Application.OpenForms.Count <= 0)
                    {//通知するべきフォームがない.
                        return;
                    }
                    Form f = Application.OpenForms[0];
                    if (f == null || f.IsDisposed )
                    {
                        return;
                    }
                    f.Invoke(EventHandler, new object[] { this, args });
                }
                catch (Exception e)
                {
                    Log.Error(e.ToString());
                    return;
                }
            });
            s1.Start();
        }

        static string CheckUpdateURLByGitHub(out string out_url, out string out_version)
        {
            out_url = "";
            out_version = "";

            string versionString = U.getVersion();
            double version = U.atof(versionString);

            string url = "https://api.github.com/repos/laqieer/FEBuilderGBA/releases/latest";
            string contents;
            try
            {
                contents = U.HttpGet(url);
            }
            catch (Exception e)
            {
#if DEBUG
                R.Error("Webサイトにアクセスできません。 URL:{0} Message:{1}", url, e.ToString());
                throw;
#else
                return R.Error("Webサイトにアクセスできません。 URL:{0} Message:{1}", url, e.ToString());
#endif
            }


            string downloadurl;
            {
                System.Text.RegularExpressions.Match match = RegexCache.Match(contents
                , "\"browser_download_url\": \"(.+)\""
                );
                if (match.Groups.Count < 2)
                {
                    return R._("サイトの結果が期待外でした。\r\n{0}", url) + "\r\n\r\n" 
                        + "browser_download_url not found" + "\r\n"
                        + "contents:\r\n" +  contents + "\r\n"
                        + "match.Groups:\r\n" +  U.var_dump(match.Groups);
                }
                downloadurl = match.Groups[1].Value;
            }

            {
                System.Text.RegularExpressions.Match match = RegexCache.Match(contents
                , "download/ver_([0-9.]+)/"
                );
                if (match.Groups.Count < 2)
                {
                    return R._("サイトの結果が期待外でした。\r\n{0}", url) + "\r\n\r\n" 
                        + "download/ver_ not found" + "\r\n"
                        + "contents:\r\n" + contents + "\r\n"
                        + "match.Groups:\r\n" + U.var_dump(match.Groups);
                }
                out_version = match.Groups[1].Value;

                double net_version = U.atof(out_version);
                if (version >= net_version)
                {
                    if (net_version == 0)
                    {
                        return R._("サイトの結果が期待外でした。\r\n{0}", url) + "\r\n\r\n" 
                            + "version can not parse" + "\r\n"
                            + "contents:\r\n" + contents + "\r\n"
                            + "match.Groups:\r\n" + U.var_dump(match.Groups);
                    }
                    return R._("現在のバージョンが最新です。version:{0}", version);
                }
            }

            out_url = downloadurl;
            return "";
        }

        // doc: https://gitee.com/api/v5/swagger#/getV5ReposOwnerRepoReleasesLatest
        public class GiteeReleaseAsset
        {
            public string Name { get; set; }
            public string Browser_download_url { get; set; }
        }
        public class GiteeRelease
        {
            public string Tag_name { get; set; }
            public string Name { get; set; }
            public GiteeReleaseAsset[] Assets { get; set; }
        }
        static string CheckUpdateURLByGitee(out string out_url, out string out_version)
        {
            out_url = "";
            out_version = "";
            string versionString = U.getVersion();
            double version = U.atof(versionString);
            string url = "https://gitee.com/api/v5/repos/laqieer/FEBuilderGBA/releases/latest";
            string contents;
            try
            {
                contents = U.HttpGet(url);
            }
            catch (Exception e)
            {
#if DEBUG
                R.Error("Webサイトにアクセスできません。 URL:{0} Message:{1}", url, e.ToString());
                throw;
#else
                return R.Error("Webサイトにアクセスできません。 URL:{0} Message:{1}", url, e.ToString());
#endif
            }
            // parse contents as json using System.Text.Json
            try
            {
                GiteeRelease release = JsonSerializer.Deserialize<GiteeRelease>(contents, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true // Allows case-insensitive matching of JSON properties
                });
                if (release == null)
                {
                    return R._("サイトの結果が期待外でした。\r\n{0}", url) + "\r\n\r\n"
                        + "json can not parse" + "\r\n"
                        + "contents:\r\n" + contents;
                }
                System.Text.RegularExpressions.Match match = RegexCache.Match(release.Name
                , "ver_([0-9.]+)"
                );
                if (match.Groups.Count < 2)
                {
                    System.Text.RegularExpressions.Match match2 = RegexCache.Match(release.Tag_name
                    , "ver_([0-9.]+)"
                    );
                    if (match2.Groups.Count < 2)
                    {
                        return R._("サイトの結果が期待外でした。\r\n{0}", url) + "\r\n\r\n"
                        + "ver_ not found" + "\r\n"
                        + "name:\r\n" + release.Name + "\r\n"
                        + "tag_name:\r\n" + release.Tag_name + "\r\n";
                    }
                    out_version = match2.Groups[1].Value;
                }
                else
                {
                    out_version = match.Groups[1].Value;
                }
                double net_version = U.atof(out_version);
                if (version >= net_version)
                {
                    if (net_version == 0)
                    {
                        return R._("サイトの結果が期待外でした。\r\n{0}", url) + "\r\n\r\n"
                            + "version can not parse" + "\r\n"
                            + "contents:\r\n" + contents;
                    }
                    return R._("現在のバージョンが最新です。version:{0}", version);
                }
                foreach (GiteeReleaseAsset asset in release.Assets)
                {
                    if (asset.Name.EndsWith(".7z"))
                    {
                        out_url = asset.Browser_download_url;
                        break;
                    }
                }
                if (out_url == "")
                {
                    return R._("サイトの結果が期待外でした。\r\n{0}", url) + "\r\n\r\n"
                        + ".7z not found" + "\r\n"
                        + "contents:\r\n" + contents;
                }
                return "";
            }
            catch (JsonException e)
            {
                return R._("サイトの結果が期待外でした。\r\n{0}", url) + "\r\n\r\n"
                    + "json can not parse" + "\r\n"
                    + "contents:\r\n" + contents + "\r\n"
                    + "error:\r\n" + e.ToString();
            }
        }

        static bool UseChinaMainlandMirror()
        {
            int func_release_source = OptionForm.release_source();
            string lang = OptionForm.lang();
            return func_release_source == 2 || (func_release_source == 0 && lang == "zh");
        }

        static string CheckUpdateURLByRelease(out string out_url, out string out_version)
        {
            // Initialize out parameters to avoid CS0177 errors
            out_url = string.Empty;
            out_version = string.Empty;

            if (UseChinaMainlandMirror())
            {
                // Gitee
                return CheckUpdateURLByGitee(out out_url, out out_version);
            }
            else
            {
                // GitHub
                return CheckUpdateURLByGitHub(out out_url, out out_version);
            }
        }

        static string CheckUpdateURLByNightlyLink(out string out_url, out string out_version)
        {
            out_url = "";
            out_version = "";

            string versionString = U.getVersion();
            double version = U.atof(versionString);

            string url = "https://nightly.link/laqieer/FEBuilderGBA/workflows/msbuild/master";
            string contents;
            try
            {
                contents = U.HttpGet(url);
            }
            catch (Exception e)
            {
#if DEBUG
                R.Error("Webサイトにアクセスできません。 URL:{0} Message:{1}", url, e.ToString());
                throw;
#else
                return R.Error("Webサイトにアクセスできません。 URL:{0} Message:{1}", url, e.ToString());
#endif
            }

            {
                System.Text.RegularExpressions.Match match = RegexCache.Match(contents
                , url + "/FEBuilderGBA_([0-9.]+).zip"
                );
                if (match.Groups.Count < 2)
                {
                    return R._("サイトの結果が期待外でした。\r\n{0}", url) + "\r\n\r\n"
                        + "nightly build not found" + "\r\n"
                        + "contents:\r\n" + contents + "\r\n"
                        + "match.Groups:\r\n" + U.var_dump(match.Groups);
                }
                out_version = match.Groups[1].Value;

                double net_version = U.atof(out_version);
                if (version >= net_version)
                {
                    if (net_version == 0)
                    {
                        return R._("サイトの結果が期待外でした。\r\n{0}", url) + "\r\n\r\n"
                            + "version can not parse" + "\r\n"
                            + "contents:\r\n" + contents + "\r\n"
                            + "match.Groups:\r\n" + U.var_dump(match.Groups);
                    }
                    return R._("現在のバージョンが最新です。version:{0}", version);
                }
            }

            out_url = url + "/FEBuilderGBA_" + out_version + ".zip";
            return "";
        }

        public class GiteeGoArtifact
        {
            public int Id { get; set; }
            public string ActiveStatusEnum { get; set; }
        }
        public class GiteeGoArtifactsResponse
        {
            public int Code { get; set; }
            public string Message { get; set; }
            public string Error { get; set; }
            public GiteeGoArtifact[] Data { get; set; }
        }
        public class GiteeGoArtifactDetailDownloadInfo
        {
            public string DownloadUrl { get; set; }
            public string Token { get; set; }
        }
        public class GiteeGoArtifactDetail
        {
            public int Id { get; set; }
            public string ActiveStatusEnum { get; set; }
            public string UpLoadTime { get; set; }
            public GiteeGoArtifactDetailDownloadInfo DownLoadUrlVo { get; set; }
        }
        public class GiteeGoArtifactDetailResponse
        {
            public int Code { get; set; }
            public string Message { get; set; }
            public string Error { get; set; }
            public GiteeGoArtifactDetail Data { get; set; }
        }
        static string CheckUpdateURLByGiteeGo(out string out_url, out string out_version)
        {
            out_url = "";
            out_version = "";
            string versionString = U.getVersion();
            double version = U.atof(versionString);
            // fetch list of artifacts
            string url = "https://go-repo.gitee.com/laqieer/FEBuilderGBA/gitee-go/artifact-repo/rest/v1/for-pipe/list-published-artis?pageIndex=1&pageSize=10";
            string contents;
            try
            {
                contents = U.HttpGet(url);
            }
            catch (Exception e)
            {
#if DEBUG
                R.Error("Webサイトにアクセスできません。 URL:{0} Message:{1}", url, e.ToString());
                throw;
#else
                return R.Error("Webサイトにアクセスできません。 URL:{0} Message:{1}", url, e.ToString());
#endif
            }
            // parse contents as json using System.Text.Json
            try
            {
                GiteeGoArtifactsResponse giteeGoArtifactsResponse = JsonSerializer.Deserialize<GiteeGoArtifactsResponse>(contents, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true // Allows case-insensitive matching of JSON properties
                });
                if (giteeGoArtifactsResponse == null || giteeGoArtifactsResponse.Data == null || giteeGoArtifactsResponse.Data.Length == 0)
                {
                    return R._("サイトの結果が期待外でした。\r\n{0}", url) + "\r\n\r\n"
                        + "json can not parse" + "\r\n"
                        + "contents:\r\n" + contents;
                }
                // find the latest artifact whose ActiveStatusEnum is "ACTIVE"
                GiteeGoArtifact artifact = giteeGoArtifactsResponse.Data
                    .Where(a => a.ActiveStatusEnum == "ACTIVE")
                    .OrderByDescending(a => a.Id)
                    .FirstOrDefault();
                if (artifact == null)
                {
                    return R._("サイトの結果が期待外でした。\r\n{0}", url) + "\r\n\r\n"
                        + "active artifact not found" + "\r\n"
                        + "artifacts:\r\n" + giteeGoArtifactsResponse.Data.ToString();
                }
                // fetch detail of latest artifact
                url = $"https://go-repo.gitee.com/laqieer/FEBuilderGBA/gitee-go/artifact-repo/rest/v1/for-pipe/{artifact.Id}/get-published-detail";
                try
                {
                    contents = U.HttpGet(url);
                }
                catch (Exception ee)
                {
#if DEBUG
                    R.Error("Webサイトにアクセスできません。 URL:{0} Message:{1}", url, ee.ToString());
                    throw;
#else
                    return R.Error("Webサイトにアクセスできません。 URL:{0} Message:{1}", url, ee.ToString());
#endif
                }
                // parse contents as json using System.Text.Json
                GiteeGoArtifactDetailResponse giteeGoArtifactDetailResponse = JsonSerializer.Deserialize<GiteeGoArtifactDetailResponse>(contents, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true // Allows case-insensitive matching of JSON properties
                });
                if (giteeGoArtifactDetailResponse == null || giteeGoArtifactDetailResponse.Data == null)
                {
                    return R._("サイトの結果が期待外でした。\r\n{0}", url) + "\r\n\r\n"
                        + "json can not parse" + "\r\n"
                        + "contents:\r\n" + contents;
                }
                // parse version from "upLoadTime": "2025-04-23 00:51:01"
                System.Text.RegularExpressions.Match match = RegexCache.Match(giteeGoArtifactDetailResponse.Data.UpLoadTime
                    , "([0-9]{4})-([0-9]{2})-([0-9]{2}) ([0-9]{2}):([0-9]{2}):([0-9]{2})");
                if (match.Groups.Count < 7)
                {
                    return R._("サイトの結果が期待外でした。\r\n{0}", url) + "\r\n\r\n"
                        + "fail to parse version from upLoadTime" + "\r\n"
                        + "upLoadTime:\r\n" + giteeGoArtifactDetailResponse.Data.UpLoadTime + "\r\n"
                        + "match.Groups:\r\n" + U.var_dump(match.Groups);
                }
                out_version = $"{match.Groups[1].Value}{match.Groups[2].Value}{match.Groups[3].Value}.{match.Groups[4].Value}";
                double net_version = U.atof(out_version);
                if (version >= net_version)
                {
                    if (net_version == 0)
                    {
                        return R._("サイトの結果が期待外でした。\r\n{0}", url) + "\r\n\r\n"
                            + "version can not parse" + "\r\n"
                            + "contents:\r\n" + contents;
                    }
                    return R._("現在のバージョンが最新です。version:{0}", version);
                }
                // parse download url
                out_url = giteeGoArtifactDetailResponse.Data.DownLoadUrlVo.DownloadUrl;
                if (string.IsNullOrEmpty(out_url))
                {
                    return R._("サイトの結果が期待外でした。\r\n{0}", url) + "\r\n\r\n"
                        + "download url not found" + "\r\n"
                        + "contents:\r\n" + contents;
                }
                return "";
            }
            catch (JsonException e)
            {
                return R._("サイトの結果が期待外でした。\r\n{0}", url) + "\r\n\r\n"
                    + "json can not parse" + "\r\n"
                    + "contents:\r\n" + contents + "\r\n"
                    + "error:\r\n" + e.ToString();
            }
        }

        static string CheckUpdateURLByCI(out string out_url, out string out_version)
        {
            // Initialize out parameters to avoid CS0177 errors
            out_url = string.Empty;
            out_version = string.Empty;

            if (UseChinaMainlandMirror())
            {
                // Gitee: it doesn't work because you need to login first to access the artifacts of Gitee Go
                return CheckUpdateURLByGiteeGo(out out_url, out out_version);
            }
            else
            {
                // GitHub
                return CheckUpdateURLByNightlyLink(out out_url, out out_version);
            }
        }

        static string CheckUpdateURLByGetUploader(out string out_url, out string out_version)
        {
            out_url = "";
            out_version = "";

            string versionString = U.getVersion();
            double version = U.atof(versionString);

            string url = "https://ux.getuploader.com/FE4/";
            string contents;
            try
            {
                contents = U.HttpGet(url);
            }
            catch (Exception e)
            {
#if DEBUG
                R.Error("Webサイトにアクセスできません。 URL:{0} Message:{1}", url, e.ToString());
                throw;
#else
                return R.Error("Webサイトにアクセスできません。 URL:{0} Message:{1}", url, e.ToString());
#endif
            }

            System.Text.RegularExpressions.Match match = RegexCache.Match(contents
            , "<td><a href=\"(https://ux.getuploader.com/FE4/download/[0-9]+)\" title=\"FEBuilder(?:GBA)?_[0-9.]+.7z\">FEBuilder(?:GBA)?_([0-9.]+).7z</a></td><td></td><td>FEBuilder(?:GBA)?_[0-9.]+.7z</td><td>(2|3|4)\\.[0-9] MB</td>"
            );
            Log.Error(U.var_dump(match.Groups));
            if (match.Groups.Count < 2)
            {
                return R._("サイトの結果が期待外でした。\r\n{0}", url) + "\r\n\r\n"
                    + "href can not parse" + "\r\n"
                    + "contents:\r\n" + contents + "\r\n"
                    + "match.Groups:\r\n" + U.var_dump(match.Groups);
            }

            out_version = match.Groups[2].Value;
            double net_version = U.atof(match.Groups[2].Value);
            if (version >= net_version)
            {
                if (net_version == 0)
                {
                    return R._("サイトの結果が期待外でした。\r\n{0}", url) + "\r\n\r\n"
                        + "version can not parse" + "\r\n"
                        + "contents:\r\n" + contents + "\r\n"
                        + "match.Groups:\r\n" + U.var_dump(match.Groups);
                }
                return R._("現在のバージョンが最新です。version:{0}", version);
            }

            double yyyymmdd = U.atof(DateTime.Now.AddDays(3).ToString("yyyyMMdd.HH"));
            if (net_version > yyyymmdd)
            {//いたずらで変な日付のものが挙げられた可能性あり
                return R._("サイトの結果が期待外でした。\r\n{0}", url) + "\r\n\r\n"
                    + "date can not parse" + "\r\n"
                    + "contents:\r\n" + contents + "\r\n"
                    + "match.Groups:\r\n" + U.var_dump(match.Groups);
            }

            out_url = match.Groups[1].Value;
            return "";
        }

    }
}
