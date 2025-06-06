![GitHub Downloads (all assets, all releases)](https://img.shields.io/github/downloads/DiabeloDev/AutoUpdate/total?style=for-the-badge) <br>
![Latest](https://img.shields.io/github/v/release/DiabeloDev/AutoUpdate?style=for-the-badge&label=Latest%20Release&color=%23D91656)

# AutoUpdate for EXILED

A powerful plugin that automatically updates your other EXILED plugins by downloading them directly from their GitHub releases.

## Requirements
- [EXILED](https://github.com/ExSLMod-Team/EXILED) v9.6.0  
- Newtonsoft.Json

## Installation
1. Download the latest version from the [Releases tab](https://github.com/DiabeloDev/AutoUpdate/releases/latest).
2. Place the plugin `AutoUpdate.dll` file into the `EXILED/Plugins` folder on your server.
3. Place the plugin `Newtonsoft.Json.dll` file into the `EXILED/Plugins/dependencies` folder on your server.
4. Run the server once to generate configuration files.
5. Configure the `repositories.json` file (details below).
```json
{
  "SCPStats": {
    "user": "PintTheDragon",
    "repository": "SCPStats",
    "fileName": null
  },
  "ExamplePluginWithSpecificFile": {
    "user": "YourUser",
    "repository": "YourRepo",
    "fileName": "ExamplePlugin-Exiled.dll"
  }
}
```
6. Restart the server.

## Configuration
```yaml
auto_update:
  debug: false
  # Path to the JSON file containing the list of plugins to update. Format: {"PluginName": "GitHubUser/RepoName"}
  repositories_config_path: '/home/container/.config/EXILED/Configs/AutoUpdate/repositories.json'
  # Run updater at start
  run_updater_at_start: true
```

## Support
For support, please:
- [Open an issue here](https://github.com/DiabeloDev/AutoUpdate/issues)
- Contact the author directly

## License
This project is licensed under the MIT License – see the [LICENSE](LICENSE) file for details.
