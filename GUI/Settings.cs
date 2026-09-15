#if PackAsTool
using System;
using System.Collections.Generic;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace ImapCopy
{
    public enum Command { None, Copy, Update, Backup, Restore };
    public class Settings
    {
        const string DefaultFileName = "imapcopy.settings.json";
        public Command Command { get; set; }
        public string? Source { get; set; }
        public string? Destination { get; set; }

        public void Save(string? filename = null)
        {
            var json = JsonConvert.SerializeObject(this, new JsonSerializerSettings {
                Converters = { new StringEnumConverter() }
            });
            File.WriteAllText(filename ?? DefaultFileName, json);
        }

        public void Load(string? filename = null)
        {
            var json = File.ReadAllText(filename ?? DefaultFileName);
            var settings = JsonConvert.DeserializeObject<Settings>(json, new JsonSerializerSettings
            {
                Converters = { new StringEnumConverter() }
            });
            Command = settings?.Command ?? Command.None;
            Source = settings?.Source;
            Destination = settings?.Destination;
        }
    }
}
#endif