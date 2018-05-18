using Oxide.Core;
using Oxide.Core.Configuration;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Steamworks;
using System.Linq;
using TheForest.Utils;
using UnityEngine;

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

            // Check Covalence for player's name
            IPlayer iplayer = Covalence.PlayerManager.FindPlayerById(idString);
            string name = iplayer != null ? iplayer.Name : "Unnamed";

            // Let covalence know
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
                CoopKickToken coopKickToken = new CoopKickToken
                {
                    KickMessage = canLogin is string ? canLogin.ToString() : "Connection was rejected", // TODO: Localization
                    Banned = false
                };

                connection.Disconnect(coopKickToken);
                return true;
            }

            // Call game and covalence hooks
            return Interface.Call("OnUserApprove", connection) ?? Interface.Call("OnUserApproved", name, idString, ip); // TODO: Localization
        }

        /// <summary>
        /// Called when the player sends a message
        /// </summary>
        /// <param name="evt"></param>
        [HookMethod("IOnPlayerChat")]
        private object IOnPlayerChat(ChatEvent evt)
        {
            BoltEntity entity = Scene.SceneTracker.allPlayerEntities.FirstOrDefault(ent => ent.networkId == evt.Sender);

            if (entity == null)
            {
                return null;
            }

            if (evt.Message.Trim().Length <= 1)
            {
                return true;
            }

            ulong id = SteamDSConfig.clientConnectionInfo[entity.source.ConnectionId].m_SteamID;
            IPlayer iplayer = Covalence.PlayerManager.FindPlayerById(id.ToString());

            // Is it a chat command?
            string str = evt.Message.Substring(0, 1);
            if (!str.Equals("/") && !str.Equals("!"))
            {
                object chatSpecific = Interface.Call("OnPlayerChat", entity, evt.Message);
                object chatCovalence = Interface.Call("OnUserChat", iplayer, evt.Message);

                if (chatSpecific != null || chatCovalence != null)
                {
                    return true;
                }

                Debug.Log($"[Chat] {iplayer.Name}: {evt.Message}");
                return null;
            }

            // Replace ! and / for Covalence handling
            evt.Message = '/' + evt.Message.Substring(1);

            // Call the game hook
            if (Interface.Call("OnPlayerCommand", entity, str) != null)
            {
                return true;
            }

            // Is this a covalence command?
            if (Covalence.CommandSystem.HandleChatMessage(iplayer, evt.Message))
            {
                return true;
            }

            // Get the command and parse it
            string cmd;
            string[] args;
            string command = evt.Message.Substring(1);
            ParseCommand(command, out cmd, out args);
            if (cmd == null)
            {
                return null;
            }

            // TODO: Handle non-Covalence commands

            //iplayer.Reply(string.Format(lang.GetMessage("UnknownCommand", this, id.ToString()), cmd));

            return null;
        }

        /// <summary>
        /// Called when the player has connected
        /// </summary>
        /// <param name="entity"></param>
        [HookMethod("OnPlayerConnected")]
        private void OnPlayerConnected(BoltEntity entity)
        {
            string id = SteamDSConfig.clientConnectionInfo[entity.source.ConnectionId].m_SteamID.ToString();

            // Check Covalence for player's name
            IPlayer iplayer = Covalence.PlayerManager.FindPlayerById(id);
            string name = entity.GetState<IPlayerState>().name?.Sanitize() ?? (iplayer != null ? iplayer.Name : "Unnamed");

            // Set player name if available
            entity.GetState<IPlayerState>().name = name;

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

            // Let covalence know
            Covalence.PlayerManager.PlayerConnected(entity);

            // Call universal hook
            if (iplayer != null)
            {
                Interface.Call("OnUserConnected", iplayer);
            }

            Debug.Log($"{id}/{name} joined");
        }

        /// <summary>
        /// Called when the player has disconnected
        /// </summary>
        /// <param name="connection"></param>
        [HookMethod("IOnPlayerDisconnected")]
        private void IOnPlayerDisconnected(BoltConnection connection)
        {
            BoltEntity entity = Scene.SceneTracker.allPlayerEntities.FirstOrDefault(ent => ent.source.ConnectionId == connection.ConnectionId);
            string id = SteamDSConfig.clientConnectionInfo[connection.ConnectionId].m_SteamID.ToString();

            if (entity == null)
            {
                return;
            }

            // Call game-specific hook
            Interface.Call("OnPlayerDisconnected", entity);

            // Call universal hook
            IPlayer iplayer = Covalence.PlayerManager.FindPlayerById(id);
            if (iplayer != null)
            {
                Interface.Call("OnUserDisconnected", iplayer, "Unknown"); // TODO: Localization
            }

            // Let covalence know
            Covalence.PlayerManager.PlayerDisconnected(entity);

            Debug.Log($"{id}/{entity.GetState<IPlayerState>().name?.Sanitize() ?? "Unnamed"} quit");
        }

        /// <summary>
        /// Called when the player spawns
        /// </summary>
        /// <param name="entity"></param>
        [HookMethod("OnPlayerSpawn")]
        private void OnPlayerSpawn(BoltEntity entity)
        {
            string id = SteamDSConfig.clientConnectionInfo[entity.source.ConnectionId].m_SteamID.ToString();

            // Call universal hook
            IPlayer iplayer = Covalence.PlayerManager.FindPlayerById(id);
            if (iplayer != null)
            {
                Interface.Call("OnUserSpawn", iplayer);
                iplayer.Name = entity.GetState<IPlayerState>().name?.Sanitize() ?? iplayer.Name;
            }
        }

        #endregion Player Hooks
    }
}
