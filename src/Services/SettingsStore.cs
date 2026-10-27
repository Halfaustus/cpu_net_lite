using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using cpu_net.Model;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace cpu_net.Services
{
    public sealed class SettingsStore
    {
        private static readonly object Gate = new object();
        private static readonly IDeserializer Deserializer = new DeserializerBuilder()
            .WithNamingConvention(PascalCaseNamingConvention.Instance).Build();
        private static readonly ISerializer Serializer = new SerializerBuilder()
            .WithNamingConvention(PascalCaseNamingConvention.Instance).Build();
        private static readonly System.Reflection.PropertyInfo[] Properties = typeof(SettingModel)
            .GetProperties().Where(p => p.CanRead && p.CanWrite).ToArray();
        private static readonly Dictionary<string, (DateTime Stamp, long Length, SettingModel Value)> Cache
            = new(StringComparer.OrdinalIgnoreCase);
        private readonly string _path;

        public SettingsStore(string? path = null)
        {
            _path = Path.GetFullPath(path ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.yaml"));
        }

        public SettingModel Read(bool force = false)
        {
            lock (Gate)
            {
                try { return ReadCore(force); }
                catch (YamlDotNet.Core.YamlException) { return new SettingModel(); }
            }
        }

        private static SettingModel Copy(SettingModel source)
        {
            var copy = new SettingModel();
            foreach (var property in Properties) property.SetValue(copy, property.GetValue(source));
            return copy;
        }

        private SettingModel ReadCore(bool force = false)
        {
            var file = new FileInfo(_path);
            if (!file.Exists) { Cache.Remove(_path); return new SettingModel(); }
            if (!force && Cache.TryGetValue(_path, out var cached) &&
                cached.Stamp == file.LastWriteTimeUtc && cached.Length == file.Length)
                return Copy(cached.Value);
            var value = Deserializer.Deserialize<SettingModel>(File.ReadAllText(_path)) ?? new SettingModel();
            Cache[_path] = (file.LastWriteTimeUtc, file.Length, value);
            return Copy(value);
        }

        // 每次保存均以磁盘中的最新配置为基础，只应用当前模块的修改。
        public bool Update(Func<SettingModel, bool> update)
        {
            lock (Gate)
            {
                var settings = ReadCore(force: true);
                if (!update(settings)) return false;
                WriteCore(settings);
                return true;
            }
        }

        public void Save(SettingModel settings)
        {
            lock (Gate) WriteCore(settings);
        }

        private void WriteCore(SettingModel settings)
        {
            string yaml = Serializer.Serialize(settings);
            string temp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, yaml);
                File.Move(temp, _path, overwrite: true);
                var file = new FileInfo(_path);
                Cache[_path] = (file.LastWriteTimeUtc, file.Length, Copy(settings));
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }
    }
}
