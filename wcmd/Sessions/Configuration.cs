using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace wcmd.Sessions
{
    internal class Configuration
    {
        #region Static

        private static FileInfo GetDefaultConfigFile()
        {
            var path = Path.Combine(DefaultDbDirectory.FullName, Constants.ConfigFileName);
            return new FileInfo(path);
        }

        private static DirectoryInfo DefaultDbDirectory => _defaultDbDirectory.Value;

        private static readonly Lazy<DirectoryInfo> _defaultDbDirectory = new Lazy<DirectoryInfo>(
            GetDefaultDbDirectory
        );

        private static DirectoryInfo GetDefaultDbDirectory()
        {
            var appDataRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(appDataRoot))
                throw new ProgramException("The special folder LocalApplicationData is not specified.");

            Trace.TraceInformation("Environment LocalApplicationData: {0}", appDataRoot);

            var dbDir = new DirectoryInfo(Path.Combine(appDataRoot, Constants.LocalDirectory));
            Trace.TraceInformation("Local database directory: {0}", dbDir);

            if (dbDir.Exists)
                return dbDir;

            Trace.TraceWarning("Local database directory does not exist: {0}", dbDir);
            Trace.TraceInformation("Trying to create local database directory...");
            dbDir.Create();

            return dbDir;
        }

        public static Configuration LoadDefault()
        {
            var configFile = GetDefaultConfigFile();
            if (!configFile.Exists)
                return null;

            return LoadFromFile(configFile);
        }

        private static Configuration LoadFromFile(FileSystemInfo configFile)
        {
            Trace.TraceInformation("Reading configuration file: {0}", configFile);
            if (!configFile.Exists)
            {
                Trace.TraceInformation("Configuration file does not exist.");
                return null;
            }

            ConfigurationData configData;
            using (var stream = new FileStream(configFile.FullName, FileMode.Open))
            {
                configData = Serializer.LoadFromStream<ConfigurationData>(stream);
            }

            return new Configuration(configData);
        }

        public static Configuration CreateDefault()
        {
            var hostName = Environment.MachineName;
            var userName = WindowsIdentity.GetCurrent().Name;
            var random = new Random().Next(0x10000);
            var sessionSeed = $"{hostName}/{userName}/{random:X}";
            var sessionId = CreateSessionId(sessionSeed);

            var configData = new ConfigurationData
            {
                HostName = hostName,
                UserName = userName,
                Random = random,
                SessionId = sessionId,
                WhenCreated = DateTime.Now
            };

            var configFile = GetDefaultConfigFile();

            Trace.TraceInformation("Writing configuration file: {0}", configFile);

            using (var stream = new FileStream(configFile.FullName, FileMode.CreateNew))
            {
                Serializer.SaveToStream(stream, configData);
            }

            return new Configuration(configData);
        }

        private static Guid CreateSessionId(string sessionSeed)
        {
            var bytes = Encoding.UTF8.GetBytes(sessionSeed);
            using (var sha256 = SHA256.Create())
            {
                bytes = sha256.ComputeHash(bytes);
            }

            var guidBytes = new byte[16];
            Array.Copy(bytes, 0, guidBytes, 0, guidBytes.Length);

            return new Guid(guidBytes);
        }

        #endregion

        private Configuration(ConfigurationData configData)
        {
            SessionId = configData.SessionId;
        }

        public Guid SessionId { get; }
    }


    public class ConfigurationData
    {
        public Guid SessionId;
        public string HostName;
        public string UserName;
        public int Random;
        public DateTime WhenCreated;
    }
}