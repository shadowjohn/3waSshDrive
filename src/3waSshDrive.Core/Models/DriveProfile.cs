using System.Runtime.Serialization;

namespace ThreeWa.SshDrive.Core.Models
{
    [DataContract]
    public sealed class DriveProfile
    {
        [DataMember(Order = 1)]
        public string Name { get; set; }

        [DataMember(Order = 2)]
        public string Host { get; set; }

        [DataMember(Order = 3)]
        public int Port { get; set; } = 22;

        [DataMember(Order = 4)]
        public string Username { get; set; }

        [DataMember(Order = 5)]
        public string RemoteRoot { get; set; } = "/";

        [DataMember(Order = 6)]
        public string DriveLetter { get; set; } = "Z:";

        [DataMember(Order = 7)]
        public AuthenticationMode AuthenticationMode { get; set; } =
            AuthenticationMode.PrivateKey;

        [DataMember(Order = 8)]
        public string PrivateKeyPath { get; set; }

        [DataMember(Order = 9)]
        public string HostKeyFingerprintSha256 { get; set; }

        // 密碼只供目前應用程式執行期間使用，絕不寫入 profiles.json。
        [IgnoreDataMember]
        public string Password { get; set; }
    }
}
