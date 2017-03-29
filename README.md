# FileDownloader
### Build status
master | TBA
---|---
---

A simple tool which automatizes the process of files updates checking and files downloading always stored at the same URL. Operations are performed in the parallel way. Updates checking based on comparing actual values of specific HTTP response headers with their state from previous program run (values serialized into the `state.json` file).

A list of URLs to check and download is stored in a text file `addresses.txt` one address per line. Other settings (`FileDownloader.exe.config` file):
* **`addressesFileName`** - the text file which contains list of URLs to check updates and download (default: `addresses.txt`);
* **`targetFilePath`** - the path where downloaded files should be stored (default: `..`);

## Plans for future releases:
* Add checks for downloaded files when they are present and the `state.json` file isn't available;
* Merge all assemblies into the single executable file.