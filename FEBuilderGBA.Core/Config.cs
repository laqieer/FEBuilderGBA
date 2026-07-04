using System;
using System.Collections.Generic;
using System.Text;
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
            try
            {
                using (StreamWriter w = new StreamWriter(fullfilename))
                {
                    xml.Save(w);
                }
            }
            catch (Exception e)
            {
                R.ShowStopError("設定ファイルに書き込めません。\r\n{0}\r\n{1}", fullfilename, e.ToString());
            }
            return;
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
