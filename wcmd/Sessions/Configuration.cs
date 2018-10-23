using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using wcmd.DataFiles;
using wcmd.Diagnostics;

namespace wcmd.Sessions
{
    internal class Configuration
    {
        private static readonly TraceSource _trace = DiagnosticsCenter.GetTraceSource( nameof( Configuration ) );

        #region Static

        private static FileInfo GetDefaultConfigFile()
        {
            var path = Path.Combine( DefaultDbDirectory.FullName, Constants.ConfigFileName );
            return new FileInfo( path );
        }

        private static DirectoryInfo DefaultDbDirectory => _defaultDbDirectory.Value;

        private static readonly Lazy<DirectoryInfo> _defaultDbDirectory = new Lazy<DirectoryInfo>(
            GetDefaultDbDirectory
        );

        private static DirectoryInfo GetDefaultDbDirectory()
        {
            var appDataRoot = Environment.GetFolderPath( Environment.SpecialFolder.LocalApplicationData );
            if ( string.IsNullOrWhiteSpace( appDataRoot ) )
                throw new Exception( "The special folder LocalApplicationData is not specified." );

            _trace.TraceInformation( "Environment LocalApplicationData: {0}", appDataRoot );

            var dbDir = new DirectoryInfo( Path.Combine( appDataRoot, Constants.LocalDirectory ) );
            _trace.TraceInformation( "Local database directory: {0}", dbDir );

            if ( dbDir.Exists )
                return dbDir;

            _trace.TraceWarning( "Local database directory does not exist: {0}", dbDir );
            _trace.TraceInformation( "Trying to create local database directory..." );
            dbDir.Create();

            return dbDir;
        }

        public static Configuration LoadDefault()
        {
            var configFile = GetDefaultConfigFile();
            if ( !configFile.Exists )
                return null;

            return LoadFromFile( configFile );
        }

        private static Configuration LoadFromFile( FileInfo configFile )
        {
            _trace.TraceInformation( "Reading configuration file: {0}", configFile );
            if ( !configFile.Exists )
            {
                _trace.TraceInformation( "Configuration file does not exist." );
                return null;
            }

            ConfigurationData configData;
            using ( var stream = new FileStream( configFile.FullName, FileMode.Open ) )
            {
                configData = Serializer.LoadFromStream<ConfigurationData>( stream );
            }

            return new Configuration( configData, configFile );
        }

        public static Configuration CreateDefault()
        {
            var hostName = Environment.MachineName;
            var userName = WindowsIdentity.GetCurrent().Name;
            var random = new Random().Next( 0x10000 );
            var sessionSeed = $"{hostName}/{userName}/{random:X}";
            var sessionId = CreateSessionId( sessionSeed );

            var configData = new ConfigurationData
            {
                HostName = hostName,
                UserName = userName,
                Random = random,
                SessionId = sessionId,
                WhenCreated = DateTime.Now
            };

            var configFile = GetDefaultConfigFile();

            _trace.TraceInformation( "Writing configuration file: {0}", configFile );

            using ( var stream = new FileStream( configFile.FullName, FileMode.CreateNew ) )
            {
                Serializer.SaveToStream( stream, configData );
            }

            return new Configuration( configData, configFile );
        }

        private static Guid CreateSessionId( string sessionSeed )
        {
            var bytes = Encoding.UTF8.GetBytes( sessionSeed );
            using ( var sha256 = SHA256.Create() )
            {
                bytes = sha256.ComputeHash( bytes );
            }

            var guidBytes = new byte[16];
            Array.Copy( bytes, 0, guidBytes, 0, guidBytes.Length );

            return new Guid( guidBytes );
        }

        #endregion

        private Configuration( ConfigurationData configData, FileInfo configFile )
        {
            ConfigFile = configFile;
            SessionId = configData.SessionId;
            SharedDirectory = !string.IsNullOrWhiteSpace( configData.SharedFolder ) ? new DirectoryInfo( configData.SharedFolder ) : null;
        }

        public FileInfo ConfigFile { get; }
        public Guid SessionId { get; }
        public DirectoryInfo SharedDirectory { get; }
        public DirectoryInfo LocalDbDirectory => ConfigFile.Directory;
    }

    public class ConfigurationData
    {
        public Guid SessionId;
        public string HostName;
        public string UserName;
        public string SharedFolder;
        public int Random;
        public DateTime WhenCreated;
    }
}