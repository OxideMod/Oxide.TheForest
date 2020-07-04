using Oxide.Core;
using Oxide.Core.Configuration;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Steamworks;
using System.Linq;
using TheForest.Utils;

namespace Oxide.Game.TheForest
{
    /// <summary>
    /// Game hooks and wrappers for the core The Forest plugin
    /// </summary>
    public partial class TheForestCore
    {
        #region Player Hooks

        /// <summary>
        /// Called when the player is attempting to connect
        /// </summary>
        /// <param name="connection"></param>
        /// <returns></returns>
        [HookMethod("IOnUserApprove")]
        private object IOnUserApprove(BoltConnection connection)
        {
            CSteamID cSteamId = SteamDSConfig.clientConnectionInfo[connection.ConnectionId];
            string playerId = cSteamId.ToString();
            ulong steamId = cSteamId.m_SteamID;

            // Check for existing player's name
            IPlayer player = Covalence.PlayerManager.FindPlayerById(playerId);
            string name = !string.IsNullOrEmpty(player?.Name) ? player.Name : "Unnamed";

            // Handle universal player joining
            Covalence.PlayerManager.PlayerJoin(steamId, name);

            // Get IP address from Steam
            SteamGameServerNetworking.GetP2PSessionState(cSteamId, out P2PSessionState_t sessionState);
            uint remoteIp = sessionState.m_nRemoteIP;
            string playerIp = string.Concat(remoteIp >> 24 & 255, ".", remoteIp >> 16 & 255, ".", remoteIp >> 8 & 255, ".", remoteIp & 255);

            // Call out and see if we should reject
            object loginSpecific = Interface.Call("CanClientLogin", connection);
            object loginCovalence = Interface.Call("CanUserLogin", name, playerId, playerIp);
            object canLogin = loginSpecific is null ? loginCovalence : loginSpecific;
            if (!serverInitialized || canLogin is string || canLogin is bool loginBlocked && !loginBlocked)
            {
                // Create kick token for player
                CoopKickToken coopKickToken = new CoopKickToken
                {
                    KickMessage = !serverInitialized ? "Server not initialized yet" : canLogin is string ? canLogin.ToString() : "Connection was rejected", // TODO: Localization
                    Banned = false
                };

                // Disconnect player using kick token
                connection.Disconnect(coopKickToken);
                return true;
            }

            // Call hooks for plugins
            object approvedSpecific = Interface.Call("OnUserApprove", connection);
            object approvedCovalence = Interface.Call("OnUserApproved", name, playerId, playerIp);
            return approvedSpecific is null ? approvedCovalence : approvedSpecific;
        }

        /// <summary>
        /// Called when the player has connected
        /// </summary>
        /// <param name="connection"></param>
        [HookMethod("IOnPlayerConnected")]
        private void IOnPlayerConnected(BoltConnection connection)
        {
            string playerId = SteamDSConfig.clientConnectionInfo[connection.ConnectionId].m_SteamID.ToString();
            IPlayer player = Covalence.PlayerManager.FindPlayerById(playerId);
            string playerName = !string.IsNullOrEmpty(player?.Name) ? player.Name : "Unnamed"; // TODO: Localization

            // Update name and groups with permissions
            if (permission.IsLoaded)
            {
                permission.UpdateNickname(playerId, playerName);
                OxideConfig.DefaultGroups defaultGroups = Interface.Oxide.Config.Options.DefaultGroups;
                if (!permission.UserHasGroup(playerId, defaultGroups.Players))
                {
                    permission.AddUserGroup(playerId, defaultGroups.Players);
                }
                if (connection.IsDedicatedServerAdmin() && !permission.UserHasGroup(playerId, defaultGroups.Administrators))
                {
                    permission.AddUserGroup(playerId, defaultGroups.Administrators);
                }
            }

            if (player != null)
            {
                // Call hooks for plugins
                Interface.Call("OnPlayerConnected", connection);
                Interface.Call("OnUserConnected", player);
            }
        }

        /// <summary>
        /// Called when the player has respawned
        /// </summary>
        /// <param name="entity"></param>
        [HookMethod("IOnPlayerRespawn")]
        private void IOnPlayerRespawn(BoltEntity entity)
        {
            // Handle universal player connecting
            Covalence.PlayerManager.PlayerConnected(entity);

            string playerId = SteamDSConfig.clientConnectionInfo[entity.source.ConnectionId].m_SteamID.ToString();
            IPlayer player = Covalence.PlayerManager.FindPlayerById(playerId);
            if (player != null)
            {
                // Set IPlayer for BoltEntity
                entity.IPlayer = player;

                // Get updated IPlayer after entity is set
                Covalence.PlayerManager.FindPlayerById(playerId);

                // Call hooks for plugins
                Interface.Call("OnPlayerRespawn", entity);
                Interface.Call("OnUserRespawn", player);
            }
        }

