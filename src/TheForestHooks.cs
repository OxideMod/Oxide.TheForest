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
            string idString = cSteamId.ToString();
            ulong id = cSteamId.m_SteamID;

            // Check for existing player's name
            IPlayer iplayer = Covalence.PlayerManager.FindPlayerById(idString);
            string name = !string.IsNullOrEmpty(iplayer?.Name) ? iplayer.Name : "Unnamed";

            // Handle universal player joining
            Covalence.PlayerManager.PlayerJoin(id, name);

            // Get IP address from Steam
            P2PSessionState_t sessionState;
            SteamGameServerNetworking.GetP2PSessionState(cSteamId, out sessionState);
            uint remoteIp = sessionState.m_nRemoteIP;
            string ip = string.Concat(remoteIp >> 24 & 255, ".", remoteIp >> 16 & 255, ".", remoteIp >> 8 & 255, ".", remoteIp & 255);

            // Call out and see if we should reject
            object canLogin = Interface.Call("CanClientLogin", connection) ?? Interface.Call("CanUserLogin", name, idString, ip); // TODO: Localization
            if (canLogin is string || canLogin is bool && !(bool)canLogin)
            {
                // Create kick token for player
                CoopKickToken coopKickToken = new CoopKickToken
                {
                    KickMessage = canLogin is string ? canLogin.ToString() : "Connection was rejected", // TODO: Localization
                    Banned = false
                };

                // Disconnect player using kick token
                connection.Disconnect(coopKickToken);
                return true;
            }

            // Call game and covalence hooks
            return Interface.Call("OnUserApprove", connection) ?? Interface.Call("OnUserApproved", name, idString, ip); // TODO: Localization
        }

        /// <summary>
        /// Called when the player has connected
        /// </summary>
        /// <param name="entity"></param>
        [HookMethod("OnPlayerConnected")]
        private void OnPlayerConnected(BoltEntity entity)
        {
            string id = SteamDSConfig.clientConnectionInfo[entity.source.ConnectionId].m_SteamID.ToString();
            IPlayer iplayer = Covalence.PlayerManager.FindPlayerById(id);
            string name = entity.GetState<IPlayerState>().name?.Sanitize() ?? (!string.IsNullOrEmpty(iplayer?.Name) ? iplayer.Name : "Unnamed");

            // Update player's permissions group and name
            if (permission.IsLoaded)
            {
                OxideConfig.DefaultGroups defaultGroups = Interface.Oxide.Config.Options.DefaultGroups;

                if (!permission.UserHasGroup(id, defaultGroups.Players))
                {
                    permission.AddUserGroup(id, defaultGroups.Players);
                }

                if (entity.source.IsDedicatedServerAdmin() && !permission.UserHasGroup(id, defaultGroups.Administrators))
                {
                    permission.AddUserGroup(id, defaultGroups.Administrators);
                }

                permission.UpdateNickname(id, name);
            }

            // Handle universal player connecting
            Covalence.PlayerManager.PlayerConnected(entity);

            // Get updated IPlayer after entity is set
            iplayer = Covalence.PlayerManager.FindPlayerById(id);

            if (iplayer != null)
            {
                // Set IPlayer for BoltEntity
                entity.IPlayer = iplayer;

                // Call universal hook
                Interface.Call("OnUserConnected", iplayer);
            }

            Interface.Oxide.LogInfo($"{id}/{name} joined");
        }

        /// <summary>
        /// Called when the player spawns
        /// </summary>
        /// <param name="entity"></param>
        [HookMethod("OnPlayerSpawn")]
        private void OnPlayerSpawn(BoltEntity entity)
        {
            IPlayer iplayer = entity.IPlayer;
            if (iplayer != null)
            {
                // Set player name if available
                iplayer.Name = entity.GetState<IPlayerState>().name?.Sanitize() ?? (!string.IsNullOrEmpty(iplayer.Name) ? iplayer.Name : "Unnamed");

                // Call universal hook
                Interface.Call("OnUserSpawn", iplayer);
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

            IPlayer iplayer = entity.IPlayer;
            if (iplayer == null)
            {
                return null;
            }

            // Set player name if available
            iplayer.Name = entity.GetState<IPlayerState>().name?.Sanitize() ?? (!string.IsNullOrEmpty(iplayer.Name) ? iplayer.Name : "Unnamed");

            // Is it a chat command?
            string str = evt.Message.Substring(0, 1);
            if (!str.Equals("/") && !str.Equals("!"))
            {
                // Call the hooks for plugins
                object chatUniversal = Interface.Call("OnUserChat", iplayer, evt.Message);
                object chatSpecific = Interface.Call("OnPlayerChat", entity, evt.Message);
                if (chatUniversal != null || chatSpecific != null)
                {
                    return true;
                }

                Interface.Oxide.LogInfo($"[Chat] {iplayer.Name}: {evt.Message}");
                return null;
            }

            // Replace ! with / for Covalence handling
            evt.Message = '/' + evt.Message.Substring(1);

            // Get the command and parse it
            string cmd;
            string[] args;
            ParseCommand(evt.Message.Substring(1), out cmd, out args);
            if (cmd == null)
            {
                return null;
            }

            // Call the hooks for plugins
            object commandUniversal = Interface.Call("OnUserCommand", iplayer, cmd, args);
            object commandSpecific = Interface.Call("OnPlayerCommand", entity, cmd, args);
            if (commandUniversal != null || commandSpecific != null)
            {
                return true;
            }

            // Is this a covalence command?
            if (Covalence.CommandSystem.HandleChatMessage(iplayer, evt.Message))
            {
                return true;
            }

            // TODO: Handle non-universal commands

            iplayer.Reply(string.Format(lang.GetMessage("UnknownCommand", this, iplayer.Id), cmd));
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

            IPlayer iplayer = entity.IPlayer;
            if (iplayer == null)
            {
                return;
            }

            // Set player name if available
            iplayer.Name = entity.GetState<IPlayerState>().name?.Sanitize() ?? (!string.IsNullOrEmpty(iplayer.Name) ? iplayer.Name : "Unnamed");

            // Call hooks for plugins
            Interface.Call("OnUserDisconnected", iplayer, "Unknown"); // TODO: Localization
            Interface.Call("OnPlayerDisconnected", entity, "Unknown"); // TODO: Localization

            // Handle universal player disconnecting
            Covalence.PlayerManager.PlayerDisconnected(entity);

            Interface.Oxide.LogInfo($"{iplayer.Id}/{iplayer.Name} quit");
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

                // Call the hook for plugins
                if (Interface.Call("OnServerCommand", command, args) != null)
                {
                    return true;
                }

                // Is this a covalence command?
                IPlayer iplayer = new TheForestConsolePlayer();
                if (Covalence.CommandSystem.HandleConsoleMessage(iplayer, $"{command} {data}"))
                {
                    return true;
                }

                // TODO: Handle non-Covalence commmands

                iplayer.Reply(string.Format(lang.GetMessage("UnknownCommand", this, iplayer.Id), command));
            }

            return true;
        }

        #endregion Server Hooks
    }
}
