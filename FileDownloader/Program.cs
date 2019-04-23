using Newtonsoft.Json;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace FileDownloader {
    public class Program {
        static readonly string _stateFileName = "state.json";
        static readonly string _currentDir = Directory.GetCurrentDirectory();
        static readonly Logger _logger = NLog.LogManager.GetLogger(nameof(Program));

        public static void Main () {
            try {
                if(File.Exists(AppSettings.AddressFileName)) {
                    var prevStatesByUrl = DownloadingFileState.ReadStatesByUrlFromFile(_stateFileName);
                    var prevStates = File.ReadAllLines(AppSettings.AddressFileName)
                        .Where(_ => Uri.IsWellFormedUriString(_, UriKind.Absolute))
                        .Select(_ => {
                            var fileUrl = new Uri(_.Trim());
                            return (prevStatesByUrl.ContainsKey(fileUrl))
                                ? prevStatesByUrl[fileUrl]
                                : new DownloadingFileState { Url = fileUrl };
                        })
                        .Distinct()
                        .ToList()
                        .AsReadOnly();

                    Task<DownloadingFileState>[] downloadTasks = prevStates
                        .Select(state => Task.Factory
                            .StartNew(_ => RetrieveCurrentState((DownloadingFileState)_), state)
                            .ContinueWith(_ => DownloadIfAvailable(_.Result), TaskContinuationOptions.OnlyOnRanToCompletion))
                        .ToArray();
                    Task.WaitAll(downloadTasks);

                    Dictionary<Uri, DownloadingFileState> newStates = downloadTasks
                        .Select(_ => _.Result)
                        .ToDictionary(_ => _.Url, _ => _);
                    DownloadingFileState.WriteStatesByUrlToFile(_stateFileName, newStates);
                }
            } catch(Exception ex) {
                _logger.Error(ex);
            }
        }

        static DownloadingFileState RetrieveCurrentState (DownloadingFileState prevState) {
            _logger.Info($"[{System.IO.Path.GetFileName(prevState.Url.LocalPath)}] Checking updates...");
            var headersRequest = WebRequest.CreateDefault(prevState.Url);
            headersRequest.Method = WebRequestMethods.Http.Head;
            using(WebResponse headersResponse = headersRequest.GetResponse()) {
                var currentState = DownloadingFileState.CreateFromWebResponse(headersRequest.RequestUri, headersResponse);
                currentState.IsNewVersionAvailable = currentState != prevState;
                return currentState;
            }
        }

        static DownloadingFileState DownloadIfAvailable (DownloadingFileState currentState) {
            if(currentState.IsNewVersionAvailable) {
                _logger.Info($"[{System.IO.Path.GetFileName(currentState.Url.LocalPath)}] Update found. Downloading...");
                var downloadRequest = WebRequest.CreateDefault(currentState.Url);
                downloadRequest.Method = WebRequestMethods.Http.Get;
                using(WebResponse downloadResponse = downloadRequest.GetResponse()) {
                    currentState.DownloadPath = GetTargetFilePath(downloadRequest.RequestUri);
                    if(File.Exists(currentState.DownloadPath)) {
                        File.Delete(currentState.DownloadPath);
                    }
                    using(WebResponse downloadingResponse = downloadRequest.GetResponse())
                    using(Stream downloadStream = downloadingResponse.GetResponseStream())
                    using(var fileStream = new FileStream(currentState.DownloadPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: /*1 Mb*/1024 * 1024)) {
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
                _logger.Info($"[{System.IO.Path.GetFileName(currentState.Url.LocalPath)}] File downloaded.");
            } else {
                _logger.Info($"[{System.IO.Path.GetFileName(currentState.Url.LocalPath)}] No updates.");
            }
            return currentState;
        }

        static string GetTargetFilePath (Uri sourceFileUri) {
            string targetFileName = Path.Combine(AppSettings.TargetFilePath, Path.GetFileName(sourceFileUri.LocalPath));
            string targetFilePath = Path.Combine(_currentDir, targetFileName);
            return Path.GetFullPath(targetFilePath);
        }
    }

    sealed class DownloadingFileState {
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
            var another = obj as DownloadingFileState;
            return !object.ReferenceEquals(another, null)
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

        public static bool operator == (DownloadingFileState x, DownloadingFileState y) {
            return object.ReferenceEquals(x, y)
                || !object.ReferenceEquals(x, null) && !object.ReferenceEquals(y, null) && x.Equals(y);
        }

        public static bool operator != (DownloadingFileState x, DownloadingFileState y) {
            return !(x == y);
        }

        public static Dictionary<Uri, DownloadingFileState> ReadStatesByUrlFromFile (string stateFileName) {
            if(File.Exists(stateFileName)) {
                string json = File.ReadAllText(stateFileName);
                return JsonConvert.DeserializeObject<Dictionary<Uri, DownloadingFileState>>(json);
            }
            return new Dictionary<Uri, DownloadingFileState>();
        }

        public static void WriteStatesByUrlToFile (string stateFileName, IDictionary<Uri, DownloadingFileState> states) {
            string json = JsonConvert.SerializeObject(states, new JsonSerializerSettings { Formatting = Formatting.Indented });
            File.WriteAllText(stateFileName, json);
        }

        public static DownloadingFileState CreateFromWebResponse (Uri requestUrl, WebResponse webResponse) {
            return new DownloadingFileState {
                Url = requestUrl,
                MD5Hash = webResponse.Headers["Content-Md5"],
                Length = webResponse.Headers["Content-Length"],
                LastModified = webResponse.Headers["Last-Modified"]
            };
        }
    }
}
