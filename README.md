<h1 align="center">Jellyfin Merge Versions Plugin</h1>
<h3 align="center">Part of the <a href="https://jellyfin.org">Jellyfin Project</a></h3>

<p align="center">
Jellyfin Merge Versions plugin is a plugin that automatically groups every repeated and episode.

This is a simplified rewritten version of [Merge Versions](https://github.com/danieladov/jellyfin-plugin-mergeversions) from danieladov.

Since movie is already done by Jellyfin, this does not do movies.
This just follow the Show rules in the documentation as:
Show Name SXXEYY Episode Name - Tag1 - Tag2.extension

The way it works is anything past SXX or SXXX up until the first space is counted for. So any files with the same name up until that point are considered equal.

Show Name S00E01 Test - 1080p is the same as ShowName S00E01 - 720p
But different from
Show Name S00E02 Test - 1080p or ShowName S01E01 Test - 1080p

</p>

## Install Process


## From Repository
1. In jellyfin, go to dashboard -> plugins -> Repositories -> add and paste this link https://raw.githubusercontent.com/PedroLoures/JellyfinPluginManifest/master/manifest.json
2. Go to Catalog and search for the plugin you want to install
3. Click on it and install
4. Restart Jellyfin


## From .zip file
1. Download the .zip file from release page
2. Extract it and place the .dll file in a folder called ```plugins/Merge Versions``` under  the program data directory or inside the portable install directory
3. Restart Jellyfin

## User Guide
1. To merge your movies or episodes you can do it from Schedule task or directly from the configuration of the plugin.
2. Spliting is only avaible through the configuration



## Build Process
1. Clone or download this repository
2. Ensure you have .NET Core SDK setup and installed
3. Build plugin with following command.
```sh
dotnet publish --configuration Release --output bin
```
4. Place the resulting .dll file in a folder called ```plugins/Merge versions``` under  the program data directory or inside the portable install directory


