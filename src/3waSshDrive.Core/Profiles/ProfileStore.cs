using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using ThreeWa.SshDrive.Core.Models;

namespace ThreeWa.SshDrive.Core.Profiles
{
    public sealed class ProfileStore
    {
        private readonly string _filePath;
        private readonly DataContractJsonSerializer _serializer =
            new DataContractJsonSerializer(typeof(ProfileDocument));

        public ProfileStore(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("Profile store path is required.", nameof(filePath));

            _filePath = Path.GetFullPath(filePath);
        }

        public static ProfileStore CreateDefault()
        {
            var applicationData = Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData);
            return new ProfileStore(Path.Combine(
                applicationData,
                "3waSshDrive",
                "profiles.json"));
        }

        public IReadOnlyList<DriveProfile> Load()
        {
            if (!File.Exists(_filePath))
                return Array.Empty<DriveProfile>();

            using (var stream = File.OpenRead(_filePath))
            {
                var document = (ProfileDocument)_serializer.ReadObject(stream);
                return document?.Profiles ?? new List<DriveProfile>();
            }
        }

        public void Save(IEnumerable<DriveProfile> profiles)
        {
            if (profiles == null)
                throw new ArgumentNullException(nameof(profiles));

            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var temporaryPath = _filePath + ".tmp";
            try
            {
                var document = new ProfileDocument
                {
                    Profiles = profiles.ToList()
                };

                using (var stream = new FileStream(
                    temporaryPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None))
                {
                    _serializer.WriteObject(stream, document);
                    stream.Flush(true);
                }

                if (File.Exists(_filePath))
                    File.Replace(temporaryPath, _filePath, null);
                else
                    File.Move(temporaryPath, _filePath);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }

        [DataContract]
        private sealed class ProfileDocument
        {
            [DataMember(Name = "profiles", Order = 1)]
            public List<DriveProfile> Profiles { get; set; } = new List<DriveProfile>();
        }
    }
}
