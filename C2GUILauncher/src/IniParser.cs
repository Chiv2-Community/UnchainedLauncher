using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace C2GUILauncher
{
    public class IniConfigItem
    {
        public string? DisplayName { get; set; }
        public string? Section { get; set; }
        public string? Key { get; set; }
        public bool DefaultValue { get; set; }
    }

    public class IniParser
    {
        private readonly Dictionary<string, Dictionary<string, IniConfigItem>> _data = new Dictionary<string, Dictionary<string, IniConfigItem>>();
        private readonly Dictionary<string, Dictionary<string, string>> _comments = new Dictionary<string, Dictionary<string, string>>();

        public void Parse(string content)
        {
            string[] lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            string currentSection = string.Empty;
            Dictionary<string, string> currentComments = new Dictionary<string, string>();

            foreach (string line in lines)
            {
                string trimmedLine = line.Trim();

                if (trimmedLine.StartsWith("[") && trimmedLine.EndsWith("]"))
                {
                    // Section line
                    currentSection = trimmedLine.Substring(1, trimmedLine.Length - 2);
                    _data[currentSection] = new Dictionary<string, IniConfigItem>();
                    _comments[currentSection] = new Dictionary<string, string>();
                    currentComments.Clear();
                }
                else if (trimmedLine.Contains("="))
                {
                    // Key-value pair line
                    string[] parts = trimmedLine.Split('=');
                    string key = parts[0].Trim();
                    string[] val = parts[1].Split(';');
                    bool value;
                    if (bool.TryParse(val[0].Trim(), out bool parsedValue))
                    {
                        value = parsedValue;
                    }
                    else
                    {
                        // Handle invalid boolean values here, for example, set value to false
                        value = false;
                    }

                    // Extract comments if they exist
                    int commentIndex = trimmedLine.IndexOf(';');
                    if (commentIndex != -1)
                    {
                        string comment = trimmedLine.Substring(commentIndex + 1).Trim();
                        currentComments[key] = comment;
                    }

                    IniConfigItem configItem = new IniConfigItem
                    {
                        DisplayName = currentComments.ContainsKey(key) ? currentComments[key] : "John Doe",
                        Section = currentSection,
                        Key = key,
                        DefaultValue = value
                    };
                    _data[currentSection][key] = configItem;

                    _comments[currentSection] = new Dictionary<string, string>(currentComments);
                    currentComments.Clear();
                }
                else if (trimmedLine.StartsWith(";") || trimmedLine.StartsWith("#"))
                {
                    // Comment line without key-value pair, ignore it
                }
            }
        }

        public Dictionary<string, IniConfigItem>? GetConfigItems(string section)
        {
            if (_data.ContainsKey(section))
            {
                return _data[section];
            }
            return null;
        }

        public Dictionary<string, Dictionary<string, IniConfigItem>> GetData()
        {
            return _data;
        }

        public string? GetComment(string section, string key)
        {
            if (_comments.ContainsKey(section) && _comments[section].ContainsKey(key))
            {
                return _comments[section][key];
            }
            return null;
        }
    }
}
