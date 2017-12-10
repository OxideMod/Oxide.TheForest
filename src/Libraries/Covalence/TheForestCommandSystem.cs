﻿using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using System.Collections.Generic;

namespace Oxide.Game.TheForest.Libraries.Covalence
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
        private CommandHandler commandHandler;

        // All registered commands
        private IDictionary<string, CommandCallback> registeredCommands;

        /// <summary>
        /// Initializes the command system
        /// </summary>
        public TheForestCommandSystem()
        {
            registeredCommands = new Dictionary<string, CommandCallback>();
            commandHandler = new CommandHandler(ChatCommandCallback, registeredCommands.ContainsKey);
            consolePlayer = new TheForestConsolePlayer();
        }

        private bool ChatCommandCallback(IPlayer caller, string command, string[] args)
        {
            CommandCallback callback;
            return registeredCommands.TryGetValue(command, out callback) && callback(caller, command, args);
        }

        #endregion Initialization

        #region Command Registration

        /// <summary>
        /// Registers the specified command
        /// </summary>
        /// <param name="command"></param>
        /// <param name="callback"></param>
        public void RegisterCommand(string command, Plugin plugin, CommandCallback callback)
        {
            // Convert to lowercase
            var commandName = command.ToLowerInvariant();

            // Check if it already exists
            if (registeredCommands.ContainsKey(commandName))
                throw new CommandAlreadyExistsException(commandName);

            registeredCommands.Add(commandName, callback);
        }

        /// <summary>
        /// Unregisters the specified command
        /// </summary>
        /// <param name="command"></param>
        public void UnregisterCommand(string command, Plugin plugin) => registeredCommands.Remove(command);

        /// <summary>
        /// Handles a chat message
        /// </summary>
        /// <param name="player"></param>
        /// <param name="message"></param>
        /// <returns></returns>
        public bool HandleChatMessage(IPlayer player, string message) => commandHandler.HandleChatMessage(player, message);

        #endregion Command Registration
    }
}