        /// <summary>
        /// Called when the player spawns
        /// </summary>
        /// <param name="entity"></param>
        [HookMethod("OnPlayerSpawn")]
        private void OnPlayerSpawn(BoltEntity entity)
        {
            IPlayer player = entity.IPlayer;
            if (player != null)
            {
                // Set player name if available
                player.Name = entity.GetState<IPlayerState>().name?.Sanitize() ?? (!string.IsNullOrEmpty(player.Name) ? player.Name : "Unnamed"); // TODO: Localization

                // Call hooks for plugins
                Interface.Call("OnUserSpawn", player);
            }
        }

        /// <summary>
        /// Called when the player sends a message
        /// </summary>
        /// <param name="evt"></param>
        [HookMethod("IOnPlayerChat")]
        private object IOnPlayerChat(ChatEvent evt)
        {
            if (evt.Message.Trim().Length <= 1)
            {
                return true;
            }

            BoltEntity entity = BoltNetwork.FindEntity(evt.Sender);
            if (entity == null)
            {
                return null;
            }

            IPlayer player = entity.IPlayer;
            if (player == null)
            {
                return null;
            }

            // Set player name if available
            player.Name = entity.GetState<IPlayerState>().name?.Sanitize() ?? (!string.IsNullOrEmpty(player.Name) ? player.Name : "Unnamed"); // TODO: Localization

            // Is it a chat command?
            string str = evt.Message.Substring(0, 1);
            if (!str.Equals("/") && !str.Equals("!"))
            {
                // Call hooks for plugins
                object chatSpecific = Interface.Call("OnPlayerChat", entity, evt.Message);
                object chatCovalence = Interface.Call("OnUserChat", player, evt.Message);
                object canChat = chatSpecific is null ? chatCovalence : chatSpecific;
                if (canChat != null)
                {
                    return true;
                }

                Interface.Oxide.LogInfo($"[Chat] {player.Name}: {evt.Message}");
                return null;
            }

            // Replace ! with / for Covalence handling
            evt.Message = '/' + evt.Message.Substring(1);

            // Parse the command
            ParseCommand(evt.Message.Substring(1), out string cmd, out string[] args);
            if (cmd == null)
            {
                return null;
            }

            // Call hooks for plugins
            object commandSpecific = Interface.Call("OnPlayerCommand", entity, cmd, args);
            object commandCovalence = Interface.Call("OnUserCommand", player, cmd, args);
            object canBlock = commandSpecific is null ? commandCovalence : commandSpecific;
            if (canBlock != null)
            {
                return true;
            }

            // Is this a covalence command?
            if (Covalence.CommandSystem.HandleChatMessage(player, evt.Message))
            {
                return true;
            }

            // TODO: Handle non-universal commands

            player.Reply(string.Format(lang.GetMessage("UnknownCommand", this, player.Id), cmd));
            return true;
        }

        /// <summary>
        /// Called when the player has disconnected
        /// </summary>
        /// <param name="connection"></param>
        [HookMethod("IOnPlayerDisconnected")]
        private void IOnPlayerDisconnected(BoltConnection connection)
        {
            BoltEntity entity = Scene.SceneTracker.allPlayerEntities.FirstOrDefault(ent => ent.source.ConnectionId == connection.ConnectionId);
            if (entity == null)
            {
                return;
            }

            IPlayer player = entity.IPlayer;
            if (player == null)
            {
                return;
            }

            // Set player name if available
            player.Name = entity.GetState<IPlayerState>().name?.Sanitize() ?? (!string.IsNullOrEmpty(player.Name) ? player.Name : "Unnamed"); // TODO: Localization

            // Call hooks for plugins
            Interface.Call("OnUserDisconnected", player, "Unknown"); // TODO: Localization
            Interface.Call("OnPlayerDisconnected", entity, "Unknown"); // TODO: Localization

            // Handle universal player disconnecting
            Covalence.PlayerManager.PlayerDisconnected(entity);

            Interface.Oxide.LogInfo($"{player.Id}/{player.Name} quit"); // TODO: Localization
        }

        #endregion Player Hooks

        #region Server Hooks

        /// <summary>
        /// Called when a command was run from the server
        /// </summary>
        /// <param name="connection"></param>
        /// <param name="command"></param>
        /// <param name="data"></param>
        /// <returns></returns>
        [HookMethod("IOnServerCommand")]
        private object IOnServerCommand(BoltConnection connection, string command, string data)
        {
            if (command.Length != 0)
            {
                // Create array of arguments
                string[] args = data.Split(' ');

                // Call hooks for plugins
                if (Interface.Call("OnServerCommand", command, args) != null)
                {
                    return true;
                }

                // Is it a valid command?
                IPlayer player = new TheForestConsolePlayer();
                if (!Covalence.CommandSystem.HandleConsoleMessage(player, $"{command} {data}"))
                {
                    player.Reply(string.Format(lang.GetMessage("UnknownCommand", this, player.Id), command));
                }

                return true;
            }

            return null;
        }

        #endregion Server Hooks
    }
}
