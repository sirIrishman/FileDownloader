using System.Configuration;

namespace FileDownloader {
    static class AppSettings {
        public static string DownloadTasksFileName {
            get { return ConfigurationManager.AppSettings["downloadTasksFileName"]; }
        }
        public static string TargetDirectoryPath {
            get { return ConfigurationManager.AppSettings["targetDirectoryPath"]; }
        }
    }
}
