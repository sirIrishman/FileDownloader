using HtmlAgilityPack;
using Newtonsoft.Json;
using NLog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace FileDownloader {
    public static class Program {
        static readonly string _stateFileName = "state.json";
        static readonly string _currentDir = Directory.GetCurrentDirectory();
        static readonly Logger _logger = LogManager.GetLogger(nameof(Program));

        public static void Main () {
            try {
                if(File.Exists(AppSettings.DownloadTasksFileName)) {
                    var downloadTasks = JsonConvert.DeserializeObject<DownloadFileTask[]>(File.ReadAllText(AppSettings.DownloadTasksFileName));
                    var prevStatesByUrl = DownloadFileState.ReadStatesByUrlFromFile(_stateFileName);
                    foreach(var task in downloadTasks) {
                        task.Url = ResolveUrl(task);
                        task.DownloadState = prevStatesByUrl.ContainsKey(task.Url)
                            ? prevStatesByUrl[task.Url]
                            : new DownloadFileState { Url = task.Url, IsNewVersionAvailable = true };
                    }

                    Task<DownloadFileTask>[] activeDownloadTasks = downloadTasks
                        .Select(state => Task.Factory
                            .StartNew(_ => {
                                var task = (DownloadFileTask)_;
                                task.DownloadState = RetrieveCurrentState(task.DownloadState);
                                return task;
                            }, state)
                            .ContinueWith(_ => DownloadIfAvailable(_.Result), TaskContinuationOptions.OnlyOnRanToCompletion)
                            .ContinueWith(_ => ExecuteFileActionIfAvailable(_.Result), TaskContinuationOptions.OnlyOnRanToCompletion))
                        .ToArray();
                    Task.WaitAll(activeDownloadTasks);

                    Dictionary<Uri, DownloadFileState> newStates = downloadTasks.ToDictionary(_ => _.DownloadState.Url, _ => _.DownloadState);
                    DownloadFileState.WriteStatesByUrlToFile(_stateFileName, newStates);
                }
            } catch(Exception ex) {
                _logger.Error(ex);
            }
        }

        private static Uri ResolveUrl (DownloadFileTask task) {
            if(string.IsNullOrEmpty(task.XPath)) {
                return task.Url;
            }
            var webPage = new HtmlWeb();
            var html = webPage.Load(task.Url);
            var urlText = html.DocumentNode.SelectSingleNode(task.XPath).GetAttributeValue("href", null);
            var url = Helper.CreateUrl(urlText);
            return url.IsAbsoluteUri
                ? url
                : new Uri(task.Url, url);
        }

        static DownloadFileState RetrieveCurrentState (DownloadFileState prevState) {
            _logger.Info($"[{Path.GetFileName(prevState.Url.LocalPath)}] Checking updates...");
            var headersRequest = WebRequest.CreateDefault(prevState.Url);
            headersRequest.Method = WebRequestMethods.Http.Head;
            try {
                using(WebResponse headersResponse = headersRequest.GetResponse()) {
                    var currentState = DownloadFileState.CreateFromWebResponse(headersRequest.RequestUri, headersResponse);
                    currentState.IsNewVersionAvailable = currentState != prevState;
                    return currentState;
                }
            } catch {
                return prevState;
            }
        }

        static DownloadFileTask DownloadIfAvailable (DownloadFileTask task) {
            try {
                if(task.DownloadState.IsNewVersionAvailable) {
                    _logger.Info($"[{Path.GetFileName(task.DownloadState.Url.LocalPath)}] Update found. Downloading...");
                    var downloadRequest = WebRequest.CreateDefault(task.DownloadState.Url);
                    downloadRequest.Method = WebRequestMethods.Http.Get;
                    using(WebResponse downloadResponse = downloadRequest.GetResponse()) {
                        task.DownloadState.DownloadPath = GetTargetFilePath(downloadRequest.RequestUri);
                        if(File.Exists(task.DownloadState.DownloadPath)) {
                            File.Delete(task.DownloadState.DownloadPath);
                        }
                        using(WebResponse downloadingResponse = downloadRequest.GetResponse())
                        using(Stream downloadStream = downloadingResponse.GetResponseStream())
                        using(var fileStream = new FileStream(task.DownloadState.DownloadPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: /*1 Mb*/1024 * 1024)) {
                            int totalReadBytes = 0;
                            while(true) {
                                var readBuffer = new byte[4096];
                                int readBytes = downloadStream.Read(readBuffer, 0, readBuffer.Length);
                                if(readBytes == 0) {
                                    break;
                                }
                                fileStream.Write(readBuffer, 0, readBytes);
                                totalReadBytes += readBytes;
                            }
                            fileStream.Flush();
                        }
                    }
                    _logger.Info($"[{Path.GetFileName(task.DownloadState.Url.LocalPath)}] File downloaded.");
                } else {
                    _logger.Info($"[{Path.GetFileName(task.DownloadState.Url.LocalPath)}] No updates.");
                }
            } catch(Exception ex) {
                _logger.Error(ex, $"[{Path.GetFileName(task.DownloadState.Url.LocalPath)}] Downloading failed with error: {ex.Message}.");
            }
            return task;
        }

        private static DownloadFileTask ExecuteFileActionIfAvailable (DownloadFileTask task) {
            if(task.DownloadState.IsNewVersionAvailable && task.PostdownloadAction != null) {
                _logger.Info($"[{Path.GetFileName(task.DownloadState.Url.LocalPath)}] Action found. Triggering...");
                task.PostdownloadAction.Trigger(task.DownloadState.DownloadPath);
                _logger.Info($"[{Path.GetFileName(task.DownloadState.Url.LocalPath)}] Action finished.");
            }
            return task;
        }

        static string GetTargetFilePath (Uri sourceFileUri) {
            string targetFileName = Path.Combine(AppSettings.TargetDirectoryPath, Path.GetFileName(sourceFileUri.LocalPath));
            string targetFilePath = Path.Combine(_currentDir, targetFileName);
            return Path.GetFullPath(targetFilePath);
        }
    }

    sealed class DownloadFileTask {
        [JsonRequired]
        public Uri Url { get; set; }

        [JsonProperty("link_xpath")]
        public string XPath { get; set; }

        [JsonProperty("action")]
        public FileAction PostdownloadAction { get; set; }

        [JsonIgnore]
        public DownloadFileState DownloadState { get; set; }

        public override bool Equals (object obj) {
            var another = obj as DownloadFileTask;
            return !(another is null)
                && this.Url == another.Url
                && this.XPath == another.XPath
                && this.PostdownloadAction == another.PostdownloadAction;
        }

        public override int GetHashCode () {
            const int prime = 31;
            int hashcode = 1;
            hashcode = hashcode * prime + Url?.GetHashCode() ?? 0;
            hashcode = hashcode * prime + XPath?.GetHashCode() ?? 0;
            hashcode = hashcode * prime + PostdownloadAction?.GetHashCode() ?? 0;
            return hashcode;
        }
    }

    sealed class FileAction {
        public const string DownloadedFilePathPlaceholder = "{download}";

        [JsonProperty("command")]
        public string RawCommand { get; set; } = DownloadedFilePathPlaceholder;

        [JsonProperty("args")]
        public string RawArguments { get; set; }

        [JsonProperty("keep_file")]
        public bool KeepFile { get; set; } = true;

        public void Trigger (string filePath) {
            // TODO Use smart proccess running (output errors, wait for exit with a timeout)
            var command = RawCommand.Replace(DownloadedFilePathPlaceholder, filePath);
            var arguments = RawArguments.Replace(DownloadedFilePathPlaceholder, filePath);
            var startInfo = new ProcessStartInfo(command, arguments) { WindowStyle = ProcessWindowStyle.Hidden };
            using(var process = Process.Start(startInfo)) {
                process.WaitForExit(Convert.ToInt32(TimeSpan.FromMinutes(10).TotalMilliseconds));
            }
            if(!KeepFile) {
                File.Delete(filePath);
            }
        }

        public override bool Equals (object obj) {
            var another = obj as FileAction;
            return !(another is null)
                && this.RawCommand == another.RawCommand
                && this.RawArguments == another.RawArguments
                && this.KeepFile == another.KeepFile;
        }

        public override int GetHashCode () {
            const int prime = 31;
            int hashcode = 1;
            hashcode = hashcode * prime + RawCommand?.GetHashCode() ?? 0;
            hashcode = hashcode * prime + RawArguments?.GetHashCode() ?? 0;
            hashcode = hashcode * prime + KeepFile.GetHashCode();
            return hashcode;
        }
    }

    sealed class DownloadFileState {
        [JsonRequired]
        public Uri Url { get; set; }

        public string MD5Hash { get; set; }

        public string Length { get; set; }

        public string LastModified { get; set; }

        [JsonIgnore]
        public string DownloadPath { get; set; }

        [JsonIgnore]
        public bool IsNewVersionAvailable { get; set; }

        public override bool Equals (object obj) {
            var another = obj as DownloadFileState;
            return !(another is null)
                && this.Url == another.Url
                && this.MD5Hash == another.MD5Hash
                && this.Length == another.Length
                && this.LastModified == another.LastModified;
        }

        public override int GetHashCode () {
            const int prime = 31;
            int hashcode = 1;
            hashcode = hashcode * prime + Url?.GetHashCode() ?? 0;
            hashcode = hashcode * prime + MD5Hash?.GetHashCode() ?? 0;
            hashcode = hashcode * prime + Length?.GetHashCode() ?? 0;
            hashcode = hashcode * prime + LastModified?.GetHashCode() ?? 0;
            return hashcode;
        }

        public static Dictionary<Uri, DownloadFileState> ReadStatesByUrlFromFile (string stateFileName) {
            if(File.Exists(stateFileName)) {
                string json = File.ReadAllText(stateFileName);
                return JsonConvert.DeserializeObject<Dictionary<Uri, DownloadFileState>>(json);
            }
            return new Dictionary<Uri, DownloadFileState>();
        }

        public static void WriteStatesByUrlToFile (string stateFileName, IDictionary<Uri, DownloadFileState> states) {
            string json = JsonConvert.SerializeObject(states, new JsonSerializerSettings { Formatting = Formatting.Indented });
            File.WriteAllText(stateFileName, json);
        }

        public static DownloadFileState CreateFromWebResponse (Uri requestUrl, WebResponse webResponse) {
            return new DownloadFileState {
                Url = requestUrl,
                MD5Hash = webResponse.Headers["Content-Md5"],
                Length = webResponse.Headers["Content-Length"],
                LastModified = webResponse.Headers["Last-Modified"]
            };
        }

        public static bool operator == (DownloadFileState x, DownloadFileState y) {
            return ReferenceEquals(x, y)
                || !(x is null) && !(y is null) && x.Equals(y);
        }

        public static bool operator != (DownloadFileState x, DownloadFileState y) {
            return !(x == y);
        }
    }
}
