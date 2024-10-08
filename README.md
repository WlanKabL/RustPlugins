# Rust Server Plugin Collection

Welcome to the **Rust Server Plugin Collection**! This repository hosts a curated selection of custom plugins developed to extend your Rust server with advanced features and seamless management tools. Whether you're looking to enhance gameplay, improve administrative control, or create immersive experiences, this collection has something for every server owner.

## Overview

This repository organizes all the plugins in one place, making it easy to manage and deploy them on your Rust server. Each plugin serves a specific purpose, such as managing server events, enabling a player-driven economy, or even integrating advanced stage lighting systems for in-game concerts or festivals.

One of the standout features includes integration with advanced **stage lighting control** software, specifically the [Rust-LJ](https://github.com/WlanKabL/Rust-LJ) system, which is designed to handle real-time lighting effects and stage setups commonly seen at live events. These plugins help simplify the setup process or even act as middleware between the game server and lighting equipment.

## Installation

To install a plugin from this collection on your Rust server, follow these steps:

1. **Download the Plugin**: Navigate to the folder of the plugin you wish to install and download the `.cs` file.
2. **Upload the Plugin**: Upload the `.cs` file to the `oxide/plugins/` directory on your Rust server.
3. **Activate the Plugin**: Restart the server or run the following command to reload the plugin:
   ```
   oxide.reload PLUGIN_NAME
   ```
   Replace `PLUGIN_NAME` with the name of the plugin file (without the `.cs` extension).

4. **Configuration**: Some plugins may include configuration options, typically located in the `oxide/config/` directory. Open the configuration file, adjust the settings, and reload the plugin for the changes to take effect.

## Contributing

Contributions are welcome! If you'd like to contribute to this project:

1. Fork the repository.
2. Create a new branch for your feature or bugfix.
3. Test the plugin thoroughly to ensure it works as expected.
4. Submit a pull request, detailing your changes and improvements.

## Troubleshooting

If you encounter issues with any plugins, try these steps:

1. **Check Logs**: Review server logs in `oxide/logs/` for error messages related to the plugin.
2. **Ensure Compatibility**: Verify that the plugin is compatible with the latest versions of Rust and Oxide/uMod.
3. **Reload the Plugin**: If the plugin is not functioning correctly, reload it using:
   ```
   oxide.reload PLUGIN_NAME
   ```

## License

This project is licensed under the MIT License. You're free to use, modify, and distribute the plugins, provided you include proper attribution to the original authors.

