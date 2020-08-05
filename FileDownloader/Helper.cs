using System;

namespace FileDownloader {
    public static class Helper {

        public static Uri CreateUrl (string url) {
            Uri result;
            if(!Uri.TryCreate(url, UriKind.Absolute, out result) && !Uri.TryCreate(url, UriKind.Relative, out result)) {
                return null;
            }
            return result;
        }
    }
}
