![GitHub Downloads (all assets, all releases)](https://img.shields.io/github/downloads/DiabeloDev/AutoUpdate/total?style=for-the-badge) <br>
![Latest](https://img.shields.io/github/v/release/DiabeloDev/AutoUpdate?style=for-the-badge&label=Latest%20Release&color=%23D91656) <br>

# AutoUpdate for EXILED

A powerful plugin that automatically updates your other EXILED plugins by downloading them directly from their GitHub releases. Set it up once and never worry about outdated plugins again.

## ✨ Core Features

-   **Automatic Updates:** Downloads the latest version of plugins directly from their GitHub release assets.
-   **Scheduled Checks:** Automatically checks for updates on a configurable schedule, perfect for servers with long uptimes.
-   **Discord Notifications:** Sends detailed, formatted update summaries to a Discord webhook.
-   **Configuration via File:** Easily manage a list of plugins to watch using a simple `repositories.json` file.
-   **Dynamic Integration:** Other plugins can register themselves for updates without needing to be in the config file.
-   **GitHub API Support:** Optionally use a GitHub Personal Access Token (PAT) to increase API rate limits.
-   **In-Game Commands:** Check for updates and list configured plugins directly from the server console or Remote Admin.

## ⚙️ Installation & Setup

### Step 1: Download & Install
1.  Download the latest version from the [**Releases Tab**](https://github.com/DiabeloDev/AutoUpdate/releases/latest).
2.  Place the `AutoUpdate.dll` file into your server's `EXILED/Plugins` folder.
3.  Place the `Newtonsoft.Json.dll` dependency into the `EXILED/Plugins/dependencies` folder.
4.  Restart the server once to generate the necessary configuration files.

### Step 2: Configure Repositories
After the first launch, a `repositories.json` file will be created. Open it and add the plugins you want to keep updated.

<details>
<summary><b>➡️ Click to see EXILED/Configs/AutoUpdate/repositories.json example</b></summary>

```json
{
  "SCPStats": {
    "user": "PintTheDragon",
    "repository": "SCPStats"
  },
  "ExamplePluginWithSpecificFile": {
    "user": "YourUser",
    "repository": "YourRepo",
    "fileName": "ExamplePlugin-Exiled.dll"
  },
  "AnotherPlugin": {
    "user": "AnotherDev",
    "repository": "AnotherRepo"
  }
}
```
- **`user`**: The GitHub username or organization.
- **`repository`**: The name of the repository.
- **`fileName`** (Optional): The specific `.dll` file to download from the release. If omitted, the first `.dll` found will be used.
</details>

### Step 3: Configure GitHub Token (Optional but Recommended)
To avoid hitting GitHub's API rate limits, especially with many plugins, you can use a Personal Access Token (PAT).

<details>
<summary><b>➡️ Click to see EXILED/Configs/AutoUpdate/github.json example</b></summary>

```json
{
  "enabled": true,
  "token": "Your-GitHub-PAT-Here"
}
```
1.  [Generate a new PAT](https://github.com/settings/tokens) with no special scopes (public repository access is sufficient).
2.  Paste the token into the `token` field and set `enabled` to `true`.
</details>

## 🚀 In-Game Commands

The following commands can be used from the server console or Remote Admin panel.

| Command             | Alias | Permission | Description                                           |
| ------------------- | ----- | ---------- | ----------------------------------------------------- |
| `autoupdate check`  | `au check` | `au.check` | Manually triggers a check for all configured plugins. |
| `autoupdate list`   | `au list`  | `au.list`  | Shows all plugins configured for updates and their source. |
| `autoupdate info <PluginName>` | `au info`  | `au.info`  | Displays detailed configuration info for a specific plugin. |


## 💻 For Developers: Integrating with AutoUpdate

Allow users to automatically update your plugin by integrating it with AutoUpdate. There are two methods available.

### Dynamic Integration (🚀 Highly Recommended - Soft Dependency)
This method allows your plugin to register with AutoUpdate if it's installed, but **does not require it**. If AutoUpdate is missing, your plugin will work perfectly fine without any errors. This is the best approach for public plugins.

**How to implement:** Place the following code in your plugin's main class. **Crucially, the call to `TryRegisterWithAutoUpdate()` must be inside `OnEnabled()`, not the constructor.**

<details>
<summary><b>➡️ Click to see the full, safe code example</b></summary>

```csharp
using Exiled.API.Features;
using System;
using System.Reflection;

public class YourPlugin : Plugin<Config>
{
    // ... your properties like Name, Author, Version

    public override void OnEnabled()
    {
        // Place the call here, after other initializations.
        TryRegisterWithAutoUpdate();

        base.OnEnabled();
    }

    /// <summary>
    /// A safe method to integrate with AutoUpdate.
    /// It checks for the plugin's existence at every step to prevent errors.
    /// </summary>
    private void TryRegisterWithAutoUpdate()
    {
        try
        {
            // First, check if the AutoUpdate plugin is loaded by EXILED.
            if (Exiled.Loader.Loader.GetPlugin("AutoUpdate") == null)
            {
                Log.Debug("AutoUpdate plugin not found. Skipping integration.");
                return;
            }

            // Next, get the Type of the 'Updater' class. This will be null if it's not found.
            Type updaterType = Type.GetType("AutoUpdate.Updater, AutoUpdate");
            if (updaterType == null)
            {
                Log.Warn("AutoUpdate plugin was found, but its 'Updater' class could not be loaded.");
                return;
            }

            // Now, get the registration method from the Updater class.
            MethodInfo registerMethod = updaterType.GetMethod("RegisterPluginForUpdates", BindingFlags.Public | BindingFlags.Static);
            if (registerMethod == null)
            {
                Log.Warn("Found AutoUpdate's 'Updater' class, but the 'RegisterPluginForUpdates' method is missing.");
                return;
            }
            
            // If all checks passed, prepare the parameters and invoke the method.
            object[] parameters = new object[]
            {
                Name,                  // Your plugin's name
                "YourGitHubUsername",       // Your GitHub username or organization
                "YourPluginRepository",     // The name of your plugin's repository
                "YourPluginFile.dll"        // (Optional but recommended) The specific .dll file name in your release
            };

            registerMethod.Invoke(null, parameters);
            
            Log.Info("Successfully registered with AutoUpdate for automatic updates!");
        }
        catch (Exception ex)
        {
            // This is a final safety net for any unexpected errors.
            Log.Error($"An unexpected error occurred while trying to integrate with AutoUpdate: {ex.Message}");
        }
    }
}
```
</details>

### Static Integration (⚠️ Advanced - Hard Dependency)
This method is simpler but creates a **hard dependency**. Your plugin **will not load** and will cause an error if the user does not have `AutoUpdate.dll` installed.

1.  **Add a reference** to `AutoUpdate.dll` in your C# project.
2.  **Call the method directly** in your `OnEnabled` method:
    ```csharp
    public override void OnEnabled()
    {
        AutoUpdate.Updater.RegisterPluginForUpdates(this.Name, "User", "Repo", "File.dll");
        base.OnEnabled();
    }
    ```

### Comparison

| Feature            | Dynamic Integration (Soft)                        | Static Integration (Hard)                             |
| ------------------ | ------------------------------------------------- | ----------------------------------------------------- |
| **User Experience**| ✅ **Excellent:** Works whether AutoUpdate is present or not. | ❌ **Poor:** Plugin crashes if AutoUpdate is missing. |
| **Requirement**    | AutoUpdate is completely optional.                | AutoUpdate is **required** to be installed.           |
| **Recommendation** | **Strongly Recommended for all public plugins.**  | Not recommended unless for private server setups.     |

## 📄 Main Plugin Configuration
You can configure the main settings for AutoUpdate in your EXILED config files.

```yaml
is_enabled: true
debug: false
# Path to repositories config file
repositories_config_path: '/home/container/.config/EXILED/Configs/AutoUpdate/repositories.json'
# Path to GitHub config file
git_hub_config_path: '/home/container/.config/EXILED/Configs/AutoUpdate/github.json'
# Run updater at start
run_updater_at_start: true
# --- Schedule Settings ---
# Enable periodic update checks.
schedule_enabled: false
# How often (in hours) should the updater check for new plugin versions? Minimum: 1
check_interval_hours: 12
# --- Discord Webhook Settings ---
# Enable sending update summaries to a Discord webhook.
discord_webhook_enabled: true
# The URL of the Discord webhook to send notifications to.
discord_webhook_url: ''
# The username for the webhook.
webhook_username: 'AutoUpdate'
# --- Analytics Settings ---
# To help the developer improve the plugin, anonymous data can be sent.
# Set the level of data you are willing to share. This is highly appreciated!
# 1=Full, 2=PluginsOnly, 3=Anonymousm 4=None. Default: 3 (Anonymous).
analytics_consent_level: None
```

## 💬 Support
For support, please:
- [Open an issue on GitHub](https://github.com/DiabeloDev/AutoUpdate/issues)
- Contact the author directly

## ⚖️ License
This project is licensed under the MIT License – see the [LICENSE](LICENSE) file for details.
