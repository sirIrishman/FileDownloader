# FileDownloader

## Build status

master | ![status](https://travis-ci.com/sirIrishman/FileDownloader.svg?branch=master)
---|---
---

A simple tool which automatizes the process of files updates checking and files downloading always stored at the same URL. Operations are performed in the parallel way. Updates checking based on comparing actual values of specific HTTP response headers with their state from previous program run (values serialized into the `state.json` file).

A list of URLs to check and download with optional post-download actions to trigger are stored in a file `tasks.json`. Other settings (`FileDownloader.exe.config` file):

* **`downloadTasksFileName`** - a JSON file which contains list of URLs to check updates and download (default: `tasks.json`).
* **`targetDirectoryPath`** - a path where downloaded files should be stored into (default: `..`).

## `tasks.json` format example

```
[
    {
        "url": "https://website1.com/files/archive.zip",
        "action": { "command": "7z", "args": "x -aoa \"{download}\" -o\"d:\\tools\\tool1\"", "keep_file": "false" }
    },
    {
        "url": "http://website2.net/install.exe",
        "action": { "command": "{download}", "args": "/S", "keep_file": "false" }
    },
    {
        "url": "https://www.website3.org/ftp/3.7.3/setup-3.7.3-amd64.exe"
    }
]
```

## Plans for future releases:

* Add checks for downloaded files when they are present and the `state.json` file isn't available.
* Merge all assemblies into the single executable file.
* Implement version templates in URLs to find new versions with necessity to update URL