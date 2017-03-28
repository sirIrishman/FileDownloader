using System.Configuration;

namespace FileDownloader {
    static class AppSettings {
        public static string AddressFileName {
            get { return ConfigurationManager.AppSettings["addressesFileName"]; }
        }
        public static string TargetFilePath {
            get { return ConfigurationManager.AppSettings["targetFilePath"]; }
        }
    }
}
