using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using System.Collections.Generic;
using System.Linq;

namespace Oxide.Game.TheForest
{
    /// <summary>
    /// Represents a binding to a generic command system
    /// </summary>
    public class TheForestCommandSystem : ICommandSystem
    {
        #region Initialization

        // The console player
        internal TheForestConsolePlayer consolePlayer;

        // Command handler
        private readonly CommandHandler commandHandler;

        // All registered commands
        internal IDictionary<string, RegisteredCommand> registeredCommands;

        // Registered commands
        internal class RegisteredCommand
        {
            /// <summary>
            /// The plugin that handles the command
            /// </summary>
            public readonly Plugin Source;

            /// <summary>
            /// The name of the command
            /// </summary>
            public readonly string Command;

            /// <summary>
            /// The callback
            /// </summary>
            public readonly CommandCallback Callback;

            /// <summary>
            /// Initializes a new instance of the RegisteredCommand class
            /// </summary>
            /// <param name="source"></param>
            /// <param name="command"></param>
            /// <param name="callback"></param>
            public RegisteredCommand(Plugin source, string command, CommandCallback callback)
            {
                Source = source;
                Command = command;
                Callback = callback;
            }
        }
        /// <summary>
        /// Initializes the command system
        /// </summary>
        public TheForestCommandSystem()
        {
            registeredCommands = new Dictionary<string, RegisteredCommand>();
            commandHandler = new CommandHandler(CommandCallback, registeredCommands.ContainsKey);
            consolePlayer = new TheForestConsolePlayer();
        }

        private bool CommandCallback(IPlayer caller, string cmd, string[] args)
        {
            return registeredCommands.TryGetValue(cmd, out RegisteredCommand command) && command.Callback(caller, cmd, args);
        }

        #endregion Initialization

        #region Command Registration

        /// <summary>
        /// Registers the specified command
        /// </summary>
        /// <param name="command"></param>
        /// <param name="plugin"></param>
        /// <param name="callback"></param>
        public void RegisterCommand(string command, Plugin plugin, CommandCallback callback)
        {
            // Convert command to lowercase and remove whitespace
            command = command.ToLowerInvariant().Trim();

            // Setup a new Covalence command
            RegisteredCommand newCommand = new RegisteredCommand(plugin, command, callback);

            // Check if the command can be overridden
            if (!CanOverrideCommand(command))
            {
                throw new CommandAlreadyExistsException(command);
            }

            // Check if command already exists in another Covalence plugin
            if (registeredCommands.TryGetValue(command, out RegisteredCommand cmd))
            {
                string previousPluginName = cmd.Source?.Name ?? "an unknown plugin";
                string newPluginName = plugin?.Name ?? "An unknown plugin";
                string message = $"{newPluginName} has replaced the '{command}' command previously registered by {previousPluginName}";
                Interface.Oxide.LogWarning(message);
            }

            // Register the command
            registeredCommands[command] = newCommand;
        }

        /// <summary>
        /// Unregisters the specified command
        /// </summary>
        /// <param name="command"></param>
        /// <param name="plugin"></param>
        public void UnregisterCommand(string command, Plugin plugin) => registeredCommands.Remove(command);

        /// <summary>
        /// Handles a chat message
        /// </summary>
        /// <param name="player"></param>
        /// <param name="message"></param>
        /// <returns></returns>
        public bool HandleChatMessage(IPlayer player, string message) => commandHandler.HandleChatMessage(player, message);

        /// <summary>
        /// Handles a console message
        /// </summary>
        /// <param name="player"></param>
        /// <param name="message"></param>
        /// <returns></returns>
        public bool HandleConsoleMessage(IPlayer player, string message) => commandHandler.HandleConsoleMessage(player, message);

        #endregion Command Registration

        #region Command Overriding

        /// <summary>
        /// Checks if a command can be overridden
        /// </summary>
        /// <param name="command"></param>
        /// <returns></returns>
        private bool CanOverrideCommand(string command)
        {
            string[] split = command.Split('.');
            string fullName = split.Length >= 2 ? string.Join(".", split.Skip(1).ToArray()) : split[0].Trim();

            if (registeredCommands.TryGetValue(command, out RegisteredCommand cmd))
            {
                if (cmd.Source.IsCorePlugin)
                {
                    return false;
                }
            }

            return !TheForestCore.RestrictedCommands.Contains(command) && !TheForestCore.RestrictedCommands.Contains(fullName);
        }

        #endregion Command Overriding

    }
}
