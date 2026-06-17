using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace FEBuilderGBA
{
    /// <summary>
    /// GUI-free port of WinForms <c>ToolWorkSupportForm.CheckUpdateLow</c> (#1196):
    /// decides whether a work-support project has a newer version available by
    /// fetching its CHECK_URL, extracting a date via CHECK_REGEX, and comparing it
    /// to the local ROM's timestamp.
    ///
    /// <para>The network and filesystem touches are injected as delegates so the
    /// decision logic is fully testable offline. The Avalonia host wires real
    /// implementations (<c>U.HttpGet</c>, an HTTP HEAD Last-Modified probe, and the
    /// ROM/UPS file timestamp).</para>
    /// </summary>
    public static class WorkSupportUpdateCheckCore
    {
        public enum UpdateResult
        {
            Error,
            Latest,
            Updateable,
        }

        /// <summary>
        /// Determine update availability from the parsed update-info
        /// <paramref name="lines"/>. Returns <see cref="UpdateResult.Error"/> when
        /// required keys are missing, <see cref="UpdateResult.Updateable"/> when the
        /// remote date is newer than the ROM, else <see cref="UpdateResult.Latest"/>.
        /// Never throws.
        /// </summary>
        /// <param name="lines">Parsed <c>.updateinfo.txt</c> key/value pairs.</param>
        /// <param name="httpGet">Fetches CHECK_URL HTML (throws on network error).</param>
        /// <param name="httpHeadLastModified">Returns a URL's Last-Modified header, or null.</param>
        /// <param name="romDateTime">Returns the local ROM/UPS timestamp.</param>
        public static UpdateResult Check(
            Dictionary<string, string> lines,
            string romFilename,
            Func<string, string> httpGet,
            Func<string, string> httpHeadLastModified,
            Func<string, DateTime> romDateTime)
        {
            try
            {
                if (lines == null)
                {
                    return UpdateResult.Error;
                }

                string url = U.at(lines, "CHECK_URL");
                if (url == "")
                {
                    return UpdateResult.Error;
                }

                string regex = U.at(lines, "CHECK_REGEX");
                if (regex == "")
                {
                    return UpdateResult.Error;
                }

                string dateString;
                string match;
                if (regex == "@DIRECT_URL")
                {
                    match = url;
                }
                else
                {
                    string html;
                    try
                    {
                        html = httpGet != null ? httpGet(url) : "";
                    }
                    catch (Exception)
                    {
                        // Mirror WF: a fetch error is treated as "latest" (no update).
                        return UpdateResult.Latest;
                    }

                    Match m = RegexCache.Match(html, regex);
                    if (m.Groups.Count < 2)
                    {
                        return UpdateResult.Error;
                    }
                    match = m.Groups[1].ToString();
                }

                if (IsUrl(match))
                {
                    string lastModified;
                    try
                    {
                        lastModified = httpHeadLastModified != null ? httpHeadLastModified(url) : null;
                    }
                    catch (Exception)
                    {
                        return UpdateResult.Latest;
                    }

                    dateString = lastModified ?? DateTime.Now.ToString();
                }
                else
                {
                    dateString = match;
                }

                DateTime datetime;
                try
                {
                    datetime = ConvertDateTime(url, dateString);
                }
                catch (Exception)
                {
                    datetime = DateTime.Now;
                }

                DateTime romDt;
                try
                {
                    romDt = romDateTime != null ? romDateTime(romFilename) : DateTime.Now;
                }
                catch (Exception)
                {
                    return UpdateResult.Error;
                }

                return romDt < datetime ? UpdateResult.Updateable : UpdateResult.Latest;
            }
            catch (Exception)
            {
                return UpdateResult.Error;
            }
        }

        /// <summary>Ports <c>U.isURL</c>: starts with http:// or https://.</summary>
        public static bool IsUrl(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            return RegexCache.IsMatch(text, "^https?://");
        }

        /// <summary>Ports WF <c>ToolWorkSupportForm.ConvretDateTime</c>.</summary>
        public static DateTime ConvertDateTime(string url, string dateString)
        {
            DateTime datetime;
            if (!string.IsNullOrEmpty(url) && url.IndexOf("getuploader.com", StringComparison.Ordinal) >= 0)
            {
                return DateTime.Parse(dateString, new CultureInfo("ja-JP", false));
            }
            if (TryParseUnixTime(dateString, out datetime))
            {
                return datetime;
            }
            if (dateString.Length <= 12)
            {
                if (DateTime.TryParseExact(dateString, "yyyyMMdd.HH", null, DateTimeStyles.None, out datetime))
                {
                    return datetime;
                }
            }
            if (dateString.Length <= 8)
            {
                if (DateTime.TryParseExact(dateString, "yyyyMMdd", null, DateTimeStyles.None, out datetime))
                {
                    return datetime;
                }
            }
            return DateTime.Parse(dateString);
        }

        /// <summary>Ports <c>U.TryParseUnitTime</c> (unix-epoch seconds since 2010).</summary>
        public static bool TryParseUnixTime(string date, out DateTime retDateTime)
        {
            date = (date ?? "").Trim();
            if (!U.isNumString(date))
            {
                retDateTime = DateTime.Now;
                return false;
            }
            if (date.Length >= 10 + 6)
            {
                date = date.Substring(0, date.Length - 6);
            }
            else if (date.Length >= 10 + 3)
            {
                date = date.Substring(0, date.Length - 3);
            }

            uint dateuint = U.atoi(date);
            if (dateuint < 1262271600)
            {
                retDateTime = DateTime.Now;
                return false;
            }
            DateTime unixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            retDateTime = unixEpoch.AddSeconds(dateuint).ToLocalTime();
            return true;
        }
    }
}
