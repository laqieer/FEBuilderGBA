using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Xml;
using System.IO;

namespace FEBuilderGBA
{
    public class Config : Dictionary<string, string>
    {
        public void Save()
        {
            Save(this.ConfigFilename);
        }

        public void Save(string fullfilename)
        {
            try
            {
                SaveOrThrow(fullfilename);
            }
            catch (Exception e)
            {
                R.ShowStopError("設定ファイルに書き込めません。\r\n{0}\r\n{1}", fullfilename, e.ToString());
            }
            return;
        }

        /// <summary>
        /// Persist this config while allowing the caller to observe write failures. Legacy
        /// <see cref="Save(string)"/> keeps its UI-reporting, exception-swallowing behavior;
        /// explicit transactional workflows should use this method and publish success only
        /// after it returns. The target is replaced only after a sibling temp file is fully
        /// written and flushed.
        /// </summary>
        public void SaveOrThrow(string fullfilename)
        {
            SaveOrThrow(fullfilename, CancellationToken.None);
        }

        /// <summary>
        /// Persist this config unless cancellation is observed before the atomic replacement
        /// begins. Cancellation after replacement starts is intentionally too late to avoid
        /// creating disk/live divergence in transactional callers.
        /// </summary>
        public void SaveOrThrow(string fullfilename, CancellationToken cancellationToken)
        {
            SaveOrThrow(fullfilename, cancellationToken, ReplaceFile, afterTempFileFlushed: null);
        }

        /// <summary>Internal replacement seam for deterministic fault-injection tests.</summary>
        internal void SaveOrThrow(string fullfilename, Action<string, string> replaceFile)
        {
            SaveOrThrow(fullfilename, CancellationToken.None, replaceFile, afterTempFileFlushed: null);
        }

        /// <summary>Internal flush-boundary seam for deterministic cancellation tests.</summary>
        internal void SaveOrThrow(
            string fullfilename,
            CancellationToken cancellationToken,
            Action<string, string> replaceFile,
            Action<string> afterTempFileFlushed)
        {
            if (replaceFile == null) throw new ArgumentNullException(nameof(replaceFile));
            cancellationToken.ThrowIfCancellationRequested();

            //XMLシリアライザが初期化できないので自前でやる.
            XmlDocument xml = new XmlDocument();
            XmlElement elem = xml.CreateElement("root");
            xml.AppendChild(elem);

            foreach (var pair in this)
            {
                XmlElement item_elem = xml.CreateElement("item");
                elem.AppendChild(item_elem);

                XmlElement key_elem = xml.CreateElement("key");
                item_elem.AppendChild(key_elem);

                XmlNode item_node = xml.CreateNode(XmlNodeType.Text, "", "");
                item_node.Value = pair.Key;
                key_elem.AppendChild(item_node);

                XmlElement value_elem = xml.CreateElement("value");
                item_elem.AppendChild(value_elem);

                item_node = xml.CreateNode(XmlNodeType.Text, "", "");
                item_node.Value = pair.Value;
                value_elem.AppendChild(item_node);
            }

            string fullPath = Path.GetFullPath(fullfilename);
            string directory = Path.GetDirectoryName(fullPath);
            string tempPath = Path.Combine(
                directory,
                ".fegba-config-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var stream = new FileStream(
                    tempPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None))
                {
                    using (var writer = new StreamWriter(
                        stream,
                        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                        bufferSize: 1024,
                        leaveOpen: true))
                    {
                        xml.Save(writer);
                        writer.Flush();
                    }
                    stream.Flush(flushToDisk: true);
                }

                // The old config remains intact until the complete sibling temp file has been
                // flushed. Cancellation linearizes immediately before replacement; a failed
                // move also leaves the old target in place.
                afterTempFileFlushed?.Invoke(tempPath);
                cancellationToken.ThrowIfCancellationRequested();
                replaceFile(tempPath, fullPath);
            }
            finally
            {
                DeleteTempFileBestEffort(tempPath);
            }
        }

        static void ReplaceFile(string tempPath, string fullPath)
        {
            File.Move(tempPath, fullPath, overwrite: true);
        }

        static void DeleteTempFileBestEffort(string tempPath)
        {
            try
            {
                File.Delete(tempPath);
            }
            catch (IOException ex)
            {
                Log.Debug($"Config.SaveOrThrow: could not remove temporary file '{tempPath}'. {ex}");
            }
            catch (UnauthorizedAccessException ex)
            {
                Log.Debug($"Config.SaveOrThrow: could not remove temporary file '{tempPath}'. {ex}");
            }
            catch (System.Security.SecurityException ex)
            {
                Log.Debug($"Config.SaveOrThrow: could not remove temporary file '{tempPath}'. {ex}");
            }
        }
        public string ConfigFilename { get; protected set; }

        public void Load(string fullfilename)
        {
            this.ConfigFilename = fullfilename;
            if (!System.IO.File.Exists(fullfilename))
            {
                return;
            }

            try
            {
                //XMLシリアライザが初期化できないので自前でやる.
                using (StreamReader r = new StreamReader(fullfilename))
                {
                    XmlDocument xml = new XmlDocument();
                    xml.Load(r);

                    XmlElement elem = xml.DocumentElement;
                    foreach (XmlNode node in elem.SelectNodes("item"))
                    {
                        XmlNode key = node.SelectSingleNode("key");
                        XmlNode value = node.SelectSingleNode("value");
                        this[key.InnerText] = value.InnerText;
                    }
                }
            }
            catch (Exception e)
            {
                R.ShowStopError("設定ファイルが壊れています。\r\n設定ファイルを利用せずに、ディフォルトの設定で起動させます。\r\nこのエラーが何度も出る場合は、連絡してください。\r\n\r\n{0}\r\n{1}", fullfilename, e.ToString());
            }
        }

        public string at(string key, string def = "")
        {
            string r;
            if (this.TryGetValue(key, out r))
            {
                return r;
            }
            //設定されていないっぽい
            return def;
        }

        /// <summary>
        /// #1799: create a usable <see cref="Config"/> for <paramref name="fullfilename"/>
        /// whether or not the file exists yet. Ensures the parent directory exists (so a
        /// first-run <see cref="Save()"/> can create it) and sets <see cref="ConfigFilename"/>
        /// via <see cref="Load(string)"/>. Both the Avalonia GUI (App.axaml.cs) and the CLI
        /// (RomLoader) startup use this so <c>CoreState.Config</c> is never null on a fresh
        /// install — previously a <c>File.Exists</c> guard left it null, and every config
        /// writer (Options dialog, theme, recent files, init wizard) silently no-op'd.
        /// </summary>
        public static Config LoadOrCreate(string fullfilename)
        {
            try
            {
                string dir = Path.GetDirectoryName(fullfilename);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }
            }
            catch (Exception ex)
            {
                // Non-fatal: Save() is itself exception-safe and surfaces a clear error
                // if the location is genuinely unwritable. Log once so a first-run
                // directory-creation failure is diagnosable from the logs.
                Log.Error("Config.LoadOrCreate: could not ensure config directory for", fullfilename, ex.ToString());
            }

            var config = new Config();
            config.Load(fullfilename); // sets ConfigFilename; no-op when the file is absent
            return config;
        }
    }
}
